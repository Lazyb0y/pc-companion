using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Decides what each press of a listed app's shortcut does to its window: shows it, hides it,
/// brings it to the front or from another virtual desktop, or refuses. See
/// docs/features/windows/drop-down-apps.md. Use it on the feature's thread.
/// </summary>
public sealed partial class DropDownController : IDisposable
{
    private const string SharedAppFrameClass = "ApplicationFrameWindow";

    /// <summary>How long a window shown on another virtual desktop stays hidden before it's shown on this one.</summary>
    public static readonly TimeSpan DesktopWait = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a window being shown is waited for, to show on this desktop, before going on without it.</summary>
    public static readonly TimeSpan ShowTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>How long after a window is in place its size is checked, and again after a correction.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan ShowCheck = TimeSpan.FromMilliseconds(15);

    private readonly IDropDownSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DropWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double Width, double Height)> _draggedSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ITimer> _timers = [];
    private DropDownAppsSettings _settings = new();
    private IReadOnlyList<DropDownShortcut> _shortcuts = [];

    /// <param name="post">
    /// Runs an action later on the feature's thread, inside its error boundary, behind its other
    /// messages, as slides and timers need.
    /// </param>
    public DropDownController(IDropDownSystem system, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
    }

    /// <summary>What a press did or why it did nothing, for the listed app it was for.</summary>
    public event EventHandler<DropDownReport>? Reported;

    /// <summary>The shortcut each listed app has, in order: None for an unknown or repeated one.</summary>
    public IReadOnlyList<DropDownShortcut> Shortcuts => _shortcuts;

    /// <summary>Whether a drop-down window is shown or on its way in.</summary>
    public bool HasShownWindow => _windows.Values.Any(window => window.State is DropState.Shown or DropState.Showing);

    /// <summary>The drop-down windows, shown or hidden.</summary>
    public IReadOnlyCollection<nint> Windows => [.. _windows.Values.Select(window => window.Handle)];

    public void Apply(DropDownAppsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        var assigned = DropDownShortcuts.Assign(settings.Apps);
        _shortcuts = [.. assigned.Select(app => app.Shortcut)];
        for (var index = 0; index < assigned.Count; index++)
        {
            switch (assigned[index].Problem)
            {
                case ShortcutProblem.Unknown:
                    LogUnknownShortcut(_logger, index);
                    break;
                case ShortcutProblem.Repeated:
                    LogRepeatedShortcut(_logger, index);
                    break;
            }
        }
    }

    /// <summary>Handles a press of <paramref name="shortcut"/>, for the listed app that has it.</summary>
    public void Press(DropDownShortcut shortcut)
    {
        for (var index = 0; index < _shortcuts.Count; index++)
        {
            if (_shortcuts[index] == shortcut && shortcut != DropDownShortcut.None)
            {
                Press(index);
                return;
            }
        }
    }

    /// <summary>Handles a press of the shortcut of the listed app at <paramref name="index"/>.</summary>
    public void Press(int index)
    {
        if (index < 0 || index >= _settings.Apps.Count)
        {
            return;
        }

        var app = _settings.Apps[index];
        if (_windows.TryGetValue(KeyOf(app), out var known))
        {
            if (_system.Exists(known.Handle))
            {
                PressKnown(known, index);
                return;
            }

            Forget(known);
        }

        if (FindWindow(app) is { } found)
        {
            Adopt(found, index, app);
            return;
        }

        LogNoWindow(_logger, index);
    }

    /// <summary>A window of a watched process was destroyed.</summary>
    public void OnDestroyed(nint window)
    {
        if (Find(window) is { } closed)
        {
            Forget(closed);
            LogClosed(_logger);
        }
    }

    public void Dispose()
    {
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }

        _timers.Clear();
    }

    private static string KeyOf(DropDownApp app) => app.AppId ?? app.Program ?? string.Empty;

    private static bool MayHaveTaskbarButton(TopLevelWindow window) =>
        !window.IsDesktop && (window.IsAppWindow || (!window.IsToolWindow && !window.HasOwner));

    private void PressKnown(DropWindow window, int index)
    {
        // A press during a slide first ends it where it's going.
        window.Slide?.Finish();

        switch (window.State)
        {
            case DropState.Hidden:
                Show(window, index);
                Report(index, DropDownOutcome.Shown);
                break;
            case DropState.Shown when _system.IsMinimized(window.Handle):
                if (!HideNow(window))
                {
                    Refuse(index, DropDownOutcome.RunsAsAdministrator);
                    return;
                }

                Show(window, index);
                Report(index, DropDownOutcome.Shown);
                break;
            case DropState.Shown when _system.IsCloaked(window.Handle):
                BringHere(window, index);
                break;
            case DropState.Shown when IsInFront(window):
                Hide(window, index);
                break;
            case DropState.Shown:
                RememberFront(window);
                if (!_system.Activate(window.Handle))
                {
                    LogNotInFront(_logger);
                }

                Report(index, DropDownOutcome.BroughtToFront);
                break;
            default:
                LogStillShowing(_logger, index);
                break;
        }
    }

    // The app's front-most window that the taskbar shows, minimized or on another desktop too.
    private TopLevelWindow? FindWindow(DropDownApp app)
    {
        var reader = new AppOfWindowReader(_system);
        var listed = app.App;
        foreach (var window in _system.Windows())
        {
            if (MayHaveTaskbarButton(window) && Find(window.Handle) is null && listed.Matches(reader.Of(window)))
            {
                return window;
            }
        }

        return null;
    }

    private void Adopt(TopLevelWindow found, int index, DropDownApp app)
    {
        var handle = found.Handle;
        if (string.Equals(_system.ClassName(handle), SharedAppFrameClass, StringComparison.Ordinal))
        {
            Refuse(index, DropDownOutcome.SharedAppFrame);
            return;
        }

        if (!_system.IsEnabled(handle))
        {
            Refuse(index, DropDownOutcome.DialogOpen);
            return;
        }

        // Windows refuses the mark on the window of an app running as administrator while this
        // process doesn't, as it refuses moving and hiding it.
        if (!_system.Mark(handle))
        {
            Refuse(index, DropDownOutcome.RunsAsAdministrator);
            return;
        }

        _system.Unmark(handle);
        var window = new DropWindow(handle, found.ProcessId, KeyOf(app)) { Original = _system.Placement(handle) };
        if (!found.IsMinimized && !found.IsCloaked && !_system.IsMaximized(handle))
        {
            MeasureInsets(window);
        }

        Track(window);
        if (found.IsCloaked)
        {
            BringHere(window, index);
            return;
        }

        // A minimized or maximized window is hidden first, so it shows from its normal place.
        if ((found.IsMinimized || _system.IsMaximized(handle)) && !HideNow(window))
        {
            Forget(window);
            Refuse(index, DropDownOutcome.RunsAsAdministrator);
            return;
        }

        Show(window, index);
        Report(index, DropDownOutcome.Shown);
    }

    private void Show(DropWindow window, int index)
    {
        RememberFront(window);
        var display = ChooseDisplay() ?? _system.DisplayOf(window.Handle);
        if (display is null)
        {
            LogNoDisplay(_logger);
            return;
        }

        var work = display.WorkArea;
        (double Width, double Height) share = _draggedSizes.TryGetValue(window.Key, out var dragged) ? dragged : (_settings.Width / 100.0, _settings.Height / 100.0);
        var size = DropDownPlace.VisibleSize(work, share.Width, share.Height);
        var shown = DropDownPlace.Shown(work, size, window.Insets);
        var slide = ShouldSlide(display, shown);
        var start = slide ? new Rectangle(DropDownPlace.Above(shown, work, window.Insets), shown.Size) : shown;

        window.Change(DropState.Showing);
        window.Display = display;
        window.Size = size;
        var generation = window.Generation;
        if (_system.IsMinimized(window.Handle) || _system.IsMaximized(window.Handle))
        {
            // Restored from its normal place, which is set above the work area, so it never shows
            // at its old place; Windows waits for the app, so it's set off this thread.
            _system.SetPlacementLater(window.Handle, start, minimized: false, restoresMaximized: false, placed =>
            {
                if (!IsCurrent(window, generation))
                {
                    return;
                }

                if (!placed)
                {
                    LogNotPlaced(_logger, index);
                }

                Reveal(window, index, slide ? start.Location : null, shown.Location);
            });
            return;
        }

        _system.Place(window.Handle, start);
        _system.ShowWithoutActivating(window.Handle);
        Reveal(window, index, slide ? start.Location : null, shown.Location);
    }

    // Activates the window once it shows on this desktop, then slides it in.
    private void Reveal(DropWindow window, int index, Point? slideFrom, Point to)
    {
        _system.Unmark(window.Handle);
        if (_settings.KeepOnTop && !window.PutOnTop && !_system.IsTopmost(window.Handle) && _system.MarkOnTop(window.Handle))
        {
            _system.SetTopmost(window.Handle, topmost: true);
            window.PutOnTop = true;
        }

        var generation = window.Generation;
        WaitUntil(() => _system.IsVisible(window.Handle) && !_system.IsCloaked(window.Handle), here =>
        {
            if (!IsCurrent(window, generation))
            {
                return;
            }

            if (!here && _system.IsCloaked(window.Handle))
            {
                window.Change(DropState.Shown);
                Refuse(index, DropDownOutcome.StayedOnOtherDesktop);
                return;
            }

            if (!_system.Activate(window.Handle))
            {
                LogNotInFront(_logger);
            }

            if (slideFrom is { } from)
            {
                window.Slide = new WindowSlide(window.Handle, from, to, slidingOut: false, _system, _time, _post, result =>
                {
                    window.Slide = null;
                    LogSlid(_logger, "in", result.Frames, result.Moves, result.Skipped, result.Time.TotalMilliseconds);
                    InPlace(window);
                });
                window.Slide.Start();
            }
            else
            {
                InPlace(window);
            }
        });
    }

    private void InPlace(DropWindow window)
    {
        window.Change(DropState.Shown);
        foreach (var owned in window.HiddenOwned)
        {
            _system.Unmark(owned);
            _system.ShowWithoutActivating(owned);
        }

        window.HiddenOwned.Clear();
        var generation = window.Generation;
        After(SettleDelay, () => Settle(window, generation, firstCheck: true));
    }

    // Sets the size again when the app changed it as it moved, as for another display scale, and
    // centers the window from its own size when the app keeps that, as for its minimum size.
    private void Settle(DropWindow window, int generation, bool firstCheck)
    {
        if (!IsCurrent(window, generation) || window.Display is not { } display
            || _system.IsMinimized(window.Handle) || _system.IsMaximized(window.Handle)
            || _system.Bounds(window.Handle) is not { } bounds || _system.VisibleBounds(window.Handle) is not { } visible)
        {
            return;
        }

        window.Insets = FrameInsets.Between(bounds, visible);
        if (visible.Size == window.Size)
        {
            window.Placed = visible.Size;
            return;
        }

        if (firstCheck)
        {
            _system.Place(window.Handle, DropDownPlace.Shown(display.WorkArea, window.Size, window.Insets));
            After(SettleDelay, () => Settle(window, generation, firstCheck: false));
            return;
        }

        _system.Place(window.Handle, DropDownPlace.Shown(display.WorkArea, visible.Size, window.Insets));
        window.Placed = visible.Size;
        LogOwnSize(_logger);
    }

    private void Hide(DropWindow window, int index)
    {
        if (!_system.IsEnabled(window.Handle))
        {
            Refuse(index, DropDownOutcome.DialogOpen);
            return;
        }

        if (!MarkAndHideOwned(window))
        {
            Refuse(index, DropDownOutcome.RunsAsAdministrator);
            return;
        }

        // The front goes back as the slide starts, so typing goes there at once.
        var previous = window.PreviousFront;
        if (previous != 0 && previous != window.Handle && _system.Exists(previous) && _system.IsVisible(previous)
            && !_system.IsMinimized(previous) && !_system.IsCloaked(previous))
        {
            _system.GiveFront(previous);
        }

        RememberDraggedSize(window);
        SlideOutAndHide(window);
        Report(index, DropDownOutcome.Hidden);
    }

    private void SlideOutAndHide(DropWindow window)
    {
        window.Change(DropState.Hiding);
        var display = _system.DisplayOf(window.Handle) ?? window.Display;
        if (_system.Bounds(window.Handle) is { } bounds && display is not null
            && !_system.IsMinimized(window.Handle) && !_system.IsMaximized(window.Handle) && ShouldSlide(display, bounds))
        {
            var to = DropDownPlace.Above(bounds, display.WorkArea, window.Insets);
            window.Slide = new WindowSlide(window.Handle, bounds.Location, to, slidingOut: true, _system, _time, _post, result =>
            {
                window.Slide = null;
                LogSlid(_logger, "out", result.Frames, result.Moves, result.Skipped, result.Time.TotalMilliseconds);
                Hidden(window);
            });
            window.Slide.Start();
            return;
        }

        Hidden(window);
    }

    private void Hidden(DropWindow window)
    {
        _system.Hide(window.Handle);
        if (window.PutOnTop)
        {
            _system.SetTopmost(window.Handle, topmost: false);
            _system.UnmarkOnTop(window.Handle);
            window.PutOnTop = false;
        }

        window.Change(DropState.Hidden);
    }

    // Hides the window at once, without a slide or giving the front back. Returns false when
    // Windows refused the mark.
    private bool HideNow(DropWindow window)
    {
        if (!MarkAndHideOwned(window))
        {
            return false;
        }

        Hidden(window);
        return true;
    }

    // Windows doesn't hide owned windows with their owner, and they don't move with it.
    private bool MarkAndHideOwned(DropWindow window)
    {
        if (!_system.Mark(window.Handle))
        {
            return false;
        }

        foreach (var owned in _system.VisibleOwnedWindows(window.Handle))
        {
            _system.Mark(owned);
            _system.Hide(owned);
            window.HiddenOwned.Add(owned);
        }

        return true;
    }

    // Hidden on its desktop, then shown on this one after a moment, as "Virtual desktops" says.
    private void BringHere(DropWindow window, int index)
    {
        if (!_system.IsEnabled(window.Handle))
        {
            Refuse(index, DropDownOutcome.DialogOpen);
            return;
        }

        if (!HideNow(window))
        {
            Refuse(index, DropDownOutcome.RunsAsAdministrator);
            return;
        }

        var generation = window.Generation;
        After(DesktopWait, () =>
        {
            if (IsCurrent(window, generation) && window.State == DropState.Hidden)
            {
                Show(window, index);
            }
        });
        Report(index, DropDownOutcome.BroughtFromOtherDesktop);
    }

    private void RememberFront(DropWindow window)
    {
        var front = _system.ForegroundWindow;
        if (front != 0 && front != window.Handle && _system.RootOwner(front) != window.Handle)
        {
            window.PreviousFront = front;
        }
    }

    // A size the user dragged is kept for the app, as a share of the work area, until PC Companion exits.
    private void RememberDraggedSize(DropWindow window)
    {
        if (window.Placed is { } placed && window.Display is { } display && window.State == DropState.Shown
            && !_system.IsMinimized(window.Handle) && !_system.IsMaximized(window.Handle)
            && _system.VisibleBounds(window.Handle) is { } visible && visible.Size != placed)
        {
            _draggedSizes[window.Key] = DropDownPlace.ShareOf(display.WorkArea, visible.Size);
        }
    }

    private bool IsInFront(DropWindow window)
    {
        var front = _system.ForegroundWindow;
        return front != 0 && (front == window.Handle || _system.RootOwner(front) == window.Handle);
    }

    private void MeasureInsets(DropWindow window)
    {
        if (_system.Bounds(window.Handle) is { } bounds && _system.VisibleBounds(window.Handle) is { } visible)
        {
            window.Insets = FrameInsets.Between(bounds, visible);
        }
    }

    private DisplayDetails? ChooseDisplay() =>
        _settings.Display == DropDownDisplay.WithPointer && _system.PointerPosition is { } pointer
            ? _system.DisplayAt(pointer) ?? _system.MainDisplay()
            : _system.MainDisplay();

    private bool ShouldSlide(DisplayDetails display, Rectangle shown) =>
        _settings.Slide && _system.AnimationsOn && !DropDownPlace.HasDisplayAbove(display.Bounds, shown, _system.DisplayAreas());

    private DropWindow? Find(nint handle) => _windows.Values.FirstOrDefault(window => window.Handle == handle);

    private bool IsCurrent(DropWindow window, int generation) =>
        window.Generation == generation && _windows.TryGetValue(window.Key, out var tracked) && ReferenceEquals(tracked, window);

    private void Track(DropWindow window)
    {
        if (!_windows.Values.Any(other => other.ProcessId == window.ProcessId))
        {
            _system.Watch(window.ProcessId);
        }

        _windows[window.Key] = window;
    }

    private void Forget(DropWindow window)
    {
        window.Slide?.Stop();
        window.Change(DropState.Hidden);
        _windows.Remove(window.Key);
        if (!_windows.Values.Any(other => other.ProcessId == window.ProcessId))
        {
            _system.Unwatch(window.ProcessId);
        }
    }

    private void Refuse(int index, DropDownOutcome outcome)
    {
        _system.PlayWarning();
        Report(index, outcome);
    }

    private void Report(int index, DropDownOutcome outcome)
    {
        LogOutcome(_logger, index, outcome);
        Reported?.Invoke(this, new DropDownReport(index, outcome));
    }

    // Checks at once, then every 15 ms until the condition holds or ShowTimeout passes.
    private void WaitUntil(Func<bool> condition, Action<bool> done)
    {
        if (condition())
        {
            done(true);
            return;
        }

        var started = _time.GetTimestamp();
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Contains(timer))
            {
                return;
            }

            var met = condition();
            if (met || _time.GetElapsedTime(started) >= ShowTimeout)
            {
                _timers.Remove(timer);
                timer.Dispose();
                done(met);
            }
        }), null, ShowCheck, ShowCheck);
        _timers.Add(timer);
    }

    private void After(TimeSpan delay, Action action)
    {
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Remove(timer))
            {
                return;
            }

            timer.Dispose();
            action();
        }), null, delay, Timeout.InfiniteTimeSpan);
        _timers.Add(timer);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps, app {Index}: {Outcome}.")]
    private static partial void LogOutcome(ILogger logger, int index, DropDownOutcome outcome);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps, app {Index}: the app has no window the taskbar shows.")]
    private static partial void LogNoWindow(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps, app {Index}: its window is still on its way in.")]
    private static partial void LogStillShowing(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps slid a window {Direction}: {Frames} frames, {Moves} moves, {Skipped} frames without a move, {Milliseconds:F0} ms.")]
    private static partial void LogSlid(ILogger logger, string direction, int frames, int moves, int skipped, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window keeps a size of its own, so it's centered from that size.")]
    private static partial void LogOwnSize(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window closed.")]
    private static partial void LogClosed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't bring a drop-down window to the front.")]
    private static partial void LogNotInFront(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drop-down Apps, app {Index}: Windows didn't restore its minimized or maximized window at its new place.")]
    private static partial void LogNotPlaced(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drop-down Apps found no display to show a window on.")]
    private static partial void LogNoDisplay(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shortcut of Drop-down Apps' app {Index} in the settings is unknown, so it has none.")]
    private static partial void LogUnknownShortcut(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shortcut of Drop-down Apps' app {Index} in the settings belongs to an app earlier in the list, so it has none.")]
    private static partial void LogRepeatedShortcut(ILogger logger, int index);

    private enum DropState
    {
        Hidden,
        Showing,
        Shown,
        Hiding,
    }

    // A listed app's drop-down window, and what Drop-down Apps did to it.
    private sealed class DropWindow(nint handle, uint processId, string key)
    {
        public nint Handle { get; } = handle;

        public uint ProcessId { get; } = processId;

        public string Key { get; } = key;

        public DropState State { get; private set; } = DropState.Hidden;

        // Changes with every change of state, so a callback for an earlier one does nothing.
        public int Generation { get; private set; }

        public WindowPlacement? Original { get; init; }

        public FrameInsets Insets { get; set; }

        public DisplayDetails? Display { get; set; }

        // The visible size it was asked to show at, and the one it settled at.
        public Size Size { get; set; }

        public Size? Placed { get; set; }

        public nint PreviousFront { get; set; }

        public bool PutOnTop { get; set; }

        public List<nint> HiddenOwned { get; } = [];

        public WindowSlide? Slide { get; set; }

        public void Change(DropState state)
        {
            State = state;
            Generation++;
        }
    }
}
