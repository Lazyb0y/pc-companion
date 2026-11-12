using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// Runs PC Companion's actions from the user's own shortcuts and mouse buttons. See
/// docs/features/input/shortcuts-and-mouse-buttons.md.
/// </summary>
public sealed partial class ShortcutsAndMouseButtonsFeature : IFeature
{
    /// <summary>Lock's action ID.</summary>
    public const string LockAction = "lock";

    /// <summary>Turn off displays' action ID.</summary>
    public const string TurnOffDisplaysAction = "turnOffDisplays";

    /// <summary>Start the screen saver's action ID.</summary>
    public const string ScreenSaverAction = "screenSaver";

    // How long the host waits as it exits for the thread to remove the shortcuts.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly IHostActions _actions;
    private readonly ShortcutRefusals _refusals;
    private readonly TimeProvider _time;
    private readonly ILogger<ShortcutsAndMouseButtonsFeature> _logger;
    private readonly Func<Action<PointerButton, uint>, IDisposable> _watchRawPresses;
    private IDisposable? _rawPresses;
    private ShortcutsAndMouseButtonsSettings _settings = new();
    private ShortcutsAndMouseButtonsThread? _thread;
    private ShortcutsAndMouseButtonsThread? _stopped;
    private bool _failed;

    /// <param name="actions">The other features' actions, which the host gives this feature only.</param>
    /// <param name="refusals">Where the feature says which of its shortcuts Windows refused.</param>
    public ShortcutsAndMouseButtonsFeature(IHostActions actions, ShortcutRefusals refusals, TimeProvider time, ILogger<ShortcutsAndMouseButtonsFeature> logger)
        : this(actions, refusals, time, logger, WatchRawPresses)
    {
    }

    /// <param name="watchRawPresses">
    /// Reports the Back, Forward and middle buttons going down, with their times, on the host's UI
    /// thread until disposed, as raw input does, which every feature shares on that thread.
    /// </param>
    public ShortcutsAndMouseButtonsFeature(IHostActions actions, ShortcutRefusals refusals, TimeProvider time, ILogger<ShortcutsAndMouseButtonsFeature> logger, Func<Action<PointerButton, uint>, IDisposable> watchRawPresses)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(watchRawPresses);

        _actions = actions;
        _refusals = refusals;
        _time = time;
        _logger = logger;
        _watchRawPresses = watchRawPresses;

        // After the host applied the settings to every feature, other features' actions may have
        // come or gone. Raised on the host's UI thread, outside the feature runner's error boundary.
        _actions.Changed += (_, _) =>
        {
            try
            {
                Plan();
            }
            catch (Exception exception)
            {
                LogFailed(_logger, exception);
                _failed = true;
                Stop();
            }
        };
    }

    public static FeatureInfo Definition { get; } = new("shortcutsAndMouseButtons", InputModule.Id, "ShortcutsAndMouseButtonsName", "ShortcutsAndMouseButtonsDescription", "");

    /// <summary>Whether this PC can run Shortcuts & Mouse Buttons, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    /// <summary>The plan the feature's thread has now, for tests; empty while it's off.</summary>
    public TriggerPlan? CurrentPlan { get; private set; }

    /// <summary>The actions of its own the feature offers: Lock, Turn off displays and Start the screen saver, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
    [
        new(new FeatureAction(Definition.Id, LockAction), Strings.ShortcutsAndMouseButtonsActionLock),
        new(new FeatureAction(Definition.Id, TurnOffDisplaysAction), Strings.ShortcutsAndMouseButtonsActionTurnOffDisplays),
        new(new FeatureAction(Definition.Id, ScreenSaverAction), Strings.ShortcutsAndMouseButtonsActionScreenSaver),
    ];

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

        ShortcutsAndMouseButtonsSettings read;
        try
        {
            read = ShortcutsAndMouseButtonsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            read = new ShortcutsAndMouseButtonsSettings();
        }

        // Once for each change, not at each plan, which the other features' changes also bring.
        if (read.Enabled && !read.Equals(_settings))
        {
            foreach (var unusable in TriggerCheck.CheckAll(read.Triggers).Where(checkedTrigger => !checkedTrigger.IsUsable))
            {
                LogUnusableTrigger(_logger, unusable.Index + 1, unusable.Problem);
            }
        }

        _settings = read;
        Plan();
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

    // The triggers that can be used and whose actions can run now, which the thread registers.
    private void Plan()
    {
        if (_failed || !_settings.Enabled)
        {
            Stop();
            return;
        }

        var planned = new List<PlannedTrigger>();
        var unavailable = 0;
        foreach (var checkedTrigger in TriggerCheck.CheckAll(_settings.Triggers))
        {
            if (!checkedTrigger.IsUsable)
            {
                continue;
            }

            var action = checkedTrigger.Trigger.Action.ToFeatureAction();
            if (!CanRun(action))
            {
                unavailable++;
                continue;
            }

            planned.Add(new PlannedTrigger(checkedTrigger.Index, action, TriggerCheck.InputOf(checkedTrigger.Trigger)!.Value, checkedTrigger.Trigger.ExcludedApps));
        }

        var plan = new TriggerPlan(planned, _settings.PauseMouseButtonsInFullScreen);
        if (plan == CurrentPlan && _thread is not null)
        {
            return;
        }

        CurrentPlan = plan;
        LogPlanned(_logger, planned.Count, unavailable);
        if (_thread is null)
        {
            _thread = new ShortcutsAndMouseButtonsThread(plan, _actions, _refusals, _time, _logger, _stopped);
            _stopped = null;
        }
        else
        {
            _thread.Update(plan);
        }

        // Raw input tells the thread of clicks the shared pointer hook should have seen.
        if (planned.Any(trigger => trigger.Input.Button is not null))
        {
            _rawPresses ??= WatchRawPressesOrNot();
        }
        else
        {
            _rawPresses?.Dispose();
            _rawPresses = null;
        }
    }

    private IDisposable? WatchRawPressesOrNot()
    {
        try
        {
            return _watchRawPresses((button, time) => _thread?.RawPress(button, time));
        }
        catch (Win32Exception exception)
        {
            LogNoRawInput(_logger, exception);
            return null;
        }
    }

    private static IDisposable WatchRawPresses(Action<PointerButton, uint> pressed)
    {
        var monitor = new RawPointerMonitor();
        monitor.InputReceived += (_, input) =>
        {
            if (input.Changes.HasFlag(PointerButtonChanges.X1Down))
            {
                pressed(PointerButton.Back, input.Time);
            }

            if (input.Changes.HasFlag(PointerButtonChanges.X2Down))
            {
                pressed(PointerButton.Forward, input.Time);
            }

            if (input.Changes.HasFlag(PointerButtonChanges.MiddleDown))
            {
                pressed(PointerButton.Middle, input.Time);
            }
        };
        return monitor;
    }

    private bool CanRun(FeatureAction action) =>
        action.FeatureId == Definition.Id
            ? action.Target is null && OfferedActions().Any(offered => offered.Action == action)
            : _actions.CanRun(action);

    // Doesn't wait: a new thread waits for this one before it registers the shortcuts.
    private void Stop()
    {
        CurrentPlan = null;
        _rawPresses?.Dispose();
        _rawPresses = null;
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Shortcuts & Mouse Buttons settings can't be read, so Shortcuts & Mouse Buttons is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trigger {Number} can't be used: {Problem}.")]
    private static partial void LogUnusableTrigger(ILogger logger, int number, TriggerProblem problem);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons has {Planned} triggers whose actions can run, and {Unavailable} whose features are off or don't have what they name.")]
    private static partial void LogPlanned(ILogger logger, int planned, int unavailable);

    [LoggerMessage(Level = LogLevel.Error, Message = "Shortcuts & Mouse Buttons failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shortcuts & Mouse Buttons didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't report mouse input, so Shortcuts & Mouse Buttons can't tell when Windows removes the pointer hook.")]
    private static partial void LogNoRawInput(ILogger logger, Exception exception);
}
