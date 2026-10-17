using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var paths = AppDataPaths.ForCurrentUser();
        var startedAtSignIn = args.Contains(HostProcess.StartupArgument, StringComparer.OrdinalIgnoreCase);
        if (startedAtSignIn && !IsSetToStartWithWindows(paths))
        {
            return;
        }

        // One host per user session. Started again by the user, it asks the running host to open the settings.
        using var instanceLock = new Mutex(initiallyOwned: true, HostProcess.InstanceLockName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!startedAtSignIn)
            {
                MessageWindow.PostToWindow(TrayController.WindowClassName, TrayController.OpenSettingsMessage);
            }

            return;
        }

        using var detailedLogging = new DetailedLogging();
        using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddFileLog(paths.LogsFolder, "host", detailedLogging: detailedLogging))
            .AddSingleton(paths)
            .AddSingleton<SettingsStore>()
            .AddSingleton<SettingsAppLauncher>()
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(logger, e.ExceptionObject as Exception);
        LogStarted(logger, Environment.OSVersion.Version);

        var store = services.GetRequiredService<SettingsStore>();
        ApplySettings(store.Load(), detailedLogging, logger);

        // Declared after the window, so the tray icon is removed before the window is destroyed.
        using var window = new MessageWindow(TrayController.WindowClassName);
        using var tray = ActivatorUtilities.CreateInstance<TrayController>(services);
        tray.Show(window);

        using var watcher = new SettingsWatcher(paths);
        watcher.Changed += (_, _) => window.BeginInvoke(() => ReloadSettings(store, detailedLogging, logger));

        MessageLoop.Run();
        LogStopped(logger);
    }

    // Windows starts the host at every sign-in, for every user of an all-users install. This runs
    // before any logging, so users who haven't turned the setting on get no files.
    private static bool IsSetToStartWithWindows(AppDataPaths paths)
    {
        try
        {
            return new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Load().General.StartWithWindows;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ApplySettings(AppSettings settings, DetailedLogging detailedLogging, ILogger logger)
    {
        LanguageSetting.Apply(settings.General.Language, logger);
        detailedLogging.TurnOnUntil(settings.Diagnostics.DetailedLoggingUntil);
    }

    private static void ReloadSettings(SettingsStore store, DetailedLogging detailedLogging, ILogger logger)
    {
        try
        {
            ApplySettings(store.Load(), detailedLogging, logger);
            LogSettingsReloaded(logger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantReloadSettings(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started on Windows {WindowsVersion}.")]
    private static partial void LogStarted(ILogger logger, Version windowsVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings changed and were loaded again.")]
    private static partial void LogSettingsReloaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings changed but can't be read, so the previous settings stay in use.")]
    private static partial void LogCantReloadSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The host stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);
}
