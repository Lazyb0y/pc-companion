using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// Ends the app of a window the user chooses, such as one that stopped responding, after asking.
/// See docs/features/windows/end-app.md.
/// </summary>
public sealed partial class EndAppFeature : IFeature, IHostMenuItems, IFeatureActions
{
    // How long the host waits as it exits for the feature's thread to end.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private const int EndAnAppItem = 1;
    private const string HostWindowClassName = "PCCompanion.EndApp.Host";

    private readonly TimeProvider _time;
    private readonly ILogger<EndAppFeature> _logger;
    private readonly IHostNotifications _notifications;
    private readonly ShortcutRefusals _refusals;
    private EndAppThread? _thread;
    private EndAppThread? _stopped;

    // On the host's UI thread, where notifications are shown, while the feature runs.
    private MessageWindow? _hostWindow;
    private bool _failed;

    /// <param name="refusals">Where the feature says whether Windows refused its shortcut.</param>
    public EndAppFeature(TimeProvider time, ILogger<EndAppFeature> logger, IHostNotifications notifications, ShortcutRefusals refusals)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(refusals);

        _time = time;
        _logger = logger;
        _notifications = notifications;
        _refusals = refusals;
    }

    /// <summary>The shortcut's name in <see cref="ShortcutRefusals"/>.</summary>
    public const string ChooseAppShortcut = "chooseApp";

    public static FeatureInfo Definition { get; } = new("endApp", WindowsModule.Id, "EndAppName", "EndAppDescription", "");

    /// <summary>Whether this PC can run End App, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    /// <summary>The action End App offers to shortcuts and mouse buttons, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
        [new(new FeatureAction(Definition.Id, ChooseAppShortcut), Strings.EndAppActionChooseApp)];

    /// <summary>The shortcut End App registers with <paramref name="settings"/> while it's on, with the action it runs.</summary>
    public static IReadOnlyList<(FeatureAction Action, ShortcutKeys Keys)> Shortcuts(EndAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HotKeyModifiers? modifiers = settings.Shortcut switch
        {
            EndAppShortcut.ControlAltEscape => HotKeyModifiers.Control | HotKeyModifiers.Alt,
            EndAppShortcut.WindowsControlEscape => HotKeyModifiers.Windows | HotKeyModifiers.Control,
            _ => null,
        };

        return modifiers is { } keys
            ? [(new FeatureAction(Definition.Id, ChooseAppShortcut), ShortcutKeys.OfVirtualKey(keys, EndAppThread.KeyEscape))]
            : [];
    }

    public bool CanRun(FeatureAction action) => _thread is not null && action == new FeatureAction(Definition.Id, ChooseAppShortcut);

    public void Run(FeatureAction action)
    {
        if (CanRun(action))
        {
            _thread?.StartFromAction();
        }
    }

    public void Apply(JsonElement? settings)
    {
        // A failure on the feature's thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        EndAppSettings read;
        try
        {
            read = EndAppSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        if (_thread is null)
        {
            _hostWindow = new MessageWindow(HostWindowClassName);
            var hostWindow = _hostWindow;
            _thread = new EndAppThread(read, _time, _logger, (title, text) => hostWindow.BeginInvoke(() => Notify(title, text)), _stopped, _refusals);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems() => _thread is null ? [] : [new HostMenuItem(EndAnAppItem, Strings.EndAppMenuItem)];

    public void ChooseMenuItem(int id)
    {
        if (id == EndAnAppItem)
        {
            _thread?.StartFromMenu();
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
        _hostWindow?.Dispose();
        _hostWindow = null;
    }

    // On the host's UI thread; an exception here would end the host.
    private void Notify(string title, string text)
    {
        try
        {
            _notifications.Show(title, text, null);
        }
        catch (Exception exception)
        {
            LogNotificationFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The End App settings can't be read, so End App is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End App didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End App's notification couldn't be shown.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception);
}
