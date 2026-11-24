using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Windows for <see cref="TabsController"/>: windows in an order front to back, with invisible
/// borders of 7 pixels left, right and below, on one display. Shows and hides are reported to the
/// controller through the fake feature thread, as WinEvent hooks report them.
/// </summary>
internal sealed class FakeTabsSystem(Action<Action> post) : ITabsSystem
{
    public static readonly Rectangle Display = new(0, 0, 1920, 1080);
    public static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    private readonly List<FakeTabWindow> _order = [];
    private readonly Queue<Action> _frames = new();

    public TabsController? Controller { get; set; }

    public nint ForegroundWindow { get; set; }

    public Point? PointerPosition { get; set; }

    public bool IsControlHeld { get; set; }

    public int Warnings { get; private set; }

    public List<string> Calls { get; } = [];

    public Dictionary<nint, bool> Marks { get; } = [];

    public Dictionary<uint, int> Watched { get; } = [];

    public Dictionary<uint, int> Followed { get; } = [];

    public List<(nint Window, Rectangle Normal)> HiddenPlacements { get; } = [];

    public FakeTabWindow Add(nint handle, Rectangle visible, nint owner = 0)
    {
        var window = new FakeTabWindow(handle) { Visible = visible, Owner = owner, Normal = Inflate(visible) };
        _order.Insert(0, window);
        return window;
    }

    public FakeTabWindow this[nint handle] => _order.Single(window => window.Handle == handle);

    /// <summary>Runs what waited for frames to be drawn.</summary>
    public void RunFrames()
    {
        while (_frames.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>A hung app's window does what it was asked once its app responds again.</summary>
    public void Respond(nint handle)
    {
        var window = this[handle];
        window.Hung = false;
        foreach (var pending in window.Pending.ToList())
        {
            pending();
        }

        window.Pending.Clear();
    }

    /// <summary>The window's app shows or hides it, or it closes, as Windows reports to the hooks.</summary>
    public void AppShows(nint handle) => SetShown(this[handle], true);

    public void AppHides(nint handle) => SetShown(this[handle], false);

    public void Close(nint handle)
    {
        var window = this[handle];
        window.Exists = false;
        _order.Remove(window);
        post(() => Controller?.OnDestroyed(handle));
    }

    public TabRefusal Check(nint window) =>
        Find(window) is not { } found ? TabRefusal.Gone
        : found.Refusal != TabRefusal.None ? found.Refusal
        : !found.Enabled ? TabRefusal.DialogOpen
        : found.Minimized ? TabRefusal.Minimized
        : found.FullScreen ? TabRefusal.FullScreen
        : found.Cloaked ? TabRefusal.OtherDesktop
        : TabRefusal.None;

    public bool Exists(nint window) => Find(window) is not null;

    public bool IsVisible(nint window) => Find(window)?.Shown ?? false;

    public bool IsMinimized(nint window) => Find(window)?.Minimized ?? false;

    public bool IsMaximized(nint window) => Find(window)?.Maximized ?? false;

    public bool IsCloaked(nint window) => Find(window)?.Cloaked ?? false;

    public bool IsEnabled(nint window) => Find(window)?.Enabled ?? false;

    public bool IsFullScreen(nint window) => Find(window)?.FullScreen ?? false;

    public uint ProcessIdOf(nint window) => (uint)window;

    public Rectangle? Bounds(nint window) => Find(window) is { } found ? Inflate(found.Visible) : null;

    public Rectangle? VisibleBounds(nint window) => Find(window)?.Visible;

    public WindowPlacement? Placement(nint window) =>
        Find(window) is { } found ? new WindowPlacement(found.Normal, found.Minimized ? WindowState.Minimized : found.Maximized ? WindowState.Maximized : WindowState.Normal, false) : null;

    public IReadOnlyList<nint> VisibleOwnedWindows(nint window) => [.. _order.Where(other => other.Owner == window && other.Shown).Select(other => other.Handle)];

    public nint RootOwner(nint window)
    {
        var current = window;
        while (Find(current) is { Owner: not 0 } found)
        {
            current = found.Owner;
        }

        return current;
    }

    public nint WindowAt(Point point, nint except) =>
        _order.FirstOrDefault(window => window.Handle != except && window.Shown && window.Owner == 0 && window.Visible.Contains(point))?.Handle ?? 0;

    public DisplayDetails? DisplayOf(nint window) => Find(window) is { } found ? new DisplayDetails(Display, WorkArea, found.Dpi) : null;

    // Moving a window that isn't maximized moves its normal place too.
    public bool Place(nint window, Rectangle bounds) => Do(window, $"place {window} {Deflate(bounds)}", found =>
    {
        found.Visible = Deflate(bounds);
        if (!found.Maximized)
        {
            found.Normal = bounds;
        }
    });

    public bool Show(nint window, TabShow how) => Do(window, $"show {window} {how}", found =>
    {
        found.Maximized = how == TabShow.Maximized || (how == TabShow.AsItWas && found.Maximized);
        found.Minimized = false;
        if (found.Maximized)
        {
            found.Visible = WorkArea;
        }

        SetShown(found, true);
        Activate(window);
    });

    public bool ShowWithoutActivating(nint window) => Do(window, $"show without activating {window}", found => SetShown(found, true));

    public bool Hide(nint window) => Do(window, $"hide {window}", found => SetShown(found, false));

    public bool ShowMinimized(nint window) => Do(window, $"show minimized {window}", found =>
    {
        found.Minimized = true;
        SetShown(found, true);
    });

    public bool Activate(nint window) => Do(window, $"activate {window}", found =>
    {
        _order.Remove(found);
        _order.Insert(0, found);
        ForegroundWindow = window;
    });

    public bool AskToClose(nint window)
    {
        Calls.Add($"close {window}");
        return true;
    }

    public void SetHiddenPlacementLater(nint window, Rectangle normalBounds)
    {
        HiddenPlacements.Add((window, normalBounds));
        if (Find(window) is { } found)
        {
            found.Normal = normalBounds;
            if (!found.Maximized)
            {
                found.Visible = Deflate(normalBounds);
            }
        }
    }

    public void AfterFrames(int frames, Action done) => _frames.Enqueue(() => post(done));

    public bool Mark(nint window, bool owned)
    {
        if (Find(window) is not { MarkRefused: false })
        {
            return false;
        }

        Marks[window] = owned;
        return true;
    }

    public void Unmark(nint window) => Marks.Remove(window);

    public IReadOnlyList<(nint Window, bool Owned)> FindMarked() => [.. Marks.Where(mark => Exists(mark.Key)).Select(mark => (mark.Key, mark.Value))];

    public void PlayWarning() => Warnings++;

    public void Watch(uint processId) => Watched[processId] = Watched.GetValueOrDefault(processId) + 1;

    public void Unwatch(uint processId) => Count(Watched, processId);

    public void FollowMoves(uint processId) => Followed[processId] = Followed.GetValueOrDefault(processId) + 1;

    public void StopFollowingMoves(uint processId) => Count(Followed, processId);

    private static void Count(Dictionary<uint, int> counts, uint processId)
    {
        if (counts.TryGetValue(processId, out var count))
        {
            if (count <= 1)
            {
                counts.Remove(processId);
            }
            else
            {
                counts[processId] = count - 1;
            }
        }
    }

    private static Rectangle Inflate(Rectangle visible) => Rectangle.FromLTRB(visible.Left - 7, visible.Top, visible.Right + 7, visible.Bottom + 7);

    private static Rectangle Deflate(Rectangle bounds) => Rectangle.FromLTRB(bounds.Left + 7, bounds.Top, bounds.Right - 7, bounds.Bottom - 7);

    private FakeTabWindow? Find(nint window) => _order.Find(found => found.Handle == window && found.Exists);

    // An app that stopped responding carries out what it was asked once it responds.
    private bool Do(nint window, string call, Action<FakeTabWindow> change)
    {
        Calls.Add(call);
        if (Find(window) is not { } found || found.Refused)
        {
            return false;
        }

        if (found.Hung)
        {
            found.Pending.Add(() => change(found));
        }
        else
        {
            change(found);
        }

        return true;
    }

    private void SetShown(FakeTabWindow window, bool shown)
    {
        if (window.Shown == shown)
        {
            return;
        }

        window.Shown = shown;
        var handle = window.Handle;
        post(() =>
        {
            if (shown)
            {
                Controller?.OnShown(handle);
            }
            else
            {
                Controller?.OnHidden(handle);
            }
        });
    }
}

internal sealed class FakeTabWindow(nint handle)
{
    public nint Handle { get; } = handle;

    public nint Owner { get; set; }

    public Rectangle Visible { get; set; }

    public Rectangle Normal { get; set; }

    public bool Shown { get; set; } = true;

    public bool Exists { get; set; } = true;

    public bool Minimized { get; set; }

    public bool Maximized { get; set; }

    public bool Cloaked { get; set; }

    public bool Enabled { get; set; } = true;

    public bool FullScreen { get; set; }

    public bool Hung { get; set; }

    /// <summary>The scale of the display the window is on.</summary>
    public uint Dpi { get; set; } = 96;

    public bool Refused { get; set; }

    public bool MarkRefused { get; set; }

    public TabRefusal Refusal { get; set; }

    public List<Action> Pending { get; } = [];
}

internal sealed class FakeTabStrip : ITabStrip
{
    public event EventHandler<int>? TabChosen;

    public event EventHandler<TabDrop>? TabDropped;

    public event EventHandler<TabMenuChoice>? MenuChosen;

    public bool IsPointerOver { get; set; }

    public StripView? View { get; private set; }

    public bool Shown { get; private set; }

    public bool IsDropTarget { get; private set; }

    public bool Disposed { get; private set; }

    public nint Above { get; private set; }

    public List<string> Announcements { get; } = [];

    public List<nint> TitleChanges { get; } = [];

    public void Show(StripView view, nint window)
    {
        View = view;
        Shown = true;
        Above = window;
    }

    public void Hide() => Shown = false;

    public void SetDropTarget(bool isTarget) => IsDropTarget = isTarget;

    public void TitleChanged(nint window) => TitleChanges.Add(window);

    public void Announce(string text) => Announcements.Add(text);

    public void Choose(int index) => TabChosen?.Invoke(this, index);

    public void Drop(int index, Point point) => TabDropped?.Invoke(this, new TabDrop(index, point));

    public void Menu(int index, TabMenuItem item) => MenuChosen?.Invoke(this, new TabMenuChoice(index, item));

    public void Dispose()
    {
        Disposed = true;
        Shown = false;
    }
}

internal sealed class FakeDropHighlight : IDropHighlight
{
    public Rectangle? Area { get; private set; }

    public void Show(Rectangle area) => Area = area;

    public void Hide() => Area = null;

    public void Dispose() => Area = null;
}
