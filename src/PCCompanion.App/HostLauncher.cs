using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

/// <summary>
/// Starts the background host, installed next to the settings app, when it isn't running.
/// </summary>
public sealed partial class HostLauncher(ILogger<HostLauncher> logger)
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    public void StartIfNotRunning()
    {
        if (IsRunning())
        {
            LogAlreadyRunning(logger);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(HostProcess.ExecutablePath) { UseShellExecute = false });
            LogStarted(logger);
        }
        catch (Win32Exception exception)
        {
            LogCantStart(logger, exception, HostProcess.ExecutablePath);
        }
    }

    /// <summary>
    /// Asks the running host to exit, waits for it, and starts it again, for example after
    /// Always run as administrator changed.
    /// </summary>
    public async Task RestartAsync()
    {
        if (IsRunning())
        {
            MessageWindow.PostToWindow(HostProcess.WindowClassName, MessageWindow.RegisterMessage(HostProcess.ExitMessageName));
            var waited = Stopwatch.StartNew();
            while (IsRunning() && waited.Elapsed < ExitTimeout)
            {
                await Task.Delay(200).ConfigureAwait(true);
            }
        }

        StartIfNotRunning();
    }

    /// <summary>
    /// Whether the host runs without the admin rights that Always run as administrator gives it,
    /// because its hand-off to the scheduled task failed. See <see cref="HostProcess.RunsWithoutAdminRights"/>.
    /// </summary>
    public static bool RunsWithoutAdminRights(GeneralSettings general) =>
        HostProcess.RunsWithoutAdminRights(general, Elevation.IsUacLimited, IsHostElevated());

    private static bool IsRunning() => InstanceLock.IsHeld(HostProcess.InstanceLockName);

    // Read from the process that owns the host's window; null when no host runs or it can't be read.
    private static bool? IsHostElevated() =>
        MessageWindow.FindOwnerProcess(HostProcess.WindowClassName) is { } processId ? Elevation.IsProcessElevated(processId) : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "The background host is already running.")]
    private static partial void LogAlreadyRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the background host.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the background host at {Path}.")]
    private static partial void LogCantStart(ILogger logger, Win32Exception exception, string path);
}
