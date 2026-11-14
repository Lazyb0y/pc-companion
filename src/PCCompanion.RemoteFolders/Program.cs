using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// The process that serves Remote Folders' folders in File Explorer. The host starts it while the
/// feature is on, without admin rights, and it exits when the feature is turned off, when the host
/// asks, or when the host's process ends. See docs/features/files/remote-folders.md.
/// </summary>
internal static partial class Program
{
    private const int Stopped = 0;
    private const int RunsAsAdministrator = 1;
    private const string ResumeWindowClass = "PCCompanion.RemoteFolders.Power";

    private static int Main(string[] args)
    {
        var arguments = ProviderArguments.Parse(args);

        // One per user session; a second start does nothing.
        using var instanceLock = TryTakeInstanceLock();
        if (instanceLock is null)
        {
            return Stopped;
        }

        var paths = AppDataPaths.ForCurrentUser();
        using var detailedLogging = new DetailedLogging();
        using var loggers = LoggerFactory.Create(builder => builder.AddFileLog(paths.LogsFolder, "remote-folders", detailedLogging: detailedLogging));
        var logger = loggers.CreateLogger(typeof(Program));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(logger, e.ExceptionObject as Exception);

        // Code that reads a server's data never runs elevated; the host starts it without admin rights.
        if (Elevation.IsUacElevated)
        {
            LogRunsAsAdministrator(logger);
            return RunsAsAdministrator;
        }

        LogStarted(logger, Environment.OSVersion.Version, arguments.HostProcessId is not null);
        using var stop = new ManualResetEventSlim();
        using var stopSignal = SessionSignal.Listen(RemoteFoldersProcess.StopSignalName, () =>
        {
            LogStopAsked(logger);
            stop.Set();
        });
        WatchHost(arguments.HostProcessId, stop, logger);

        var store = new SettingsStore(paths, loggers.CreateLogger<SettingsStore>());
        using var recycleBin = new RecycleBin();
        var pc = new WindowsPlaceholderSystem(recycleBin);
        var sync = new WindowsSyncRootSystem();
        string sid = Elevation.CurrentUserSid;
        string icon = Environment.ProcessPath + ",0";
        using var service = new ProviderService(
            sync,
            sid,
            (server, number) => new ServerFolder(
                server,
                sid,
                icon,
                sync,
                pc,
                ServerConnections.ForSftp(server, ReadSecret, TimeProvider.System, loggers.CreateLogger<ServerConnections>(), number),
                Path.Combine(paths.Root, "remote-folders", server.Id + ".json"),
                TimeProvider.System,
                loggers.CreateLogger<ServerFolder>(),
                number),
            loggers.CreateLogger<ProviderService>());
        using var watcher = new SettingsWatcher(paths);
        var applying = new Lock();
        void Apply()
        {
            lock (applying)
            {
                if (Load(store, logger) is not { } settings)
                {
                    return;
                }

                detailedLogging.TurnOnUntil(settings.Diagnostics.DetailedLoggingUntil);

                // Conflict copies are named in the user's language.
                LanguageSetting.Apply(settings.General.Language, logger);
                var section = ProviderSettings.Read(settings, out var error);
                if (error is not null)
                {
                    LogUnreadableSettings(logger, error);
                }

                if (section is not { Enabled: true })
                {
                    LogTurnedOff(logger);
                    stop.Set();
                    return;
                }

                LogApplied(logger, section.Servers.Count, section.UsableServers.Count);
                service.Apply(section);
            }
        }

        // A server that couldn't be reached may be back after the network changes or the PC wakes.
        void TryAgainNow(string reason)
        {
            lock (applying)
            {
                LogTryingAgain(logger, reason);
                service.TryAgainNow();
            }
        }

        NetworkAddressChangedEventHandler networkChanged = (_, _) => TryAgainNow("network change");
        NetworkChange.NetworkAddressChanged += networkChanged;
        WatchResume(() => TryAgainNow("wake from sleep"), logger);

        watcher.Changed += (_, _) => Apply();
        Apply();

        stop.Wait();
        NetworkChange.NetworkAddressChanged -= networkChanged;
        LogStopped(logger);
        return Stopped;
    }

    // The host gives its process ID; when that process ends, for any reason, so does this one.
    private static void WatchHost(int? hostProcessId, ManualResetEventSlim stop, ILogger logger)
    {
        if (hostProcessId is not { } id)
        {
            return;
        }

        try
        {
            var host = Process.GetProcessById(id);
            _ = host.WaitForExitAsync().ContinueWith(
                _ =>
                {
                    LogHostEnded(logger);
                    stop.Set();
                    host.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
        catch (ArgumentException)
        {
            // The host already ended.
            LogHostEnded(logger);
            stop.Set();
        }
    }

    // Windows tells only windows that the PC woke, so a hidden one waits for it on a thread of its own.
    private static void WatchResume(Action resumed, ILogger logger)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new MessageWindow(ResumeWindowClass);
                using var notifications = new DeviceNotifications(window);
                notifications.Resumed += (_, _) => resumed();
                MessageLoop.Run();
            }
            catch (Exception exception)
            {
                LogResumeWatchFailed(logger, exception);
            }
        })
        {
            IsBackground = true,
            Name = "Wake from sleep",
        };
        thread.Start();
    }

    // A server's password or key passphrase; none when it's missing or can't be read.
    private static string? ReadSecret(string target)
    {
        try
        {
            return CredentialStore.Read(target);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static AppSettings? Load(SettingsStore store, ILogger logger)
    {
        try
        {
            return store.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantReadSettings(logger, exception);
            return null;
        }
    }

    private static Mutex? TryTakeInstanceLock()
    {
        try
        {
            var instanceLock = new Mutex(initiallyOwned: true, RemoteFoldersProcess.InstanceLockName, out var isFirstInstance);
            if (isFirstInstance)
            {
                return instanceLock;
            }

            instanceLock.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started on Windows {WindowsVersion}; started by the host: {ByHost}.")]
    private static partial void LogStarted(ILogger logger, Version windowsVersion, bool byHost);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Started with admin rights through UAC, so it stops: it never reads a server's data with admin rights.")]
    private static partial void LogRunsAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remote Folders settings applied: {Servers} servers, {Usable} of them usable.")]
    private static partial void LogApplied(ILogger logger, int servers, int usable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remote Folders is off, so the process stops.")]
    private static partial void LogTurnedOff(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The host asked the process to stop.")]
    private static partial void LogStopAsked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The host ended, so the process stops.")]
    private static partial void LogHostEnded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Remote Folders settings can't be read, so Remote Folders is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings can't be read, so the previous settings stay in use.")]
    private static partial void LogCantReadSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Servers are tried again at once after a {Reason}.")]
    private static partial void LogTryingAgain(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waking from sleep can't be watched, so servers are tried again only on their own schedule.")]
    private static partial void LogResumeWatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The process stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);
}
