using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

internal static partial class Program
{
    [STAThread]
    private static void Main()
    {
        // One host per user session. A second launch asks the running host to open the settings.
        using var instanceLock = new Mutex(initiallyOwned: true, @"Local\PCCompanion.Host", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageWindow.PostToWindow(TrayController.WindowClassName, TrayController.OpenSettingsMessage);
            return;
        }

        var paths = AppDataPaths.ForCurrentUser();
        using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddFileLog(paths.LogsFolder, "host"))
            .AddSingleton(paths)
            .AddSingleton<SettingsStore>()
            .AddSingleton<SettingsAppLauncher>()
            .AddSingleton<TrayController>()
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(logger, e.ExceptionObject as Exception);
        LogStarted(logger, Environment.OSVersion.Version);

        var settings = services.GetRequiredService<SettingsStore>().Load();
        LanguageSetting.Apply(settings.General.Language, logger);

        services.GetRequiredService<TrayController>().Show();
        MessageLoop.Run();

        LogStopped(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started on Windows {WindowsVersion}.")]
    private static partial void LogStarted(ILogger logger, Version windowsVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The host stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);
}
