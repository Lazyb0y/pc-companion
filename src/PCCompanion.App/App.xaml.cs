using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Logging;

namespace PCCompanion.App;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private readonly ILogger<App> _logger;
    private MainWindow? _window;

    public App()
    {
        _services = CreateServices();
        _logger = _services.GetRequiredService<ILogger<App>>();

        UnhandledException += (_, e) => LogCrash(_logger, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(_logger, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogUnobservedTaskException(_logger, e.Exception);

        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services => _services;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LogStarted(
            _logger,
            typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Environment.OSVersion.Version);

        _window = new MainWindow();

        // The app exits when its window closes; disposing the services flushes the log.
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
    }

    private static ServiceProvider CreateServices()
    {
        var paths = AppDataPaths.ForCurrentUser();

        return new ServiceCollection()
            .AddLogging(builder => builder.AddFileLog(paths.LogsFolder, "app"))
            .AddSingleton(paths)
            .AddSingleton<SettingsStore>()
            .BuildServiceProvider();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started version {Version} on Windows {WindowsVersion}.")]
    private static partial void LogStarted(ILogger logger, string? version, Version windowsVersion);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The app stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A background task failed and nothing handled the exception.")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
