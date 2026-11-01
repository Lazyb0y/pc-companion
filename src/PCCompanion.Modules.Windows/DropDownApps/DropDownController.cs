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

    /// <summary>How long after starting an app its first window is waited for.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long after another window comes to the front the decision to hide is made.</summary>
    public static readonly TimeSpan DecisionDelay = TimeSpan.FromMilliseconds(150);

    // Start, Search and the notification center: on top, and they pass the taskbar's test.
    private const string ShellWindowClass = "Windows.UI.Core.CoreWindow";

    // The desktop is a tool window, but a click on it does count as another app.
    private static readonly string[] DesktopClasses = ["Progman", "WorkerW"];

    private static readonly TimeSpan ShowCheck = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan StartCheck = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ButtonCheck = TimeSpan.FromMilliseconds(100);

    private readonly IDropDownSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DropWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double Width, double Height)> _draggedSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ITimer> _timers = [];
    private readonly HashSet<string> _starting = new(StringComparer.OrdinalIgnoreCase);
    private DropDownAppsSettings _settings = new();
    private IReadOnlyList<DropDownShortcut> _shortcuts = [];
    private bool _decisionPending;

    // Replaced, never changed, so the crashing thread always reads a whole list.
    private IReadOnlyList<ReleasedWindow> _released = [];

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

    /// <summary>
    /// Brings back on the taskbar, minimized, every hidden window that carries the mark, as left by
    /// a PC Companion that ended without bringing them back, takes off the top every window marked
    /// as put on top, and removes the marks. Owned windows show before their owners minimize, so
    /// they go and come back with them. Returns how many windows came back.
    /// </summary>
    public static int Recover(IDropDownSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var marked = system.FindMarked();
        var hidden = marked.Where(window => !system.IsVisible(window)).ToList();
        foreach (var owned in hidden.Where(window => system.RootOwner(window) != window))
        {
            system.ShowWithoutActivating(owned);
        }

        var recovered = 0;
        foreach (var window in hidden.Where(window => system.RootOwner(window) == window))
        {
            if (system.ShowMinimized(window))
            {
                recovered++;
            }
        }

        foreach (var window in marked)
        {
            system.Unmark(window);
        }

        foreach (var window in system.FindMarkedOnTop())
        {
            system.SetTopmost(window, topmost: false);
            system.UnmarkOnTop(window);
        }

        return recovered;
    }

    public void Apply(DropDownAppsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // A window whose app left the list is let go, as when the feature stops.
        var listed = settings.Apps.Select(KeyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _windows.Values.Where(window => !listed.Contains(window.Key)).ToList())
        {
            Release(removed);
        }

        if (!settings.KeepOnTop)
        {
            foreach (var window in _windows.Values.Where(window => window.PutOnTop))
            {
                TakeOffTop(window);
            }
        }

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

        if (FindWindow(app, shownHere: false) is { } found)
        {
            Adopt(found, index, app, report: true);
            return;
        }

        if (_starting.Contains(KeyOf(app)))
        {
            LogStillStarting(_logger, index);
            return;
        }

        Start(app);
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

    /// <summary>
    /// Another window came to the front, while a drop-down window is shown. The decision whether it
    /// hides waits a moment, then looks at the window in front then: Alt + Tab first brings hidden
    /// helper windows of the shell to the front.
    /// </summary>
    public void OnForeground()
    {
        if (_settings.HideWhenInactive == HideWhenInactive.Never || _decisionPending || !HasShownWindow)
        {
            return;
        }

        _decisionPending = true;
        After(DecisionDelay, DecideOnFront);
    }

    /// <summary>A window of a watched process started to minimize.</summary>
    public void OnMinimizeStart(nint window)
    {
        if (Find(window) is not { State: DropState.Shown } minimizing)
        {
            return;
        }

        // Once minimized, it's hidden and counts as hidden, so its shortcut brings it back.
        var generation = minimizing.Generation;
        WaitUntil(() => _system.IsMinimized(window), minimized =>
        {
            if (minimized && IsCurrent(minimizing, generation) && HideNow(minimizing))
            {
                LogMinimized(_logger);
            }
        });
    }

    /// <summary>A window of a watched process was shown.</summary>
    public void OnShown(nint window)
    {
        if (Find(window) is not { State: DropState.Hidden } hidden)
        {
            return;
        }

        // Its app, or another program, showed it. The report of a show of its own can arrive after
        // a later hide, so it's checked again a moment later, once the hide is done.
        var generation = hidden.Generation;
        After(SettleDelay, () =>
        {
            if (!IsCurrent(hidden, generation) || hidden.State != DropState.Hidden || !_system.IsVisible(window))
            {
                return;
            }

            _system.Unmark(window);
            foreach (var owned in hidden.HiddenOwned)
            {
                _system.Unmark(owned);
                _system.ShowWithoutActivating(owned);
            }

            hidden.HiddenOwned.Clear();
            hidden.Change(DropState.Shown);
            LogShownByOthers(_logger);
        });
    }

    /// <summary>
    /// Lets every drop-down window go, as PC Companion stops or the session is ending: a hidden one
    /// comes back on the taskbar, minimized and without taking the front, at the place and size it
    /// had before it first dropped down, with its owned windows; a shown one stays where it is,
    /// taken off the top when Drop-down Apps put it there. Returns how many hidden windows came back.
    /// </summary>
    public int ReleaseAll()
    {
        _starting.Clear();
        return _windows.Values.ToList().Count(Release);
    }

    /// <summary>
    /// Brings the hidden drop-down windows back on the taskbar at once, and takes those put on top
    /// off it, from any thread, as a crash ends the host. It changes nothing else, since the
    /// process is ending.
    /// </summary>
    public void ReleaseNow()
    {
        foreach (var window in Volatile.Read(ref _released))
        {
            if (window.Hidden)
            {
                foreach (var owned in window.Owned)
                {
                    _system.ShowWithoutActivating(owned);
                    _system.Unmark(owned);
                }

                _system.ShowMinimized(window.Handle);
                _system.Unmark(window.Handle);
            }

            if (window.OnTop)
            {
                _system.SetTopmost(window.Handle, topmost: false);
                _system.UnmarkOnTop(window.Handle);
            }
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

    // The app's front-most window that the taskbar shows, minimized or on another desktop too,
    // or only one shown on this desktop.
    private TopLevelWindow? FindWindow(DropDownApp app, bool shownHere)
    {
        var reader = new AppOfWindowReader(_system);
        var listed = app.App;
        foreach (var window in _system.Windows())
        {
            if (MayHaveTaskbarButton(window) && (!shownHere || !window.IsCloaked) && Find(window.Handle) is null && listed.Matches(reader.Of(window)))
            {
                return window;
            }
        }

        return null;
    }

    // Starts the app, then drops down the first window of it that the taskbar shows on this desktop
    // within 30 seconds. A Store app's frame first shows cloaked, so cloaked windows wait.
    private void Start(DropDownApp app)
    {
        var key = KeyOf(app);
        _starting.Add(key);
        _system.StartLater(app, outcome =>
        {
            if (outcome != DropDownOutcome.Started)
            {
                _starting.Remove(key);
                if (IndexOf(key) is { } refused)
                {
                    Refuse(refused, outcome);
                }

                return;
            }

            if (IndexOf(key) is { } index)
            {
                Report(index, DropDownOutcome.Started);
            }

            var started = _time.GetTimestamp();
            Repeat(StartCheck, () =>
            {
                if (!_starting.Contains(key))
                {
                    return true;
                }

                if (IndexOf(key) is not { } current)
                {
                    _starting.Remove(key);
                    return true;
                }

                var listed = _settings.Apps[current];
                if (FindWindow(listed, shownHere: true) is { } found)
                {
                    Adopt(found, current, listed, report: false);
                    return true;
                }

                if (_time.GetElapsedTime(started) < StartTimeout)
                {
                    return false;
                }

                _starting.Remove(key);
                Refuse(current, DropDownOutcome.NoWindowInTime);
                return true;
            });
        });
    }

    private int? IndexOf(string key)
    {
        for (var index = 0; index < _settings.Apps.Count; index++)
        {
            if (string.Equals(KeyOf(_settings.Apps[index]), key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    // Once one of the app's windows is taken, a start waits for no other. A start reports its own
    // result, so the window that comes of it doesn't report one.
    private void Adopt(TopLevelWindow found, int index, DropDownApp app, bool report)
    {
        _starting.Remove(KeyOf(app));
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
        var window = new DropWindow(handle, found.ProcessId, KeyOf(app), Publish) { Original = _system.Placement(handle) };
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
        if (report)
        {
            Report(index, DropDownOutcome.Shown);
        }
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
        TakeOffTop(window);
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

    // Hides the shown windows the window in front counts against, as "When another app comes to
    // the front" says. While a mouse button is held, as in a drag and drop, it waits.
    private void DecideOnFront()
    {
        if (_system.IsMouseButtonHeld)
        {
            After(ButtonCheck, DecideOnFront);
            return;
        }

        _decisionPending = false;
        var front = _system.ForegroundWindow;
        if (front == 0 || _settings.HideWhenInactive == HideWhenInactive.Never)
        {
            return;
        }

        foreach (var window in _windows.Values.Where(window => window.State == DropState.Shown).ToList())
        {
            if (CountsAsAnotherApp(front, window) && _system.IsEnabled(window.Handle) && MarkAndHideOwned(window))
            {
                RememberDraggedSize(window);
                SlideOutAndHide(window);
                LogHiddenOnFocusLoss(_logger);
            }
        }
    }

    private bool CountsAsAnotherApp(nint front, DropWindow window)
    {
        // Its own windows: those it owns, such as its dialogs, and other windows of its process.
        if (front == window.Handle || _system.RootOwner(front) == window.Handle || _system.ProcessIdOf(front) == window.ProcessId)
        {
            return false;
        }

        var className = _system.ClassName(front);
        var described = _system.Describe(front);
        if (!DesktopClasses.Contains(className, StringComparer.Ordinal)
            && (described.IsNoActivate || described.IsToolWindow || string.Equals(className, ShellWindowClass, StringComparison.Ordinal)))
        {
            return false;
        }

        return _settings.HideWhenInactive != HideWhenInactive.SameDisplay
            || _system.DisplayOf(front)?.Bounds == _system.DisplayOf(window.Handle)?.Bounds;
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
        Publish();
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

        Publish();
    }

    // Lets the window go, as "Never losing a window" says. Returns whether a hidden window came back.
    private bool Release(DropWindow window)
    {
        window.Slide?.Finish();
        var handle = window.Handle;
        var cameBack = false;
        if (_system.Exists(handle) && !_system.IsVisible(handle))
        {
            foreach (var owned in window.HiddenOwned)
            {
                _system.Unmark(owned);
                _system.ShowWithoutActivating(owned);
            }

            // A hidden window that's minimized, as one minimized while shown, keeps its normal
            // place apart, so it's set with the placement.
            var original = window.Original;
            var restoresMaximized = original is { State: WindowState.Maximized } or { State: WindowState.Minimized, RestoresMaximized: true };
            var minimized = _system.IsMinimized(handle);
            if (original is not null && !restoresMaximized && !minimized)
            {
                _system.Place(handle, original.NormalBounds);
            }

            cameBack = _system.ShowMinimized(handle);
            if (original is not null && (restoresMaximized || minimized))
            {
                // Windows waits for the app, so its normal place, and whether it comes back
                // maximized, are set off this thread, after it's on the taskbar.
                _system.SetPlacementLater(handle, original.NormalBounds, minimized: true, restoresMaximized, _ => { });
            }
        }

        _system.Unmark(handle);
        TakeOffTop(window);
        Forget(window);
        return cameBack;
    }

    private void TakeOffTop(DropWindow window)
    {
        if (window.PutOnTop)
        {
            _system.SetTopmost(window.Handle, topmost: false);
            _system.UnmarkOnTop(window.Handle);
            window.PutOnTop = false;
            Publish();
        }
    }

    // What a crash brings back, for any thread to read.
    private void Publish() =>
        Volatile.Write(ref _released, [.. _windows.Values.Select(window => new ReleasedWindow(
            window.Handle,
            [.. window.HiddenOwned],
            Hidden: window.State != DropState.Shown,
            window.PutOnTop))]);

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

    // Runs the check every interval until it returns true.
    private void Repeat(TimeSpan interval, Func<bool> check)
    {
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Contains(timer))
            {
                return;
            }

            if (check())
            {
                _timers.Remove(timer);
                timer.Dispose();
            }
        }), null, interval, interval);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps, app {Index}: the app is starting, so it isn't started again.")]
    private static partial void LogStillStarting(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps, app {Index}: its window is still on its way in.")]
    private static partial void LogStillShowing(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps slid a window {Direction}: {Frames} frames, {Moves} moves, {Skipped} frames without a move, {Milliseconds:F0} ms.")]
    private static partial void LogSlid(ILogger logger, string direction, int frames, int moves, int skipped, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window keeps a size of its own, so it's centered from that size.")]
    private static partial void LogOwnSize(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window closed.")]
    private static partial void LogClosed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window hid because another app came to the front.")]
    private static partial void LogHiddenOnFocusLoss(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drop-down window was minimized, so it's hidden.")]
    private static partial void LogMinimized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A hidden drop-down window was shown by its app or another program, so it counts as shown.")]
    private static partial void LogShownByOthers(ILogger logger);

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

    // A drop-down window as a crash brings it back.
    private sealed record ReleasedWindow(nint Handle, IReadOnlyList<nint> Owned, bool Hidden, bool OnTop);

    // A listed app's drop-down window, and what Drop-down Apps did to it.
    private sealed class DropWindow(nint handle, uint processId, string key, Action changed)
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
            changed();
        }
    }
}
