using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Switches between the windows of the app in front with a shortcut. See docs/features/windows/app-windows.md.
/// </summary>
public sealed partial class AppWindowsFeature(ILogger<AppWindowsFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.AppWindows";

    private readonly WindowSwitcher _switcher = new(new WindowsAppWindowsEnvironment());
    private AppWindowsSettings _settings = new();
    private MessageWindow? _window;
    private ForegroundMonitor? _monitor;
    private ShortcutKeeper? _shortcut;
    private bool _failed;

    public static FeatureInfo Definition { get; } = new("appWindows", WindowsModule.Id, "AppWindowsName", "AppWindowsDescription", "");

    /// <summary>
    /// Whether this PC can run App Windows, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            _settings = AppWindowsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            _settings = new AppWindowsSettings();
        }

        if (!_settings.Enabled)
        {
            Stop();
            return;
        }

        if (_window is null)
        {
            Start();
        }

        Configure();
    }

    public void Dispose() => Stop();

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        ForegroundMonitor monitor;
        try
        {
            monitor = new ForegroundMonitor();
        }
        catch
        {
            window.Dispose();
            throw;
        }

        _window = window;
        _monitor = monitor;

        // The timer fires on the thread pool; the keeper runs on the window's thread.
        _shortcut = new ShortcutKeeper(new WindowsShortcutEnvironment(window), time, action => window.BeginInvoke(() => Guard(action)), logger);
        _shortcut.Pressed += (_, direction) => Guard(() => Switch(direction));
        _monitor.Changed += (_, _) => Guard(() => _shortcut?.Check());
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _shortcut?.Dispose();
        _monitor?.Dispose();
        _window.Dispose();
        (_shortcut, _monitor, _window) = (null, null, null);
        LogStopped(logger);
    }

    private void Configure()
    {
        HotKeyModifiers? modifiers = _settings.Shortcut switch
        {
            AppWindowsShortcut.AltAboveTab => HotKeyModifiers.Alt,
            AppWindowsShortcut.WindowsAboveTab => HotKeyModifiers.Windows,
            _ => null,
        };

        if (modifiers is null)
        {
            LogUnknownShortcut(logger);
        }

        _shortcut?.Configure(modifiers);
        LogConfigured(logger, _settings.Shortcut, _settings.IncludeMinimized, _shortcut?.ChecksLayout ?? false);
    }

    private void Switch(SwitchDirection direction)
    {
        var result = _switcher.Switch(direction, _settings.IncludeMinimized);
        switch (result.Outcome)
        {
            case SwitchOutcome.Switched:
                LogSwitched(logger, direction, result.WindowCount);
                break;
            case SwitchOutcome.Refused:
                LogRefused(logger, direction);
                break;
            default:
                LogNotSwitched(logger, direction, result.Outcome);
                break;
        }
    }

    // Runs on the window's thread, where an exception would end the host. Like a failing Apply,
    // a failure stops the feature until the host restarts.
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            _failed = true;
            Stop();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows applied its settings: shortcut {Shortcut}, minimized windows {IncludeMinimized}, layout checks {ChecksLayout}.")]
    private static partial void LogConfigured(ILogger logger, AppWindowsShortcut shortcut, bool includeMinimized, bool checksLayout);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows went {Direction}; the app has {WindowCount} windows.")]
    private static partial void LogSwitched(ILogger logger, SwitchDirection direction, int windowCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows didn't go {Direction}: {Reason}.")]
    private static partial void LogNotSwitched(ILogger logger, SwitchDirection direction, SwitchOutcome reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't bring the app's window to the front when App Windows went {Direction}.")]
    private static partial void LogRefused(ILogger logger, SwitchDirection direction);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The App Windows shortcut in the settings is unknown, so it has no shortcut.")]
    private static partial void LogUnknownShortcut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The App Windows settings can't be read, so App Windows is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "App Windows failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
