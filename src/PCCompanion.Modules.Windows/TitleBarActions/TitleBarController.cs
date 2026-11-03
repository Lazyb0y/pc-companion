using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Runs the action chosen for a middle-click or the wheel on a title bar, as
/// docs/features/windows/title-bar-actions.md says. While the pointer rests on a title bar, it claims that point for the pointer hook, which
/// keeps the clicks that have an action from the app. A click that reaches the app anyway, before
/// the window answered or after Windows removed the hook, still gets its action once the window
/// answers. Use it on one thread, the one <paramref name="post"/> runs on.
/// </summary>
public sealed partial class TitleBarController : IDisposable
{
    /// <summary>How long after raw input reports a click its check waits for the hook to have seen it.</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(60);

    // How far from the answered point, in pixels, a click still counts as there.
    private const int SpotMargin = 2;

    // How far the wheel turns for a notch.
    private const int Notch = 120;

    private readonly ITitleBarWindows _windows;
    private readonly ITitleBarActionsSystem _system;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ExcludedWindows _excluded;
    private readonly TitleBarFinder _finder;
    private readonly WindowOpacity _opacity;
    private readonly ITimer _timer;
    private readonly List<RawClick> _clicks = [];
    private Dictionary<PointerTrigger, TitleBarClickAction> _actions = [];
    private HashSet<PointerTrigger> _wheel = [];
    private nint _claimed;
    private bool _timerSet;
    private nint _wheelWindow;
    private int _wheelTurned;
    private nint _soundedFor;

    /// <param name="opacity">Changes the windows' opacity on the wheel, on this controller's thread.</param>
    /// <param name="post">Runs an action on this controller's thread, from the timer's.</param>
    public TitleBarController(ITitleBarWindows windows, ITitleBarActionsSystem system, WindowOpacity opacity, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(post);

        _windows = windows;
        _system = system;
        _opacity = opacity;
        _time = time;
        _logger = logger;
        _excluded = new ExcludedWindows(windows);
        _finder = new TitleBarFinder(windows, _excluded, time);
        _finder.SpotChanged += (_, _) => UpdateClaim();
        _timer = time.CreateTimer(_ => post(CheckClicks), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The window whose title bar is claimed now, or 0.</summary>
    public nint ClaimedWindow => _claimed;

    /// <summary>
    /// Takes the actions and the apps that keep their title-bar clicks from
    /// <paramref name="settings"/>; clicks without an action reach the apps.
    /// </summary>
    public void Configure(TitleBarActionsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The title bar the pointer rests on may be a newly listed app's, so it's found again.
        if (_excluded.SetApps(settings.ExcludedApps))
        {
            _finder.Stop();
        }

        var actions = new Dictionary<PointerTrigger, TitleBarClickAction>();
        Add(actions, PointerKeys.None, settings.MiddleClick);
        Add(actions, PointerKeys.Shift, settings.ShiftMiddleClick);
        Add(actions, PointerKeys.Control, settings.ControlMiddleClick);
        _actions = actions;
        _wheel = [];
        if (settings.Wheel == TitleBarWheelAction.Opacity)
        {
            _wheel.Add(new PointerTrigger(PointerButton.Wheel, PointerKeys.None));
        }

        if (settings.ControlWheel == TitleBarWheelAction.Opacity)
        {
            _wheel.Add(new PointerTrigger(PointerButton.Wheel, PointerKeys.Control));
        }

        UpdateClaim();
    }

    /// <summary>Call it for each pointer event that raw input reports.</summary>
    public void OnPointerInput(PointerInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var button = (input.Changes & PointerButtonChanges.MiddleDown) != 0 ? PointerButton.Middle
            : (input.Changes & PointerButtonChanges.Wheel) != 0 ? PointerButton.Wheel
            : (PointerButton?)null;
        if (button is { } pressed && _windows.PointerPosition is { } point)
        {
            var trigger = new PointerTrigger(pressed, _system.HeldKeys);
            if (_actions.ContainsKey(trigger) || _wheel.Contains(trigger))
            {
                _clicks.Add(new RawClick(input.Time, trigger, point, input.WheelDelta, WasClaimed: _claimed != 0, _time.GetTimestamp()));
                ScheduleCheck();
            }
        }

        _finder.PointerMoved();
    }

    /// <summary>Call it for each click the pointer hook kept for this controller's claim.</summary>
    public void OnKept(KeptPointerInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var isWheel = input.Trigger.Button == PointerButton.Wheel;
        var action = TitleBarClickAction.None;
        if (isWheel ? !_wheel.Contains(input.Trigger) : !_actions.TryGetValue(input.Trigger, out action))
        {
            return;
        }

        // The click was kept for the window the claim is for, which must still be the one there.
        var under = _windows.WindowAt(input.Point);
        var window = under == 0 ? 0 : _windows.Root(under);
        if (window == 0 || window != _claimed)
        {
            LogWindowChanged(_logger, input.Trigger.Keys);
            return;
        }

        if (isWheel)
        {
            Turn(window, input.WheelDelta, ClickPath.Kept);
        }
        else
        {
            Run(action, window, input.Trigger, ClickPath.Kept);
        }
    }

    /// <summary>Puts back every window whose opacity the wheel changed. Returns how many.</summary>
    public int PutBack() => _opacity.PutBackAll();

    /// <summary>
    /// Shows every window whose opacity the wheel changed as before, without waiting, as when PC
    /// Companion crashes. Any thread can call it. Returns how many.
    /// </summary>
    public int PutBackNow() => _opacity.PutBackNow();

    /// <summary>
    /// Forgets the title bar and the clicks waiting for their check, removes the claim, and puts
    /// back every window whose opacity the wheel changed. Returns how many windows were put back.
    /// </summary>
    public int Stop()
    {
        _finder.Stop();
        _clicks.Clear();
        SetTimer(false);
        Release();
        (_wheelWindow, _wheelTurned, _soundedFor) = (0, 0, 0);
        return _opacity.PutBackAll();
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    private static void Add(Dictionary<PointerTrigger, TitleBarClickAction> actions, PointerKeys keys, TitleBarClickAction action)
    {
        if (action != TitleBarClickAction.None && Enum.IsDefined(action))
        {
            actions[new PointerTrigger(PointerButton.Middle, keys)] = action;
        }
    }

    private void UpdateClaim()
    {
        if (_finder.Spot is not { } spot || (_actions.Count == 0 && _wheel.Count == 0))
        {
            Release();
            return;
        }

        var area = new Rectangle(spot.Point.X - SpotMargin, spot.Point.Y - SpotMargin, (2 * SpotMargin) + 1, (2 * SpotMargin) + 1);
        _system.Claim(new PointerClaim(area, _actions.Keys.Concat(_wheel).ToHashSet()));
        _claimed = spot.Window;
    }

    private void Release()
    {
        if (_claimed != 0)
        {
            _system.Claim(null);
            _claimed = 0;
        }
    }

    // A moment after raw input reported a click, the hook has seen it if it was set: it kept it or
    // let it through. One it never saw while it was claimed means Windows removed the hook.
    private void CheckClicks()
    {
        var now = _time.GetTimestamp();
        foreach (var click in _clicks.Where(click => _time.GetElapsedTime(click.Reported, now) >= CheckDelay).ToList())
        {
            _clicks.Remove(click);
            var outcome = _system.Outcome(click.Time);
            if (outcome == PointerInputOutcome.Kept)
            {
                continue;
            }

            if (outcome == PointerInputOutcome.NotSeen && click.WasClaimed)
            {
                LogHookRemoved(_logger);
                _system.SetHookAgain();
            }

            var found = TitleBarCheck.Elsewhere;
            if (click.Trigger.Button == PointerButton.Wheel)
            {
                found = _finder.Check(click.Point, window => Turn(window, click.WheelDelta, ClickPath.LetThrough));
            }
            else if (_actions.TryGetValue(click.Trigger, out var action))
            {
                found = _finder.Check(click.Point, window => Run(action, window, click.Trigger, ClickPath.LetThrough));
            }

            if (found is TitleBarCheck.WindowLeftOut or TitleBarCheck.AppListed)
            {
                LogLeftOut(_logger, click.Trigger.Button, click.Trigger.Keys, found);
            }
        }

        SetTimer(false);
        ScheduleCheck();
    }

    private void ScheduleCheck()
    {
        if (_clicks.Count > 0)
        {
            SetTimer(true);
        }
    }

    private void SetTimer(bool set)
    {
        if (set == _timerSet)
        {
            return;
        }

        _timerSet = set;
        _timer.Change(set ? CheckDelay : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void Run(TitleBarClickAction action, nint window, PointerTrigger trigger, ClickPath path)
    {
        var click = trigger.Keys;
        switch (action)
        {
            case TitleBarClickAction.Minimize:
                Minimize(window, click, path);
                break;
            case TitleBarClickAction.SendToBack:
                SendToBack(window, click, path);
                break;
            case TitleBarClickAction.MinimizeToTray or TitleBarClickAction.HideApp:
                PassOn(action, window, click, path);
                break;
        }
    }

    private void Minimize(nint window, PointerKeys click, ClickPath path)
    {
        if (!_system.HasMinimizeButton(window))
        {
            Refuse(click, TitleBarClickAction.Minimize, TitleBarRefusal.NoMinimizeButton);
        }
        else if (!_system.AskToMinimize(window))
        {
            Refuse(click, TitleBarClickAction.Minimize, TitleBarRefusal.Refused);
        }
        else
        {
            LogRan(_logger, click, TitleBarClickAction.Minimize, path);
        }
    }

    // Minimize to Tray and Hide Apps carry it out on their threads, with their own rules and warning sound.
    private void PassOn(TitleBarClickAction action, nint window, PointerKeys click, ClickPath path)
    {
        if (!_system.HasMinimizeButton(window))
        {
            Refuse(click, action, TitleBarRefusal.NoMinimizeButton);
        }
        else if (!(action == TitleBarClickAction.MinimizeToTray ? _system.SendToTray(window) : _system.HideApp(window)))
        {
            Refuse(click, action, TitleBarRefusal.FeatureOff);
        }
        else
        {
            LogRan(_logger, click, action, path);
        }
    }

    // When the window had the front, the next window gets it, as with Alt + Esc.
    private void SendToBack(nint window, PointerKeys click, ClickPath path)
    {
        if (_system.IsTopmost(window))
        {
            Refuse(click, TitleBarClickAction.SendToBack, TitleBarRefusal.AlwaysOnTop);
            return;
        }

        var front = _system.ForegroundWindow;
        var hadFront = front != 0 && _windows.Root(front) == window;
        var next = hadFront ? _system.NextWindowBehind(window) : 0;
        if (!_system.SendToBack(window))
        {
            Refuse(click, TitleBarClickAction.SendToBack, TitleBarRefusal.Refused);
            return;
        }

        LogRan(_logger, click, TitleBarClickAction.SendToBack, path);
        if (next != 0 && !_system.BringToFront(next))
        {
            LogFrontNotGiven(_logger);
        }
    }

    // The wheel's smaller steps add up to notches, for one window at a time. A window whose opacity
    // can't change plays the warning sound once.
    private void Turn(nint window, short delta, ClickPath path)
    {
        if (window != _wheelWindow)
        {
            (_wheelWindow, _wheelTurned) = (window, 0);
        }

        _wheelTurned += delta;
        var notches = _wheelTurned / Notch;
        _wheelTurned -= notches * Notch;
        if (notches == 0)
        {
            return;
        }

        var outcome = _opacity.Step(window, notches);
        switch (outcome)
        {
            case OpacityStep.Changed:
                var level = _opacity.Level(window) ?? 100;
                LogOpacity(_logger, level, path);
                break;
            case OpacityStep.DrawsItself or OpacityStep.Refused:
                if (_soundedFor != window)
                {
                    _soundedFor = window;
                    _system.PlayWarningSound();
                    LogOpacityRefused(_logger, outcome);
                }

                break;
        }
    }

    private void Refuse(PointerKeys click, TitleBarClickAction action, TitleBarRefusal reason)
    {
        _system.PlayWarningSound();
        LogRefused(_logger, click, action, reason);
    }

    // A middle-click or wheel notch that raw input reported, waiting to be checked.
    private sealed record RawClick(uint Time, PointerTrigger Trigger, Point Point, short WheelDelta, bool WasClaimed, long Reported);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: a middle-click on a title bar with keys {Keys}, {Action} ({Path}).")]
    private static partial void LogRan(ILogger logger, PointerKeys keys, TitleBarClickAction action, ClickPath path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: a middle-click on a title bar with keys {Keys}, but {Action} didn't run: {Reason}.")]
    private static partial void LogRefused(ILogger logger, PointerKeys keys, TitleBarClickAction action, TitleBarRefusal reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: a middle-click with keys {Keys} was kept, but the window under the pointer changed, so nothing ran.")]
    private static partial void LogWindowChanged(ILogger logger, PointerKeys keys);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: a click or notch ({Button}, keys {Keys}) near the top of a window got no action: {Reason}.")]
    private static partial void LogLeftOut(ILogger logger, PointerButton button, PointerKeys keys, TitleBarCheck reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: the wheel on a title bar changed the window's opacity to {Level}% ({Path}).")]
    private static partial void LogOpacity(ILogger logger, int level, ClickPath path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions: the wheel on a title bar didn't change the window's opacity: {Reason}.")]
    private static partial void LogOpacityRefused(ILogger logger, OpacityStep reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions sent a window to the back, but Windows didn't give the front to the next window.")]
    private static partial void LogFrontNotGiven(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Title Bar Actions: Windows removed the pointer hook, so it was set again.")]
    private static partial void LogHookRemoved(ILogger logger);
}

/// <summary>How a click on a title bar got its action.</summary>
public enum ClickPath
{
    /// <summary>The pointer hook kept it from the app.</summary>
    Kept,

    /// <summary>It reached the app, and the action ran once the window answered.</summary>
    LetThrough,
}

/// <summary>Why an action didn't run.</summary>
public enum TitleBarRefusal
{
    /// <summary>The window has no Minimize button.</summary>
    NoMinimizeButton,

    /// <summary>The window is always on top, which sending it to the back would take away.</summary>
    AlwaysOnTop,

    /// <summary>The feature that carries out the action, Minimize to Tray or Hide Apps, is off.</summary>
    FeatureOff,

    /// <summary>Windows refused.</summary>
    Refused,
}
