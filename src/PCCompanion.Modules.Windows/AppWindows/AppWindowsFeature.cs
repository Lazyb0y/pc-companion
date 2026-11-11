using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Switches between the windows of the app in front with a shortcut. See docs/features/windows/app-windows.md.
/// </summary>
public sealed partial class AppWindowsFeature(ILogger<AppWindowsFeature> logger, TimeProvider time, ShortcutRefusals refusals) : IFeature, IFeatureActions
{
    /// <summary>The shortcut that goes to the next window, in <see cref="ShortcutRefusals"/>.</summary>
    public const string NextShortcut = "next";

    /// <summary>The shortcut with Shift, which goes back, in <see cref="ShortcutRefusals"/>.</summary>
    public const string PreviousShortcut = "previous";

    private const string WindowClassName = "PCCompanion.AppWindows";

    private readonly WindowSwitcher _switcher = new(new WindowsAppWindowsEnvironment());
    private AppWindowsSettings _settings = new();
    private MessageWindow? _window;
    private WinEventHook? _foreground;
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

    /// <summary>The actions App Windows offers to shortcuts and mouse buttons, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
    [
        new(new FeatureAction(Definition.Id, NextShortcut), Strings.AppWindowsActionNext),
        new(new FeatureAction(Definition.Id, PreviousShortcut), Strings.AppWindowsActionPrevious),
    ];

    public bool CanRun(FeatureAction action) => _window is not null && DirectionOf(action) is not null;

    // App Windows runs on the host's UI thread, so the switch runs right away.
    public void Run(FeatureAction action)
    {
        if (_window is not null && DirectionOf(action) is { } direction)
        {
            Switch(direction);
        }
    }

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

    private static SwitchDirection? DirectionOf(FeatureAction action) =>
        action.FeatureId != Definition.Id || action.Target is not null ? null : action.ActionId switch
        {
            NextShortcut => SwitchDirection.Forward,
            PreviousShortcut => SwitchDirection.Back,
            _ => null,
        };

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        WinEventHook foreground;
        try
        {
            // Another program's window coming to the front.
            foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
        }
        catch
        {
            window.Dispose();
            throw;
        }

        _window = window;
        _foreground = foreground;

        // The timer fires on the thread pool; the keeper runs on the window's thread.
        _shortcut = new ShortcutKeeper(new WindowsShortcutEnvironment(window), time, action => window.BeginInvoke(() => Guard(action)));
        ShortcutKeeperLog.Attach(_shortcut, logger, "App Windows");
        _shortcut.Pressed += (_, shortcut) => Guard(() =>
            Switch(shortcut.Modifiers.HasFlag(HotKeyModifiers.Shift) ? SwitchDirection.Back : SwitchDirection.Forward));
        _shortcut.RefusedShortcutsChanged += (_, _) => Guard(ReportRefused);
        _foreground.Raised += (_, _) => Guard(() => _shortcut?.Check());
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _shortcut?.Dispose();
        _foreground?.Dispose();
        _window.Dispose();
        (_shortcut, _foreground, _window) = (null, null, null);
        refusals.Set(Definition.Id, []);
        LogStopped(logger);
    }

    // The settings app shows a note when Windows refused the shortcut or its Shift version.
    private void ReportRefused() =>
        refusals.Set(Definition.Id, _shortcut?.RefusedShortcuts.Select(shortcut => shortcut.Modifiers.HasFlag(HotKeyModifiers.Shift) ? PreviousShortcut : NextShortcut) ?? []);

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

        // The shortcut with Shift added goes back.
        _shortcut?.Configure(modifiers is { } held
            ? [new(held, ShortcutKeeper.KeyAboveTab), new(held | HotKeyModifiers.Shift, ShortcutKeeper.KeyAboveTab)]
            : []);
        LogConfigured(logger, _settings.Shortcut, _settings.IncludeMinimized, _shortcut?.ChecksLayout ?? false);
    }

    // A press runs on the host's UI thread, so its time is logged.
    private void Switch(SwitchDirection direction)
    {
        var started = time.GetTimestamp();
        var result = _switcher.Switch(direction, _settings.IncludeMinimized);
        var milliseconds = time.GetElapsedTime(started).TotalMilliseconds;
        switch (result.Outcome)
        {
            case SwitchOutcome.Switched:
                LogSwitched(logger, direction, result.WindowCount, milliseconds);
                break;
            case SwitchOutcome.Refused:
                LogRefused(logger, direction);
                break;
            default:
                LogNotSwitched(logger, direction, result.Outcome, milliseconds);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows went {Direction}; the app has {WindowCount} windows; the press took {Milliseconds:F1} ms.")]
    private static partial void LogSwitched(ILogger logger, SwitchDirection direction, int windowCount, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows didn't go {Direction}: {Reason}; the press took {Milliseconds:F1} ms.")]
    private static partial void LogNotSwitched(ILogger logger, SwitchDirection direction, SwitchOutcome reason, double milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't bring the app's window to the front when App Windows went {Direction}.")]
    private static partial void LogRefused(ILogger logger, SwitchDirection direction);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The App Windows shortcut in the settings is unknown, so it has no shortcut.")]
    private static partial void LogUnknownShortcut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The App Windows settings can't be read, so App Windows is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "App Windows failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
