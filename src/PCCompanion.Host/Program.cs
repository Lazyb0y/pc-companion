using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Logging;
using PCCompanion.Modules.Input;
using PCCompanion.Modules.Windows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Setup runs the host with --exit before it removes or replaces PC Companion's files, and with
        // --remove-data as it uninstalls it. They write no log, so an uninstall that removes the
        // data folder leaves nothing behind.
        if (args.Contains(HostProcess.ExitArgument, StringComparer.OrdinalIgnoreCase))
        {
            ExitCommand.Run();
            return;
        }

        if (args.Contains(HostProcess.RemoveDataArgument, StringComparer.OrdinalIgnoreCase))
        {
            RemoveDataCommand.Run(args.Contains(HostProcess.KeepSettingsArgument, StringComparer.OrdinalIgnoreCase));
            return;
        }

        // A host running as administrator runs these in a copy of itself without admin rights.
        if (args.Contains(HostProcess.CheckForUpdatesArgument, StringComparer.OrdinalIgnoreCase))
        {
            UpdateCheckCommand.Run();
            return;
        }

        if (args.Contains(HostProcess.OpenReleasePageArgument, StringComparer.OrdinalIgnoreCase))
        {
            ReleasePageCommand.Run();
            return;
        }

        var paths = AppDataPaths.ForCurrentUser();
        var startedAtSignIn = args.Contains(HostProcess.StartupArgument, StringComparer.OrdinalIgnoreCase);
        // At sign-in with Start with Windows off, or with Always run as administrator on and no
        // admin rights, the host exits; the copy the scheduled tasks start runs instead.
        var start = HostProcess.DecideStart(LoadGeneralSettings(paths), startedAtSignIn, Elevation.IsElevated, IsHostRunning());
        if (start == HostStart.Exit
            || (start == HostStart.RunTask && ScheduledTasks.RunNow(ElevatedStartup.TaskFolder + ElevatedStartup.StartTaskName(Elevation.CurrentUserSid))))
        {
            return;
        }

        // One host per user session. Started again by the user, it asks the running host to open the settings.
        using var instanceLock = TryTakeInstanceLock();
        if (instanceLock is null)
        {
            if (!startedAtSignIn)
            {
                // As the program the user just started, this launch may bring a window to the front;
                // the running host needs that right to bring the settings window forward.
                MessageWindow.AllowOwnerToComeToFront(HostProcess.WindowClassName);
                MessageWindow.PostToWindow(HostProcess.WindowClassName, TrayController.OpenSettingsMessage);
            }

            return;
        }

        using var detailedLogging = new DetailedLogging();
        var logsFolder = GetLogsFolder(paths);
        using var services = new ServiceCollection()
            .AddLogging(builder =>
            {
                if (logsFolder is not null)
                {
                    builder.AddFileLog(logsFolder, "host", detailedLogging: detailedLogging);
                }
            })
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<SettingsStore>()
            .AddSingleton<SettingsAppLauncher>()
            .AddSingleton<FeatureRunner>()
            .AddSingleton<HostCommands>()
            .AddSingleton<IHostCommands>(provider => provider.GetRequiredService<HostCommands>())
            .AddSingleton<UpdateCheckStore>()
            .AddSingleton<GitHubReleaseSource>()
            .AddSingleton<UpdateChecker>()
            .AddSingleton<UpdateNotifier>()
            .AddInputModule()
            .AddWindowsModule()
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(logger, e.ExceptionObject as Exception);
        LogStarted(logger, Environment.OSVersion.Version, Elevation.IsElevated);
        if (start == HostStart.RunTask)
        {
            LogCantRunAsAdministrator(logger);
        }

        // Features run on this thread, whose message loop they need.
        var store = services.GetRequiredService<SettingsStore>();
        var features = services.GetRequiredService<FeatureRunner>();
        var updates = services.GetRequiredService<UpdateNotifier>();
        ApplySettings(store.Load(), detailedLogging, features, updates, logger);

        // Declared after the window, so the tray icon is removed before the window is destroyed.
        using var window = new MessageWindow(HostProcess.WindowClassName);
        using var tray = ActivatorUtilities.CreateInstance<TrayController>(services);
        tray.Show(window);
        updates.Attach(window, tray);

        // Features started before the window existed; their Settings and Exit work from now on.
        services.GetRequiredService<HostCommands>().Attach(action => window.BeginInvoke(action), tray.OpenSettings, tray.Exit);

        using var watcher = new SettingsWatcher(paths);
        watcher.Changed += (_, _) => window.BeginInvoke(() => ReloadSettings(store, detailedLogging, features, updates, logger));

        MessageLoop.Run();
        LogStopped(logger);
    }

    // Windows starts the host at every sign-in, for every user of an all-users install. This runs
    // before any logging, so users who haven't turned the settings on get no files.
    private static GeneralSettings LoadGeneralSettings(AppDataPaths paths)
    {
        try
        {
            return new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Load().General;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new GeneralSettings();
        }
    }

    private static bool IsHostRunning() => InstanceLock.IsHeld(HostProcess.InstanceLockName);

    // Returns null when another host holds the lock.
    private static Mutex? TryTakeInstanceLock()
    {
        try
        {
            var instanceLock = new Mutex(initiallyOwned: true, HostProcess.InstanceLockName, out var isFirstInstance);
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

    // A host elevated through UAC writes its logs where only administrators can change them:
    // programs without admin rights could otherwise redirect its writes with a link. Without such
    // a folder, it writes no log file.
    private static string? GetLogsFolder(AppDataPaths paths)
    {
        if (!Elevation.IsUacElevated)
        {
            return paths.LogsFolder;
        }

        var sid = Elevation.CurrentUserSid;
        var folder = AppDataPaths.ProtectedLogsFolder(sid);
        return ProtectedFiles.CreateUserFolder(folder, sid) ? folder : null;
    }

    private static void ApplySettings(AppSettings settings, DetailedLogging detailedLogging, FeatureRunner features, UpdateNotifier updates, ILogger logger)
    {
        LanguageSetting.Apply(settings.General.Language, logger);
        detailedLogging.TurnOnUntil(settings.Diagnostics.DetailedLoggingUntil);
        features.Apply(settings);
        updates.Apply(settings.Updates);
    }

    private static void ReloadSettings(SettingsStore store, DetailedLogging detailedLogging, FeatureRunner features, UpdateNotifier updates, ILogger logger)
    {
        try
        {
            ApplySettings(store.Load(), detailedLogging, features, updates, logger);
            LogSettingsReloaded(logger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantReloadSettings(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started on Windows {WindowsVersion}; running as administrator: {Elevated}.")]
    private static partial void LogStarted(ILogger logger, Version windowsVersion, bool elevated);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Always run as administrator is on, but its task didn't start, so the host runs without admin rights.")]
    private static partial void LogCantRunAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings changed and were loaded again.")]
    private static partial void LogSettingsReloaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings changed but can't be read, so the previous settings stay in use.")]
    private static partial void LogCantReloadSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The host stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);
}
