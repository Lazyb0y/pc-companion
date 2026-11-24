using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Window Tabs' view of Windows. Without a window, as when the feature only brings back windows
/// left marked, it can't watch processes or wait for frames.
/// </summary>
public sealed class WindowsTabsSystem : ITabsSystem, IDisposable
{
    /// <summary>The mark's name, which PC Companion keeps from one version to the next.</summary>
    public const string MarkName = "PCCompanion.WindowTabs";

    // The mark's values: a tab, and an owned window hidden with its tab.
    private const nint TabMark = 1;
    private const nint OwnedMark = 2;

    // Within the host's wait of 2 seconds for the feature's thread as it exits.
    private static readonly TimeSpan PlacementWait = TimeSpan.FromSeconds(1.5);

    private static readonly Lazy<WindowMark> Marks = new(() => new WindowMark(MarkName));

    // Minimize to Tray's mark, on the windows in its tray.
    private static readonly Lazy<WindowMark> TrayMarks = new(() => new WindowMark(WindowsTrayWindowsSystem.MarkName));

    private readonly MessageWindow? _window;
    private readonly Action<Action> _guard;
    private readonly DropDownWindows? _dropDown;
    private readonly Dictionary<uint, (int Count, WinEventHook Shows, WinEventHook Titles)> _watched = [];
    private readonly Dictionary<uint, (int Count, WinEventHook Moves)> _followed = [];
    private readonly List<Task> _placements = [];

    /// <param name="window">The feature thread's window, where reports and waits end.</param>
    /// <param name="guard">Runs a report inside the feature thread's error boundary.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    public WindowsTabsSystem(MessageWindow? window, Action<Action> guard, DropDownWindows? dropDown)
    {
        ArgumentNullException.ThrowIfNull(guard);

        _window = window;
        _guard = guard;
        _dropDown = dropDown;
    }

    public event EventHandler<nint>? WindowShown;

    public event EventHandler<nint>? WindowHidden;

    public event EventHandler<nint>? WindowDestroyed;

    public event EventHandler<nint>? TitleChanged;

    public event EventHandler<nint>? WindowMoved;

    public nint ForegroundWindow => Foreground.Window;

    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsControlHeld => InputState.IsModifierHeld(ModifierKey.Control);

    public TabRefusal Check(nint window)
    {
        if (!TopLevelWindows.Exists(window))
        {
            return TabRefusal.Gone;
        }

        var described = TopLevelWindows.Describe(window);
        var className = TopLevelWindows.ClassName(window);
        if (!TopLevelWindows.MayHaveTaskbarButton(window) || described.IsDesktop || className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return TabRefusal.NotOnTaskbar;
        }

        if (className == "ApplicationFrameWindow")
        {
            return TabRefusal.SharedAppFrame;
        }

        // The stand-in Windows shows for a window that stopped responding.
        if (className == "Ghost" || TopLevelWindows.IsNotResponding(window))
        {
            return TabRefusal.NotResponding;
        }

        if (_dropDown?.Contains(window) == true || TrayMarks.Value.IsOn(window))
        {
            return TabRefusal.OtherFeature;
        }

        return !TopLevelWindows.IsResizable(window) ? TabRefusal.NotResizable
            : !TopLevelWindows.IsEnabled(window) ? TabRefusal.DialogOpen
            : TopLevelWindows.IsMinimized(window) ? TabRefusal.Minimized
            : TopLevelWindows.IsCloaked(window) ? TabRefusal.OtherDesktop
            : FullScreenCheck.Covers(window) ? TabRefusal.FullScreen
            : !CanMark(window) ? TabRefusal.RunsAsAdministrator
            : TabRefusal.None;
    }

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsVisible(nint window) => TopLevelWindows.IsVisible(window);

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public bool IsMaximized(nint window) => TopLevelWindows.IsMaximized(window);

    public bool IsCloaked(nint window) => TopLevelWindows.IsCloaked(window);

    public bool IsEnabled(nint window) => TopLevelWindows.IsEnabled(window);

    public bool IsFullScreen(nint window) => FullScreenCheck.Covers(window);

    public uint ProcessIdOf(nint window) => TopLevelWindows.ProcessIdOf(window);

    public Rectangle? Bounds(nint window) => TopLevelWindows.Bounds(window);

    public Rectangle? VisibleBounds(nint window) => TopLevelWindows.VisibleBounds(window);

    public WindowPlacement? Placement(nint window) => TopLevelWindows.Placement(window);

    public IReadOnlyList<nint> VisibleOwnedWindows(nint window) => TopLevelWindows.VisibleOwnedWindows(window);

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    // The front-most window at the point decides: a window over the one under the pointer, such as
    // a dialog, hides it.
    public nint WindowAt(Point point, nint except)
    {
        nint found = 0;
        var ownProcess = (uint)Environment.ProcessId;
        TopLevelWindows.FindFrontToBack(place =>
        {
            if (place.Handle == except || place.IsCloaked || place.IsMinimized || place.IsClickThrough || place.IsNoActivate || place.IsToolWindow
                || !place.Bounds.Contains(point) || TopLevelWindows.ProcessIdOf(place.Handle) == ownProcess
                || TopLevelWindows.VisibleBounds(place.Handle) is not { } visible || !visible.Contains(point))
            {
                return null;
            }

            if (TopLevelWindows.MayHaveTaskbarButton(place.Handle))
            {
                found = place.Handle;
            }

            return true;
        });

        return found;
    }

    public DisplayDetails? DisplayOf(nint window) => Displays.Of(window);

    public bool Place(nint window, Rectangle bounds) => TopLevelWindows.Place(window, bounds);

    public bool Show(nint window, TabShow how) => how switch
    {
        TabShow.Maximized => TopLevelWindows.ShowMaximized(window),
        TabShow.Restored => TopLevelWindows.ShowRestored(window),
        _ => TopLevelWindows.ShowAsItWas(window),
    };

    public bool ShowWithoutActivating(nint window) => TopLevelWindows.ShowWithoutActivating(window);

    public bool Hide(nint window) => TopLevelWindows.Hide(window);

    public bool ShowMinimized(nint window) => TopLevelWindows.ShowMinimized(window);

    public bool Activate(nint window) => TopLevelWindows.Activate(window);

    public bool AskToClose(nint window) => TopLevelWindows.AskToClose(window);

    public void SetHiddenPlacementLater(nint window, Rectangle normalBounds)
    {
        _placements.RemoveAll(placement => placement.IsCompleted);
        _placements.Add(Task.Run(() => TopLevelWindows.SetHiddenPlacement(window, normalBounds)));
    }

    public void AfterFrames(int frames, Action done)
    {
        ArgumentNullException.ThrowIfNull(done);

        var thread = _window ?? throw new InvalidOperationException("Waiting for frames needs the feature thread's window.");
        _ = Task.Run(() =>
        {
            for (var frame = 0; frame < frames; frame++)
            {
                DesktopComposition.WaitForNextFrame();
            }

            thread.BeginInvoke(() => _guard(done));
        });
    }

    public bool Mark(nint window, bool owned) => Marks.Value.Set(window, owned ? OwnedMark : TabMark);

    public void Unmark(nint window) => Marks.Value.Remove(window);

    public IReadOnlyList<(nint Window, bool Owned)> FindMarked() =>
        [.. Marks.Value.FindMarked().Select(window => (window, Marks.Value.Value(window) == OwnedMark))];

    public void PlayWarning() => WarningSound.Play();

    public void Watch(uint processId)
    {
        if (_watched.TryGetValue(processId, out var watched))
        {
            _watched[processId] = watched with { Count = watched.Count + 1 };
            return;
        }

        RequireWindow();

        // Out-of-context hooks for that process only, which report on this thread.
        var shows = new WinEventHook(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectHideEvent, processId);
        WinEventHook titles;
        try
        {
            titles = new WinEventHook(WinEventHook.ObjectNameChangeEvent, WinEventHook.ObjectNameChangeEvent, processId);
        }
        catch
        {
            shows.Dispose();
            throw;
        }

        shows.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                (e.EventType switch
                {
                    WinEventHook.ObjectShowEvent => WindowShown,
                    WinEventHook.ObjectHideEvent => WindowHidden,
                    _ => WindowDestroyed,
                })?.Invoke(this, e.Window);
            }
        });
        titles.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                TitleChanged?.Invoke(this, e.Window);
            }
        });
        _watched[processId] = (1, shows, titles);
    }

    public void Unwatch(uint processId)
    {
        if (!_watched.TryGetValue(processId, out var watched))
        {
            return;
        }

        if (watched.Count > 1)
        {
            _watched[processId] = watched with { Count = watched.Count - 1 };
            return;
        }

        _watched.Remove(processId);
        watched.Shows.Dispose();
        watched.Titles.Dispose();
    }

    public void FollowMoves(uint processId)
    {
        if (_followed.TryGetValue(processId, out var followed))
        {
            _followed[processId] = followed with { Count = followed.Count + 1 };
            return;
        }

        RequireWindow();
        var moves = new WinEventHook(WinEventHook.LocationChangeEvent, WinEventHook.LocationChangeEvent, processId);
        moves.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                WindowMoved?.Invoke(this, e.Window);
            }
        });
        _followed[processId] = (1, moves);
    }

    public void StopFollowingMoves(uint processId)
    {
        if (!_followed.TryGetValue(processId, out var followed))
        {
            return;
        }

        if (followed.Count > 1)
        {
            _followed[processId] = followed with { Count = followed.Count - 1 };
            return;
        }

        _followed.Remove(processId);
        followed.Moves.Dispose();
    }

    // Hidden tabs that were given the group's place as the feature stopped are placed before the
    // host ends, within its wait for the feature's thread.
    public void Dispose()
    {
        foreach (var (_, watched) in _watched)
        {
            watched.Shows.Dispose();
            watched.Titles.Dispose();
        }

        foreach (var (_, followed) in _followed)
        {
            followed.Moves.Dispose();
        }

        _watched.Clear();
        _followed.Clear();
        _ = Task.WaitAll([.. _placements], PlacementWait);
    }

    // Windows refuses a program without admin rights the mark on a window of an app running as
    // administrator; a window that carries the mark already, as a hidden tab, keeps it.
    private static bool CanMark(nint window)
    {
        if (Marks.Value.IsOn(window))
        {
            return true;
        }

        if (!Marks.Value.Set(window))
        {
            return false;
        }

        Marks.Value.Remove(window);
        return true;
    }

    private void RequireWindow()
    {
        if (_window is null)
        {
            throw new InvalidOperationException("Watching needs the feature thread's window.");
        }
    }
}
