using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.Globalization;
using PCCompanion.App.Features;
using PCCompanion.App.Features.HotCorners;
using PCCompanion.App.ViewModels;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Localization;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private readonly ILogger<App> _logger;
    private MainWindow? _window;
    private SessionSignal? _exitSignal;

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

    public MainWindow? MainWindow => _window;

    /// <summary>The app's version, including the commit it was built from, such as <c>1.2.0+3f7bd98</c>.</summary>
    public static string InformationalVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = _services.GetRequiredService<AppSettings>();
        _services.GetRequiredService<DetailedLogging>().TurnOnUntil(settings.Diagnostics.DetailedLoggingUntil);
        LogStarted(_logger, InformationalVersion, Environment.OSVersion.Version);
        ApplyLanguage(settings.General.Language);

        var window = new MainWindow();
        _window = window;
        window.ApplyTheme(settings.General.Theme);

        // The app exits when its window closes; disposing the services flushes the log.
        window.Closed += (_, _) =>
        {
            _exitSignal?.Dispose();
            _services.Dispose();
        };
        window.Activate();

        // Exit in the host's tray menu closes the settings app too.
        _exitSignal = SessionSignal.Listen(SettingsAppProcess.ExitSignalName, () => window.DispatcherQueue.TryEnqueue(() =>
        {
            LogExitRequested(_logger);
            window.Close();
        }));
        if (_exitSignal is null)
        {
            LogCantListenForExit(_logger);
        }

        // Raised on another thread when the settings app is started again.
        AppInstance.GetCurrent().Activated += (_, _) => window.DispatcherQueue.TryEnqueue(window.BringToFront);

        _services.GetRequiredService<HostLauncher>().StartIfNotRunning();
    }

    // Without a language setting, .NET and WinUI follow the Windows display language.
    private void ApplyLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language, predefinedOnly: true);
        }
        catch (CultureNotFoundException exception)
        {
            LogUnknownLanguage(_logger, exception, language);
            return;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // WinUI's own text comes from resource files that follow PrimaryLanguageOverride, such as
        // tooltips, and from Windows language resource files per region, such as "On" and "Off".
        ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        try
        {
            ResourceLanguages.SetForProcess(Languages.ForWindowsResources(culture));
        }
        catch (Win32Exception exception)
        {
            LogCantSetResourceLanguages(_logger, exception, culture.Name);
        }
    }

    private static ServiceProvider CreateServices()
    {
        var paths = AppDataPaths.ForCurrentUser();
        var detailedLogging = new DetailedLogging();

        // Registered with a factory, so the service provider disposes it.
        return new ServiceCollection()
            .AddLogging(builder => builder.AddFileLog(paths.LogsFolder, "app", detailedLogging: detailedLogging))
            .AddSingleton(_ => detailedLogging)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(paths)
            .AddSingleton<SettingsStore>()
            .AddSingleton(provider => provider.GetRequiredService<SettingsStore>().Load())
            .AddSingleton<HostLauncher>()
            .AddSingleton<FeatureSettingsEditor>()
            .AddTransient<GeneralViewModel>()
            .AddTransient<HomeViewModel>()
            .AddTransient<HotCornersViewModel>()
            .BuildServiceProvider();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started version {Version} on Windows {WindowsVersion}.")]
    private static partial void LogStarted(ILogger logger, string version, Version windowsVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The language setting {Language} is not a known language, so the Windows display language is used.")]
    private static partial void LogUnknownLanguage(ILogger logger, Exception exception, string language);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows can't load its text for the language {Language}, so some built-in control text keeps the Windows display language.")]
    private static partial void LogCantSetResourceLanguages(ILogger logger, Win32Exception exception, string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exit was chosen in the tray menu, so the settings app closes.")]
    private static partial void LogExitRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't listen for Exit in the tray menu, so choosing it leaves the settings app open.")]
    private static partial void LogCantListenForExit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The app stopped because of an unhandled exception.")]
    private static partial void LogCrash(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A background task failed and nothing handled the exception.")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
