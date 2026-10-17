using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

public enum RunAsAdministratorResult
{
    Changed,
    Cancelled,
    Failed,
}

/// <summary>
/// Turns the Always run as administrator setting on and off: the elevated helper creates or
/// deletes the scheduled tasks after a UAC prompt, the setting is saved, and the host restarts.
/// See docs/features/general/run-as-administrator.md.
/// </summary>
public sealed partial class RunAsAdministrator(AppSettings settings, SettingsStore store, HostLauncher host, ILogger<RunAsAdministrator> logger)
{
    private const int Cancelled = 1223; // ERROR_CANCELLED: the user declined the UAC prompt.

    private static readonly string Folder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    private static readonly string Helper = Path.Combine(Folder, ElevatedStartup.HelperFileName);

    private readonly Lazy<string?> _unavailableReason = new(() =>
        !IsInstalledForAllUsers() ? Strings.GeneralRunAsAdministratorNeedsAllUsers
        : !Elevation.CanElevate ? Strings.GeneralRunAsAdministratorNeedsAdministrator
        : null);

    /// <summary>Why the setting can't be turned on, or <see langword="null"/> when it can.</summary>
    public string? UnavailableReason => _unavailableReason.Value;

    public bool IsOn => settings.General.AlwaysRunAsAdministrator;

    public async Task<RunAsAdministratorResult> SetAsync(bool on)
    {
        var startInfo = new ProcessStartInfo(Helper) { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add(on ? ElevatedStartup.CreateTasksCommand : ElevatedStartup.DeleteTasksCommand);
        startInfo.ArgumentList.Add(Elevation.CurrentUserSid);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return RunAsAdministratorResult.Failed;
            }

            await process.WaitForExitAsync().ConfigureAwait(true);
            if (process.ExitCode != 0)
            {
                LogHelperFailed(logger, process.ExitCode);
                return RunAsAdministratorResult.Failed;
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == Cancelled)
        {
            return RunAsAdministratorResult.Cancelled;
        }
        catch (Win32Exception exception)
        {
            LogCantStartHelper(logger, exception);
            return RunAsAdministratorResult.Failed;
        }

        settings.General.AlwaysRunAsAdministrator = on;
        try
        {
            store.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(logger, exception);
            return RunAsAdministratorResult.Failed;
        }

        LogChanged(logger, on);
        await host.RestartAsync().ConfigureAwait(true);
        return RunAsAdministratorResult.Changed;
    }

    // Folders that programs without admin rights can change would let them replace the host the
    // tasks start as administrator. A per-user install, or a build run from its output folder, is one.
    private static bool IsInstalledForAllUsers() =>
        ProtectedFiles.IsProtectedInstall(Folder, HostProcess.FileName, ElevatedStartup.HelperFileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Always run as administrator is now {On}.")]
    private static partial void LogChanged(ILogger logger, bool on);

    [LoggerMessage(Level = LogLevel.Error, Message = "The elevated helper failed with exit code {ExitCode}.")]
    private static partial void LogHelperFailed(ILogger logger, int exitCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the elevated helper.")]
    private static partial void LogCantStartHelper(ILogger logger, Win32Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't save Always run as administrator.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
