using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Window Tabs' decisions: making groups from dropped windows and from the keyboard, switching their tabs, following
/// what the windows' apps do, keeping groups for after a restart and making them again, and never losing a hidden
/// tab. See docs/features/windows/window-tabs.md.
/// Use it on the feature's thread.
/// </summary>
public sealed partial class TabsController : IDisposable
{
    private static readonly TimeSpan SyncDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ShowCheck = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan FrameCheckDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PointerCheck = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RestTime = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RevealLinger = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RevealAfterKeys = TimeSpan.FromSeconds(1.5);

    // How often kept groups' windows are looked for while groups wait to be made again, and how
    // soon after a window shows.
    private static readonly TimeSpan RegroupInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RegroupSoon = TimeSpan.FromMilliseconds(100);

    // Frames to wait after the new tab shows before the old one hides, so there's never a frame
    // with neither (spike).
    private const int FramesBeforeHiding = 2;

    // Effective pixels: how far into a window's top the pointer rests to show a strip that's over
    // it, and how far a tab taken out moves from its group.
    private const int RevealBand = 2;
    private const int TakeOutOffset = 32;

    private readonly ITabsSystem _system;
    private readonly Func<ITabStrip> _newStrip;
    private readonly IDropHighlight _highlight;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly WindowTabsWindows? _shared;
    private readonly bool _rightToLeft;
    private readonly Func<int, int, string> _position;
    private readonly Action _openSettings;
    private readonly List<Group> _groups = [];
    private readonly HashSet<nint> _expectShown = [];
    private readonly HashSet<nint> _expectHidden = [];
    private readonly Dictionary<nint, Insets> _insets = [];

    // The scale of the display each hidden tab was hidden on.
    private readonly Dictionary<nint, uint> _hiddenDpi = [];

    // Keep groups after a restart: the groups kept, and each tab's app, read once as it joins.
    private readonly KeptGroups _kept = new();
    private readonly Dictionary<nint, KeptTab?> _apps = [];
    private readonly List<ITimer> _timers = [];
    private WindowTabsSettings _settings = new();
    private Drag? _drag;
    private ITimer? _pointerTimer;
    private ITimer? _regroupTimer;
    private ITimer? _regroupSoon;
    private long _regroupUntil;
    private nint _lastForeground;
    private bool _disposed;

    // The hidden tabs and their hidden owned windows, for bringing them back from another thread
    // as the host crashes; replaced, never changed.
    private (nint Window, bool Owned)[] _hidden = [];

    /// <param name="newStrip">Makes a strip for a new group.</param>
    /// <param name="highlight">The highlight on a window's top while a window dragged with Ctrl held would group with it.</param>
    /// <param name="post">Runs an action on the feature's thread later, inside its error boundary.</param>
    /// <param name="shared">The tabs, as Minimize to Tray and Drop-down Apps read them.</param>
    /// <param name="rightToLeft">Whether the user's language reads right to left.</param>
    /// <param name="position">The text a screen reader announces for a tab's position, such as "Tab 2 of 3".</param>
    /// <param name="openSettings">Opens the feature's page in the settings app.</param>
    public TabsController(
        ITabsSystem system,
        Func<ITabStrip> newStrip,
        IDropHighlight highlight,
        TimeProvider time,
        Action<Action> post,
        ILogger logger,
        WindowTabsWindows? shared,
        bool rightToLeft,
        Func<int, int, string> position,
        Action openSettings)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(newStrip);
        ArgumentNullException.ThrowIfNull(highlight);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(openSettings);

        _system = system;
        _newStrip = newStrip;
        _highlight = highlight;
        _time = time;
        _post = post;
        _logger = logger;
        _shared = shared;
        _rightToLeft = rightToLeft;
        _position = position;
        _openSettings = openSettings;
    }

    /// <summary>The groups' tabs, the first tab first, for the logs and the tests.</summary>
    public IReadOnlyList<IReadOnlyList<nint>> Groups => [.. _groups.Select(group => (IReadOnlyList<nint>)[.. group.Tabs])];

    /// <summary>The window of each group's tab in front, in the order of <see cref="Groups"/>.</summary>
    public IReadOnlyList<nint> Fronts => [.. _groups.Select(group => group.Front >= 0 ? group.FrontWindow : 0)];

    /// <summary>The groups kept for Keep groups after a restart, as <c>window-tabs.json</c> keeps them.</summary>
    public IReadOnlyList<KeptGroup> Kept => _kept.Saved;

    /// <summary>Whether groups read from the file wait to be made again, and windows that show are looked at.</summary>
    public bool IsRegrouping => _regroupTimer is not null;

    /// <summary>Raised on the feature's thread when what's kept for Keep groups after a restart changed.</summary>
    public event EventHandler? KeptChanged;

    /// <summary>Raised on the feature's thread when no group read from the file waits any more.</summary>
    public event EventHandler? RegroupingEnded;

    public void Apply(WindowTabsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var keeping = _settings.KeepGroups;
        _settings = settings;

        // Turning Keep groups after a restart off forgets every group; turning it on keeps the ones there are.
        if (keeping && !settings.KeepGroups)
        {
            StopRegrouping();
            if (_kept.ForgetAll())
            {
                KeptChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (!keeping && settings.KeepGroups)
        {
            KeepAll();
        }
    }

    /// <summary>
    /// Brings back on the taskbar, minimized, every hidden window that carries Window Tabs' mark,
    /// as after PC Companion ended without bringing them back, and removes the marks. Returns how
    /// many tabs came back. It waits for no program.
    /// </summary>
    public static int Recover(ITabsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var marked = system.FindMarked();
        var count = 0;

        // Owned windows first: a tab that minimizes hides them, and brings them back with it.
        foreach (var (window, _) in marked.Where(entry => entry.Owned && !system.IsVisible(entry.Window)))
        {
            system.ShowWithoutActivating(window);
        }

        foreach (var (window, _) in marked.Where(entry => !entry.Owned && !system.IsVisible(entry.Window)))
        {
            system.ShowMinimized(window);
            count++;
        }

        foreach (var (window, _) in marked)
        {
            system.Unmark(window);
        }

        return count;
    }

    public void OnForeground(nint window)
    {
        _lastForeground = window;
        if (GroupOfFront(_system.RootOwner(window)) is { } group)
        {
            ShowStrip(group);
        }

        WatchPointer();
    }

    public void OnMoveSizeStart(nint window)
    {
        if (GroupOfFront(window) is { } group)
        {
            // The user places the window now, so the check after a switch doesn't move it back.
            group.Moving = true;
            CancelTimer(ref group.FrameTimer);
            return;
        }

        if (_drag is not null || GroupOf(window) is not null || !_system.Exists(window))
        {
            return;
        }

        var refusal = _system.Check(window);
        if (refusal is TabRefusal.NotOnTaskbar or TabRefusal.Gone || _system.Bounds(window) is not { } bounds)
        {
            return;
        }

        _drag = new Drag(window, refusal, _system.ProcessIdOf(window), bounds.Size);
        _system.FollowMoves(_drag.ProcessId);
    }

    public void OnMoveSizeEnd(nint window)
    {
        if (GroupOfFront(window) is { } group)
        {
            group.Moving = false;
            Sync(group);
            ShowStrip(group);
            return;
        }

        if (_drag?.Window != window)
        {
            return;
        }

        UpdateDrag();
        var drag = EndDrag();
        if (drag is null)
        {
            return;
        }

        if (drag.StripTarget is { } target && _groups.Contains(target))
        {
            Join(target, window, drag.Insertion, keys: false);
        }
        else if (drag.TitleTarget != 0)
        {
            JoinWindow(drag.TitleTarget, window, keys: false);
        }
        else if (drag.OverRefused != TabRefusal.None)
        {
            Refuse(drag.OverRefused);
        }
    }

    public void OnLocationChange(nint window)
    {
        if (_drag?.Window == window)
        {
            UpdateDrag();
            return;
        }

        if (GroupOfFront(window) is not { } group)
        {
            return;
        }

        ShowStrip(group);
        if (!group.Moving)
        {
            After(ref group.SyncTimer, SyncDelay, () => Sync(group));
        }
    }

    public void OnMinimizeStart(nint window)
    {
        if (GroupOfFront(window) is { } group)
        {
            group.Minimized = true;
            group.Strip.Hide();
        }
    }

    public void OnMinimizeEnd(nint window)
    {
        if (GroupOfFront(window) is { } group)
        {
            group.Minimized = false;
            ShowStrip(group);
        }
    }

    public void OnCloakChanged(nint window)
    {
        if (GroupOfFront(window) is { } group)
        {
            ShowStrip(group);
        }
    }

    public void OnTitleChanged(nint window)
    {
        if (GroupOf(window) is { } group)
        {
            group.Strip.TitleChanged(window);
        }
    }

    /// <summary>A window of a watched process showed: an app may have shown a hidden tab itself.</summary>
    public void OnShown(nint window)
    {
        var expected = _expectShown.Remove(window);
        if (GroupOf(window) is not { } group)
        {
            return;
        }

        var index = group.Tabs.IndexOf(window);

        // The tab in front shows once its app does it, as after the tab before it left the group,
        // and its strip comes back with it.
        if (index == group.Front)
        {
            ShowStrip(group);
            return;
        }

        if (index == group.Chosen)
        {
            return;
        }

        // A tab whose app showed it only after the user chose another one hides again.
        if (expected)
        {
            PutAway(group, window);
            return;
        }

        _system.Unmark(window);
        ShowOwned(group, window);

        // A hidden tab shows on the desktop that's showing, so it can't join a group on another.
        if (group.Front >= 0 && _system.IsCloaked(group.FrontWindow))
        {
            RemoveTab(group, window);
            LogLeft(_logger, "its app showed it while its group was on another virtual desktop", group.Tabs.Count);
            EndIfAlone(group);
            return;
        }

        LogAppShowed(_logger);
        group.Chosen = index;
        var ticket = ++group.Switches;
        if (!group.Maximized && !group.Frame.IsEmpty)
        {
            _system.Place(window, RectLike(window, group.Frame));
        }

        _system.AfterFrames(FramesBeforeHiding, () => Shown(group, window, ticket));
    }

    /// <summary>A window of a watched process hid: an app may have hidden its tab in front itself.</summary>
    public void OnHidden(nint window)
    {
        if (_expectHidden.Remove(window) || GroupOf(window) is not { } group || group.Front < 0 || group.FrontWindow != window)
        {
            return;
        }

        // Window Tabs never shows a window its app hid, so it leaves the group.
        var index = group.Front;
        RemoveTab(group, window);
        LogLeft(_logger, "its app hid it", group.Tabs.Count);
        ShowAfterLoss(group, index, activate: false);
    }

    public void OnDestroyed(nint window)
    {
        _expectShown.Remove(window);
        _expectHidden.Remove(window);
        _insets.Remove(window);
        _hiddenDpi.Remove(window);
        if (_drag?.Window == window)
        {
            EndDrag();
        }

        if (GroupOf(window) is not { } group)
        {
            return;
        }

        var index = group.Tabs.IndexOf(window);
        var wasFront = index == group.Front;
        var hadFront = _lastForeground == window;
        RemoveTab(group, window);
        LogLeft(_logger, "it closed", group.Tabs.Count);
        if (wasFront)
        {
            ShowAfterLoss(group, index, hadFront);
        }
        else
        {
            EndIfAlone(group);
        }
    }

    /// <summary>Shows the next tab of the group in front, going round from the last to the first.</summary>
    public void Next() => Step(1);

    /// <summary>Shows the previous tab of the group in front, going round from the first to the last.</summary>
    public void Previous() => Step(-1);

    /// <summary>Moves the tab in front one place along its strip, toward the last tab.</summary>
    public void MoveForward() => Move(1);

    /// <summary>Moves the tab in front one place along its strip, toward the first tab.</summary>
    public void MoveBack() => Move(-1);

    /// <summary>
    /// Adds the window in front to the group of the window behind it, or starts a group of the
    /// two, as Windows + Ctrl + T does. A tab in front moves to that group.
    /// </summary>
    public void GroupFrontWithBehind() => GroupWithBehind(_system.RootOwner(_system.ForegroundWindow), keys: true);

    /// <summary>Takes the tab in front out of its group, as Windows + Ctrl + Shift + T does.</summary>
    public void TakeOutFront() => TakeOutOf(_system.RootOwner(_system.ForegroundWindow));

    /// <summary>
    /// Adds <paramref name="window"/> to the group of the window behind it, or starts a group of
    /// the two, as Windows + Ctrl + T does for the window in front.
    /// </summary>
    public void GroupWithBehind(nint window) => GroupWithBehind(window, keys: false);

    /// <summary>
    /// Takes <paramref name="window"/> out of its group, as Windows + Ctrl + Shift + T does for the
    /// window in front. It must be its group's tab in front.
    /// </summary>
    public void TakeOutOf(nint window)
    {
        if (GroupOfFront(window) is not { } group)
        {
            LogNotATab(_logger);
            _system.PlayWarning();
            return;
        }

        TakeOut(group, group.Front, at: null);
    }

    /// <summary>
    /// Makes the groups read from <c>window-tabs.json</c> again, as Keep groups after a restart
    /// does: each as soon as each of its apps has a shown or minimized window in no group, and at
    /// <paramref name="until"/>, a timestamp of the controller's clock, with the windows there then
    /// when there are at least two. It looks once a second, and soon after <see cref="OnNewWindow"/>.
    /// </summary>
    public void StartRegrouping(IReadOnlyList<KeptGroup?> groups, long until)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (_disposed || !_settings.KeepGroups)
        {
            return;
        }

        var read = _kept.Load(groups);
        LogKeptRead(_logger, read);
        if (_kept.Waiting == 0)
        {
            return;
        }

        _regroupUntil = until;
        _regroupTimer ??= _time.CreateTimer(_ => _post(Regroup), null, RegroupInterval, RegroupInterval);
        Regroup();
    }

    /// <summary>A window showed, in any app: while groups wait to be made again, its app's may be there now.</summary>
    public void OnNewWindow(nint window)
    {
        if (IsRegrouping && _regroupSoon is null && GroupOf(window) is null)
        {
            After(ref _regroupSoon, RegroupSoon, () =>
            {
                _regroupSoon = null;
                Regroup();
            });
        }
    }

    /// <summary>
    /// Brings every hidden tab back on the taskbar, minimized, without taking the front, and ends
    /// every group, as when PC Companion exits or the session ends; the groups stay kept, so the
    /// next start makes them again. Returns how many came back.
    /// </summary>
    public int ReleaseAll()
    {
        StopRegrouping();
        var count = 0;
        foreach (var group in _groups.ToList())
        {
            foreach (var tab in group.Tabs)
            {
                if (_system.Exists(tab) && !_system.IsVisible(tab))
                {
                    ShowOwned(group, tab);
                    _system.ShowMinimized(tab);
                    count++;
                }

                _system.Unmark(tab);
            }

            EndGroup(group, "Window Tabs stopped", keep: true);
        }

        EndDrag();
        return count;
    }

    /// <summary>
    /// Brings the hidden tabs back on the taskbar at once, from any thread, as the host crashes.
    /// Nothing else changes, and the marks stay for the next start.
    /// </summary>
    public void ReleaseNow()
    {
        var hidden = Volatile.Read(ref _hidden);
        foreach (var (window, _) in hidden.Where(entry => entry.Owned))
        {
            _system.ShowWithoutActivating(window);
        }

        foreach (var (window, _) in hidden.Where(entry => !entry.Owned))
        {
            _system.ShowMinimized(window);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }

        _timers.Clear();
        _pointerTimer?.Dispose();
        _pointerTimer = null;
        _regroupTimer?.Dispose();
        _regroupTimer = null;
        foreach (var group in _groups)
        {
            group.Strip.Dispose();
        }

        _groups.Clear();
        _highlight.Hide();
    }

    private static nint FrontOf(Group group) => group.Front >= 0 ? group.FrontWindow : 0;

    private Group? GroupOf(nint window) => _groups.Find(group => group.Tabs.Contains(window));

    private Group? GroupOfFront(nint window) => window == 0 ? null : _groups.Find(group => group.Front >= 0 && group.FrontWindow == window);

    // The group whose tab is the window in front, or the window that owns it.
    private Group? FrontGroup() => GroupOfFront(_system.RootOwner(_system.ForegroundWindow));

    private void Step(int direction)
    {
        if (FrontGroup() is not { } group)
        {
            _system.PlayWarning();
            return;
        }

        var current = group.Chosen >= 0 ? group.Chosen : group.Front;
        var count = group.Tabs.Count;
        Switch(group, (((current + direction) % count) + count) % count, keys: true, close: false);
    }

    private void Move(int direction)
    {
        if (FrontGroup() is not { } group || group.Chosen >= 0)
        {
            _system.PlayWarning();
            return;
        }

        var to = group.Front + direction;
        if (to < 0 || to >= group.Tabs.Count)
        {
            _system.PlayWarning();
            return;
        }

        (group.Tabs[to], group.Tabs[group.Front]) = (group.Tabs[group.Front], group.Tabs[to]);
        group.Front = to;
        KeepAll();
        Reveal(group, RevealAfterKeys);
        ShowStrip(group);
        group.Strip.Announce(_position(to + 1, group.Tabs.Count));
    }

    // The window becomes the tab in front at the place of the window behind it, as a window dropped
    // on that window's title bar does.
    private void GroupWithBehind(nint window, bool keys)
    {
        var own = GroupOf(window);
        if (own is not null && FrontOf(own) != window)
        {
            LogNotATab(_logger);
            _system.PlayWarning();
            return;
        }

        if ((window == 0 ? TabRefusal.Gone : _system.Check(window)) is var refusal and not TabRefusal.None)
        {
            Refuse(refusal);
            return;
        }

        var behind = WindowBehind(window, own);
        if (behind == 0)
        {
            LogNothingBehind(_logger);
            _system.PlayWarning();
            return;
        }

        if (own is not null)
        {
            Detach(own, own.Front, "the user moved it to the group behind");
        }

        JoinWindow(behind, window, keys);
    }

    // The front-most shown window behind the window on its display that can be a tab, or that is a
    // group's tab in front.
    private nint WindowBehind(nint window, Group? own)
    {
        var display = _system.DisplayOf(window)?.Bounds;
        foreach (var other in _system.WindowsBehind(window))
        {
            if (own?.Tabs.Contains(other) == true || _system.DisplayOf(other)?.Bounds != display)
            {
                continue;
            }

            if (GroupOfFront(other) is not null || (GroupOf(other) is null && _system.Check(other) == TabRefusal.None))
            {
                return other;
            }
        }

        return 0;
    }

    private Group NewGroup(nint first)
    {
        var group = new Group(_newStrip());
        group.Tabs.Add(first);
        group.Strip.TabChosen += (_, index) => Switch(group, index, keys: false, close: false);
        group.Strip.TabDropped += (_, drop) => OnTabDropped(group, drop);
        group.Strip.MenuChosen += (_, choice) => OnMenu(group, choice);
        _groups.Add(group);
        RecordInsets(first);
        _system.Watch(_system.ProcessIdOf(first));
        _system.FollowMoves(_system.ProcessIdOf(first));
        return group;
    }

    private void JoinWindow(nint target, nint window, bool keys)
    {
        if (GroupOfFront(target) is { } existing)
        {
            Join(existing, window, existing.Tabs.Count, keys);
            return;
        }

        if (_system.Check(target) is var refusal and not TabRefusal.None)
        {
            Refuse(refusal);
            return;
        }

        var group = NewGroup(target);
        ShowStrip(group);
        LogMade(_logger);
        Join(group, window, 1, keys);
    }

    // keys: from a shortcut, so a strip over its window's top shows for a moment and the tab's
    // position is announced.
    private void Join(Group group, nint window, int insertion, bool keys)
    {
        if (group.Tabs.Contains(window))
        {
            return;
        }

        if (_system.Check(window) is var refusal and not TabRefusal.None)
        {
            Refuse(refusal);
            EndIfAlone(group);
            return;
        }

        var index = insertion < 0 || insertion > group.Tabs.Count ? group.Tabs.Count : insertion;
        RecordInsets(window);
        group.Tabs.Insert(index, window);
        if (index <= group.Front)
        {
            group.Front++;
        }

        if (group.Chosen >= 0 && index <= group.Chosen)
        {
            group.Chosen++;
        }

        _system.Watch(_system.ProcessIdOf(window));
        UpdateShared();
        LogJoined(_logger, group.Tabs.Count);
        Switch(group, index, keys, close: false);
    }

    private void Refuse(TabRefusal refusal)
    {
        LogRefused(_logger, refusal);
        _system.PlayWarning();
    }

    // Shows the chosen tab at the group's place and in front, and hides the old one only once the
    // new one shows and two more frames are drawn.
    private void Switch(Group group, int to, bool keys, bool close)
    {
        if (to < 0 || to >= group.Tabs.Count || group.Front < 0)
        {
            return;
        }

        var front = group.FrontWindow;
        if (to == group.Front && group.Chosen < 0)
        {
            if (close)
            {
                _system.AskToClose(front);
            }

            return;
        }

        if (_system.IsVisible(front) && (!_system.IsEnabled(front) || _system.IsFullScreen(front)))
        {
            Refuse(_system.IsEnabled(front) ? TabRefusal.FullScreen : TabRefusal.DialogOpen);
            return;
        }

        var window = group.Tabs[to];
        if (_system.IsVisible(front))
        {
            ReadPlace(group, front);
        }

        group.Chosen = to;
        group.AnnounceWhenShown = keys;
        group.CloseWhenShown = close;
        var ticket = ++group.Switches;
        if (!group.Maximized && !group.Frame.IsEmpty)
        {
            var place = RectLike(window, group.Frame);
            if (_system.Bounds(window) != place)
            {
                _system.Place(window, place);
            }
        }

        if (!_system.IsVisible(window))
        {
            _expectShown.Add(window);
        }

        _system.Show(window, ShowFor(group, window));
        ShowOwned(group, window);
        _system.Activate(window);
        if (keys)
        {
            Reveal(group, RevealAfterKeys);
        }

        ShowStrip(group);
        WaitUntilShown(group, window, ticket);
    }

    // Checks every few milliseconds, with no time limit, since an app that stopped responding shows
    // its window when it responds again; the old tab stays in front until then.
    private void WaitUntilShown(Group group, nint window, int ticket)
    {
        ITimer? timer = null;
        void Check()
        {
            if (timer is not null && !_timers.Remove(timer))
            {
                return;
            }

            timer?.Dispose();
            if (!_groups.Contains(group) || ticket != group.Switches || !_system.Exists(window))
            {
                return;
            }

            if (_system.IsVisible(window))
            {
                _system.AfterFrames(FramesBeforeHiding, () => Shown(group, window, ticket));
                return;
            }

            timer = _time.CreateTimer(_ => _post(Check), null, ShowCheck, Timeout.InfiniteTimeSpan);
            _timers.Add(timer);
        }

        Check();
    }

    private void Shown(Group group, nint window, int ticket)
    {
        var index = group.Tabs.IndexOf(window);
        if (!_groups.Contains(group) || !_system.Exists(window) || index < 0)
        {
            return;
        }

        // Another tab was chosen while this one came: it hides again.
        if (ticket != group.Switches)
        {
            if (index != group.Chosen && index != group.Front && _system.IsVisible(window))
            {
                PutAway(group, window);
            }

            return;
        }

        var old = FrontOf(group);
        group.Front = index;
        group.Chosen = -1;
        _system.Unmark(window);
        RecordInsets(window);
        if (old != 0 && old != window && group.Tabs.Contains(old))
        {
            PutAway(group, old);
            _system.StopFollowingMoves(_system.ProcessIdOf(old));
        }

        if (old != window)
        {
            _system.FollowMoves(_system.ProcessIdOf(window));
        }

        UpdateShared();
        ShowStrip(group);
        LogSwitched(_logger, index + 1, group.Tabs.Count);
        if (group.AnnounceWhenShown)
        {
            group.Strip.Announce(_position(index + 1, group.Tabs.Count));
        }

        if (group.CloseWhenShown)
        {
            group.CloseWhenShown = false;
            _system.AskToClose(window);
        }

        // An app shown on a display with another scale than it was hidden on may resize its window
        // as it moves, so its frame is set again.
        var frame = group.Frame;
        if (_hiddenDpi.Remove(window, out var hiddenDpi) && !group.Maximized && !frame.IsEmpty && _system.DisplayOf(window) is { } display && display.Dpi != hiddenDpi)
        {
            After(ref group.FrameTimer, FrameCheckDelay, () =>
            {
                if (_groups.Contains(group) && !group.Moving && FrontOf(group) == window && !_system.IsMaximized(window) && _system.VisibleBounds(window) is { } now && now != frame)
                {
                    _system.Place(window, RectLike(window, frame));
                }
            });
        }
    }

    // Hides a tab that isn't in front any more, with its shown owned windows, marked first; one
    // that can't be marked, as an app running as administrator, can't be hidden and leaves.
    private void PutAway(Group group, nint window)
    {
        var owned = _system.VisibleOwnedWindows(window);
        if (!_system.Mark(window, owned: false))
        {
            RemoveTab(group, window);
            LogLeft(_logger, "Windows refused to mark it", group.Tabs.Count);
            EndIfAlone(group);
            return;
        }

        var hidden = new List<nint>();
        foreach (var other in owned)
        {
            if (_system.Mark(other, owned: true))
            {
                _system.Hide(other);
                hidden.Add(other);
            }
        }

        group.HiddenOwned[window] = hidden;
        _hiddenDpi[window] = _system.DisplayOf(window)?.Dpi ?? 0;
        _expectHidden.Add(window);
        _system.Hide(window);
        UpdateShared();
    }

    private void ShowOwned(Group group, nint window)
    {
        if (!group.HiddenOwned.Remove(window, out var owned))
        {
            return;
        }

        foreach (var other in owned)
        {
            _system.ShowWithoutActivating(other);
            _system.Unmark(other);
        }
    }

    // After the tab in front closed, left or its app hid it, the next tab, or the one before the
    // last, shows in its place. Returns that tab, or 0 when none was left.
    private nint ShowAfterLoss(Group group, int removed, bool activate)
    {
        if (group.Tabs.Count == 0)
        {
            EndGroup(group, "its last tab left");
            return 0;
        }

        var index = Math.Min(removed, group.Tabs.Count - 1);
        var window = group.Tabs[index];
        group.Front = index;
        group.Chosen = -1;
        group.Switches++;
        if (!group.Maximized && !group.Frame.IsEmpty)
        {
            _system.Place(window, RectLike(window, group.Frame));
        }

        if (!_system.IsVisible(window))
        {
            _expectShown.Add(window);
        }

        if (activate || group.Maximized)
        {
            _system.Show(window, group.Maximized ? TabShow.Maximized : _system.IsMinimized(window) ? TabShow.Restored : TabShow.AsItWas);
            _system.Activate(window);
        }
        else
        {
            ShowQuietly(window);
        }

        ShowOwned(group, window);
        _system.Unmark(window);
        _system.FollowMoves(_system.ProcessIdOf(window));
        UpdateShared();
        if (!EndIfAlone(group))
        {
            ShowStrip(group);
        }

        return window;
    }

    private void TakeOut(Group group, int index, Point? at)
    {
        if (index < 0 || index >= group.Tabs.Count || group.Front < 0)
        {
            return;
        }

        var window = group.Tabs[index];
        var frame = group.Frame;
        var dpi = (int)(_system.DisplayOf(window)?.Dpi ?? 96);
        var offset = StripLayout.Scale(TakeOutOffset, dpi);
        var place = at is { } point
            ? new Rectangle(point.X - Math.Min(frame.Width / 2, StripLayout.Scale(100, dpi)), point.Y - StripLayout.Scale(16, dpi), frame.Width, frame.Height)
            : frame with { X = frame.X + (_rightToLeft ? -offset : offset), Y = frame.Y + offset };

        var next = Detach(group, index, "the user took it out");
        if (!frame.IsEmpty && !_system.IsMaximized(window))
        {
            _system.Place(window, RectLike(window, place));
        }

        _system.Unmark(window);
        if (!_system.IsVisible(window))
        {
            _system.Show(window, _system.IsMinimized(window) ? TabShow.Restored : TabShow.AsItWas);
        }

        ShowOwned(group, window);
        _system.Activate(window);

        // The tab shown in its place comes to the top of the order of windows once its app shows
        // it, above the window taken out even when that one is in front, so it goes behind it.
        if (next != 0)
        {
            _system.PutBehind(next, window);
        }
    }

    private void Reorder(Group group, int from, int insertion)
    {
        var to = insertion > from ? insertion - 1 : insertion;
        if (to == from || from < 0 || from >= group.Tabs.Count || to < 0 || to >= group.Tabs.Count)
        {
            return;
        }

        var front = FrontOf(group);
        var chosen = group.Chosen >= 0 ? group.Tabs[group.Chosen] : 0;
        var window = group.Tabs[from];
        group.Tabs.RemoveAt(from);
        group.Tabs.Insert(to, window);
        group.Front = group.Tabs.IndexOf(front);
        group.Chosen = chosen == 0 ? -1 : group.Tabs.IndexOf(chosen);
        KeepAll();
        ShowStrip(group);
    }

    private void OnTabDropped(Group group, TabDrop drop)
    {
        if (!_groups.Contains(group) || drop.Index < 0 || drop.Index >= group.Tabs.Count)
        {
            return;
        }

        if (group.Place is { } place && place.Contains(drop.Point))
        {
            Reorder(group, drop.Index, place.InsertionAt(place.ToStrip(drop.Point)));
            return;
        }

        if (_groups.Find(other => other != group && other.Place is { } shown && shown.Contains(drop.Point)) is { } target)
        {
            var window = group.Tabs[drop.Index];
            Detach(group, drop.Index, "the user moved it to another group");
            Join(target, window, target.Place!.InsertionAt(target.Place.ToStrip(drop.Point)), keys: false);
            return;
        }

        // A tab dropped near its strip stays; one dropped further away leaves the group.
        if (group.Place is { } own && Distance(own.Bounds, drop.Point) > own.Bounds.Height)
        {
            TakeOut(group, drop.Index, drop.Point);
        }
    }

    private void OnMenu(Group group, TabMenuChoice choice)
    {
        if (!_groups.Contains(group))
        {
            return;
        }

        switch (choice.Item)
        {
            case TabMenuItem.MoveOutOfGroup:
                TakeOut(group, choice.Index, at: null);
                break;
            case TabMenuItem.CloseWindow:
                Switch(group, choice.Index, keys: false, close: true);
                break;
            case TabMenuItem.Settings:
                _openSettings();
                break;
        }
    }

    // Takes a tab out of its group, as it leaves or moves to another group: when it was in front,
    // the next tab shows in its place, and a group of one ends. It doesn't show or hide the tab.
    // Returns the tab that shows in its place, or 0.
    private nint Detach(Group group, int index, string why)
    {
        var window = group.Tabs[index];
        var wasFront = index == group.Front;
        RemoveTab(group, window);
        LogLeft(_logger, why, group.Tabs.Count);
        if (wasFront)
        {
            return ShowAfterLoss(group, index, activate: false);
        }

        EndIfAlone(group);
        return 0;
    }

    // Takes a tab out of the group's lists and stops watching it, without showing or hiding it.
    private void RemoveTab(Group group, nint window)
    {
        var index = group.Tabs.IndexOf(window);
        if (index < 0)
        {
            return;
        }

        var front = FrontOf(group);
        group.Tabs.RemoveAt(index);
        group.HiddenOwned.Remove(window);
        group.Front = index == group.Front ? -1 : index < group.Front ? group.Front - 1 : group.Front;
        group.Chosen = index == group.Chosen ? -1 : index < group.Chosen ? group.Chosen - 1 : group.Chosen;
        if (window == front)
        {
            _system.StopFollowingMoves(_system.ProcessIdOf(window));
        }

        Unwatch(window);
        UpdateShared();
    }

    // Windows tells nothing more of the window, even of a show or hide Window Tabs just made, so
    // what Window Tabs expected of it mustn't count for its app's own show or hide later.
    private void Unwatch(nint window)
    {
        _system.Unwatch(_system.ProcessIdOf(window));
        _expectShown.Remove(window);
        _expectHidden.Remove(window);
        _apps.Remove(window);
    }

    // A group with one tab left ends, and that window stays an ordinary window.
    private bool EndIfAlone(Group group)
    {
        if (!_groups.Contains(group) || group.Tabs.Count > 1)
        {
            return false;
        }

        if (group.Tabs.Count == 1)
        {
            var last = group.Tabs[0];
            _system.Unmark(last);
            if (_system.Exists(last) && !_system.IsVisible(last))
            {
                ShowOwned(group, last);
                ShowQuietly(last);
            }
        }

        EndGroup(group, "one window was left");
        return true;
    }

    // A group that ends is forgotten, unless it ends as Window Tabs stops (keep), so the next start
    // makes it again.
    private void EndGroup(Group group, string why, bool keep = false)
    {
        if (!_groups.Remove(group))
        {
            return;
        }

        if (!keep && _kept.Forget(group.Key))
        {
            KeptChanged?.Invoke(this, EventArgs.Empty);
        }

        foreach (var tab in group.Tabs)
        {
            Unwatch(tab);
        }

        if (group.Front >= 0)
        {
            _system.StopFollowingMoves(_system.ProcessIdOf(group.FrontWindow));
        }

        group.SyncTimer?.Dispose();
        group.FrameTimer?.Dispose();
        group.Strip.Dispose();
        UpdateShared();
        WatchPointer();
        LogEnded(_logger, why);
    }

    // Gives the hidden tabs the group's place, so a tab shows at once where it belongs; not
    // during a drag, only once the window has settled.
    private void Sync(Group group)
    {
        if (!_groups.Contains(group) || group.Moving || group.Front < 0)
        {
            return;
        }

        var front = group.FrontWindow;
        if (!_system.IsVisible(front) || _system.IsMinimized(front) || _system.IsCloaked(front) || !ReadPlace(group, front))
        {
            return;
        }

        var normal = group.Maximized && _system.Placement(front) is { } placement ? Deflate(placement.NormalBounds, InsetsOf(front)) : group.Frame;
        for (var index = 0; index < group.Tabs.Count; index++)
        {
            var tab = group.Tabs[index];
            if (index != group.Front && index != group.Chosen && !_system.IsVisible(tab))
            {
                _system.SetHiddenPlacementLater(tab, RectLike(tab, normal));
            }
        }
    }

    // Reads the place of the group's tab in front: its visible frame and whether it's maximized.
    private bool ReadPlace(Group group, nint front)
    {
        if (_system.VisibleBounds(front) is not { } frame)
        {
            return false;
        }

        group.Frame = frame;
        group.Maximized = _system.IsMaximized(front);
        return true;
    }

    private void ShowStrip(Group group)
    {
        if (!_groups.Contains(group) || group.Front < 0)
        {
            return;
        }

        var front = group.FrontWindow;
        if (group.Minimized || !_system.IsVisible(front) || _system.IsMinimized(front) || _system.IsCloaked(front) || _system.IsFullScreen(front)
            || !ReadPlace(group, front) || _system.DisplayOf(front) is not { } display)
        {
            group.Place = null;
            group.Strip.Hide();
            return;
        }

        var place = StripLayout.Place(group.Frame, display.WorkArea, (int)display.Dpi, group.Tabs.Count, _rightToLeft);
        group.Place = place;
        if (place.AtTopEdge && !group.Revealed && group.DropMark < 0 && !group.Strip.IsPointerOver)
        {
            group.Strip.Hide();
            WatchPointer();
            return;
        }

        group.Strip.Show(new StripView(place, [.. group.Tabs], group.Front, group.Chosen, group.DropMark), front);
        WatchPointer();
    }

    private void Reveal(Group group, TimeSpan time)
    {
        group.Revealed = true;
        group.RevealUntil = _time.GetTimestamp() + (long)(time.TotalSeconds * _time.TimestampFrequency);
        WatchPointer();
    }

    // A strip over its window's top shows while the pointer rests at that top; the pointer is
    // read only while such a group is in front or shown.
    private void WatchPointer()
    {
        var needed = !_disposed && _groups.Exists(group => group.Place is { AtTopEdge: true } && (group.Revealed || FrontOf(group) == _system.RootOwner(_system.ForegroundWindow)));
        if (needed && _pointerTimer is null)
        {
            _pointerTimer = _time.CreateTimer(_ => _post(CheckPointer), null, PointerCheck, PointerCheck);
        }
        else if (!needed && _pointerTimer is not null)
        {
            _pointerTimer.Dispose();
            _pointerTimer = null;
        }
    }

    private void CheckPointer()
    {
        if (_pointerTimer is null)
        {
            return;
        }

        var now = _time.GetTimestamp();
        var pointer = _system.PointerPosition;
        foreach (var group in _groups.ToList())
        {
            if (group.Place is not { AtTopEdge: true } place || pointer is not { } point)
            {
                continue;
            }

            var dpi = (int)(_system.DisplayOf(FrontOf(group))?.Dpi ?? 96);
            var inBand = point.X >= group.Frame.Left && point.X < group.Frame.Right && point.Y >= group.Frame.Top && point.Y < group.Frame.Top + StripLayout.Scale(RevealBand, dpi);
            if (group.Revealed)
            {
                if (inBand || place.Contains(point) || group.Strip.IsPointerOver)
                {
                    group.RevealUntil = Math.Max(group.RevealUntil, now + Ticks(RevealLinger));
                }
                else if (now >= group.RevealUntil)
                {
                    group.Revealed = false;
                    ShowStrip(group);
                }
            }
            else if (inBand && FrontOf(group) == _system.RootOwner(_system.ForegroundWindow))
            {
                group.RestSince ??= now;
                if (now - group.RestSince.Value >= Ticks(RestTime))
                {
                    group.RestSince = null;
                    Reveal(group, RevealLinger);
                    ShowStrip(group);
                }
            }
            else
            {
                group.RestSince = null;
            }
        }

        WatchPointer();
    }

    private long Ticks(TimeSpan time) => (long)(time.TotalSeconds * _time.TimestampFrequency);

    private void UpdateDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        // A resize changes the window's size; only a move can drop it.
        if (_system.Bounds(drag.Window) is not { } bounds || bounds.Size != drag.Size)
        {
            EndDrag();
            return;
        }

        Group? stripTarget = null;
        var insertion = -1;
        nint titleTarget = 0;
        var overRefused = TabRefusal.None;
        if (_system.PointerPosition is { } point)
        {
            // A window that can't be a tab, dropped on a strip, is refused there.
            foreach (var group in _groups)
            {
                if (group.Place is { } place && FrontOf(group) != drag.Window && place.Contains(point))
                {
                    if (drag.Refusal == TabRefusal.None)
                    {
                        stripTarget = group;
                        insertion = place.InsertionAt(place.ToStrip(point));
                    }
                    else
                    {
                        overRefused = drag.Refusal;
                    }

                    break;
                }
            }

            if (stripTarget is null && overRefused == TabRefusal.None && _settings.GroupByDropping && _system.IsControlHeld)
            {
                var under = _system.WindowAt(point, drag.Window);
                if (under != 0 && DropBand(under) is { } band && band.Contains(point))
                {
                    var refusal = drag.Refusal != TabRefusal.None || GroupOfFront(under) is not null ? drag.Refusal : _system.Check(under);
                    if (refusal == TabRefusal.None)
                    {
                        titleTarget = under;
                    }
                    else
                    {
                        overRefused = refusal;
                    }
                }
            }
        }

        SetDropTargets(stripTarget, insertion, titleTarget);
        drag.StripTarget = stripTarget;
        drag.Insertion = insertion;
        drag.TitleTarget = titleTarget;
        drag.OverRefused = overRefused;
    }

    private Rectangle? DropBand(nint window)
    {
        if (_system.VisibleBounds(window) is not { } frame)
        {
            return null;
        }

        var dpi = (int)(_system.DisplayOf(window)?.Dpi ?? 96);
        return frame with { Height = Math.Min(frame.Height, StripLayout.Scale(StripLayout.DropBand, dpi)) };
    }

    private void SetDropTargets(Group? stripTarget, int insertion, nint titleTarget)
    {
        foreach (var group in _groups)
        {
            var mark = group == stripTarget ? insertion : -1;
            if (mark == group.DropMark)
            {
                continue;
            }

            var wasTarget = group.DropMark >= 0;
            group.DropMark = mark;
            if (wasTarget != (mark >= 0))
            {
                group.Strip.SetDropTarget(mark >= 0);
            }

            ShowStrip(group);
        }

        if (titleTarget != 0 && DropBand(titleTarget) is { } band)
        {
            _highlight.Show(band);
        }
        else
        {
            _highlight.Hide();
        }
    }

    private Drag? EndDrag()
    {
        if (_drag is not { } drag)
        {
            return null;
        }

        _drag = null;
        _system.StopFollowingMoves(drag.ProcessId);
        SetDropTargets(null, -1, 0);
        return drag;
    }

    private void UpdateShared()
    {
        _shared?.Set(_groups.SelectMany(group => group.Tabs));
        var hidden = new List<(nint, bool)>();
        foreach (var group in _groups)
        {
            foreach (var (tab, owned) in group.HiddenOwned)
            {
                hidden.Add((tab, false));
                hidden.AddRange(owned.Select(window => (window, true)));
            }
        }

        Volatile.Write(ref _hidden, [.. hidden]);
        KeepAll();
    }

    // Keeps each group as it is now, while Keep groups after a restart is on.
    private void KeepAll()
    {
        if (!_settings.KeepGroups)
        {
            return;
        }

        var changed = false;
        foreach (var group in _groups.Where(group => group.Front >= 0))
        {
            changed |= _kept.Keep(group.Key, [.. group.Tabs.Select(AppOf)], group.Front);
        }

        if (changed)
        {
            KeptChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private KeptTab? AppOf(nint window)
    {
        if (!_apps.TryGetValue(window, out var app))
        {
            app = _system.AppOf(window) is { } found ? KeptTab.Of(found) : null;
            _apps[window] = app;
        }

        return app;
    }

    // Looks for the windows of the groups that wait to be made again among the windows in no group
    // that can be tabs, minimized ones too, and stops once none waits or the time is up.
    private void Regroup()
    {
        if (!IsRegrouping)
        {
            return;
        }

        var final = _time.GetTimestamp() >= _regroupUntil;
        var candidates = new List<(nint, AppOfWindow)>();
        foreach (var window in _system.TaskbarWindows())
        {
            if (GroupOf(window) is null && _system.AppOf(window) is { } app && _kept.Wants(app)
                && _system.Check(window) is TabRefusal.None or TabRefusal.Minimized)
            {
                candidates.Add((window, app));
            }
        }

        foreach (var found in _kept.Match(candidates, final))
        {
            MakeAgain(found);
        }

        if (final || _kept.Waiting == 0)
        {
            StopRegrouping();
        }
    }

    private void StopRegrouping()
    {
        if (_regroupTimer is null)
        {
            return;
        }

        _regroupTimer.Dispose();
        _regroupTimer = null;
        CancelTimer(ref _regroupSoon);
        var notMade = _kept.Waiting;
        LogRegroupingEnded(_logger, notMade);
        _kept.StopWaiting();
        RegroupingEnded?.Invoke(this, EventArgs.Empty);
    }

    // Makes a kept group again: the window of its tab in front stays as it is, minimized too, and
    // the others hide behind it, in its place. The window the user is in stays in front, so it
    // doesn't hide as they work in it.
    private void MakeAgain(KeptRegroup found)
    {
        var inFront = found.Windows.ToList().IndexOf(_system.RootOwner(_system.ForegroundWindow));
        var frontIndex = inFront >= 0 ? inFront : found.Front;
        var front = found.Windows[frontIndex];
        var group = NewGroup(front);
        group.Key = found.Key;
        group.Tabs.Clear();
        group.Tabs.AddRange(found.Windows);
        group.Front = frontIndex;
        if (_system.IsMinimized(front))
        {
            group.Minimized = true;
            if (_system.Placement(front) is { } placement)
            {
                group.Frame = Deflate(placement.NormalBounds, InsetsOf(front));
                group.Maximized = placement.RestoresMaximized;
            }
        }
        else
        {
            ReadPlace(group, front);
        }

        LogMadeAgain(_logger, found.Windows.Count);
        foreach (var window in found.Windows.Where(window => window != front))
        {
            if (!_groups.Contains(group))
            {
                return;
            }

            RecordInsets(window);
            _system.Watch(_system.ProcessIdOf(window));
            PutAway(group, window);
        }

        if (!_groups.Contains(group))
        {
            return;
        }

        if (!group.Minimized)
        {
            Sync(group);
        }
        else if (!group.Frame.IsEmpty)
        {
            foreach (var tab in group.Tabs.Where(tab => tab != front))
            {
                _system.SetHiddenPlacementLater(tab, RectLike(tab, group.Frame));
            }
        }

        UpdateShared();
        ShowStrip(group);
    }

    // A minimized window, as a tab made again from a minimized window, comes back restored.
    private TabShow ShowFor(Group group, nint window) =>
        group.Maximized ? TabShow.Maximized
        : _system.IsMaximized(window) || _system.IsMinimized(window) ? TabShow.Restored
        : TabShow.AsItWas;

    private void ShowQuietly(nint window)
    {
        if (_system.IsMinimized(window))
        {
            _system.RestoreWithoutActivating(window);
        }
        else
        {
            _system.ShowWithoutActivating(window);
        }
    }

    private void RecordInsets(nint window)
    {
        if (_system.IsVisible(window) && !_system.IsMaximized(window) && !_system.IsMinimized(window)
            && _system.Bounds(window) is { } bounds && _system.VisibleBounds(window) is { } visible)
        {
            _insets[window] = new Insets(visible.Left - bounds.Left, visible.Top - bounds.Top, bounds.Right - visible.Right, bounds.Bottom - visible.Bottom);
        }
    }

    // Windows draws invisible borders for resizing, 7 effective pixels left, right and below on
    // most windows, until the window's own were read while it showed.
    private Insets InsetsOf(nint window)
    {
        if (_insets.TryGetValue(window, out var insets))
        {
            return insets;
        }

        var border = StripLayout.Scale(7, (int)(_system.DisplayOf(window)?.Dpi ?? 96));
        return new Insets(border, 0, border, border);
    }

    // The window rectangle that gives the window the visible frame.
    private Rectangle RectLike(nint window, Rectangle frame)
    {
        var insets = InsetsOf(window);
        return Rectangle.FromLTRB(frame.Left - insets.Left, frame.Top - insets.Top, frame.Right + insets.Right, frame.Bottom + insets.Bottom);
    }

    private static Rectangle Deflate(Rectangle bounds, Insets insets) =>
        Rectangle.FromLTRB(bounds.Left + insets.Left, bounds.Top + insets.Top, bounds.Right - insets.Right, bounds.Bottom - insets.Bottom);

    private static int Distance(Rectangle area, Point point)
    {
        var dx = Math.Max(Math.Max(area.Left - point.X, point.X - (area.Right - 1)), 0);
        var dy = Math.Max(Math.Max(area.Top - point.Y, point.Y - (area.Bottom - 1)), 0);
        return Math.Max(dx, dy);
    }

    private void CancelTimer(ref ITimer? slot)
    {
        if (slot is not null)
        {
            _timers.Remove(slot);
            slot.Dispose();
            slot = null;
        }
    }

    // Runs once, after the delay; starting it again first stops the previous one.
    private void After(ref ITimer? slot, TimeSpan delay, Action action)
    {
        CancelTimer(ref slot);

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
        slot = timer;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs made a group.")]
    private static partial void LogMade(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs added a window to a group, which has {Count} tabs now.")]
    private static partial void LogJoined(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs showed tab {Index} of {Count}.")]
    private static partial void LogSwitched(ILogger logger, int index, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs: an app showed its hidden tab, which comes to the front.")]
    private static partial void LogAppShowed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs: a window left its group, which has {Count} tabs now: {Why}.")]
    private static partial void LogLeft(ILogger logger, string why, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs ended a group: {Why}.")]
    private static partial void LogEnded(ILogger logger, string why);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs refused a window: {Refusal}.")]
    private static partial void LogRefused(ILogger logger, TabRefusal refusal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs found no window behind that can be a tab to group with.")]
    private static partial void LogNothingBehind(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs: the window isn't a group's tab in front.")]
    private static partial void LogNotATab(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs read {Count} kept groups to make again.")]
    private static partial void LogKeptRead(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs made a kept group again, with {Count} tabs.")]
    private static partial void LogMadeAgain(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs stopped looking for kept groups' windows; {Count} groups weren't made again.")]
    private static partial void LogRegroupingEnded(ILogger logger, int count);

    private readonly record struct Insets(int Left, int Top, int Right, int Bottom);

    private sealed class Group(ITabStrip strip)
    {
        public ITimer? SyncTimer;
        public ITimer? FrameTimer;

        public ITabStrip Strip { get; } = strip;

        /// <summary>What the group is kept by for Keep groups after a restart.</summary>
        public object Key { get; set; } = new();

        public List<nint> Tabs { get; } = [];

        public nint FrontWindow => Tabs[Front];

        /// <summary>The tab in front, or -1 while none is, as just after it closed.</summary>
        public int Front { get; set; }

        /// <summary>The tab the user chose, which isn't in front until its app shows it, or -1.</summary>
        public int Chosen { get; set; } = -1;

        /// <summary>Counts the switches, so a switch overtaken by another one leaves its tab hidden.</summary>
        public int Switches { get; set; }

        public bool AnnounceWhenShown { get; set; }

        public bool CloseWhenShown { get; set; }

        /// <summary>The user moves or resizes the window in front.</summary>
        public bool Moving { get; set; }

        public bool Minimized { get; set; }

        /// <summary>The visible frame of the tab in front, as last read.</summary>
        public Rectangle Frame { get; set; }

        public bool Maximized { get; set; }

        /// <summary>Where the strip is, or would be when it's over the window's top; null while it can't show.</summary>
        public StripPlace? Place { get; set; }

        /// <summary>Where a tab dragged over the strip would go, or -1.</summary>
        public int DropMark { get; set; } = -1;

        public bool Revealed { get; set; }

        public long RevealUntil { get; set; }

        public long? RestSince { get; set; }

        /// <summary>The owned windows hidden with each hidden tab.</summary>
        public Dictionary<nint, List<nint>> HiddenOwned { get; } = [];
    }

    private sealed class Drag(nint window, TabRefusal refusal, uint processId, Size size)
    {
        public nint Window { get; } = window;

        public TabRefusal Refusal { get; } = refusal;

        public uint ProcessId { get; } = processId;

        public Size Size { get; } = size;

        public Group? StripTarget { get; set; }

        public int Insertion { get; set; } = -1;

        public nint TitleTarget { get; set; }

        // Why the window under the pointer, or the dragged one, can't group; None when it can or there's none.
        public TabRefusal OverRefused { get; set; }
    }
}
