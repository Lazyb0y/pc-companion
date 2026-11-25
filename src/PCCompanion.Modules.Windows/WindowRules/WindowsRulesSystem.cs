using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Window Rules' view of Windows, on the feature's thread. Calls that wait for another program run
/// on the thread pool, one at a time for each window.
/// </summary>
public sealed class WindowsRulesSystem : IRulesSystem, IDisposable
{
    /// <summary>The mark's name, which PC Companion keeps from one version to the next.</summary>
    public const string MarkName = "PCCompanion.WindowRules";

    private static readonly Lazy<WindowMark> Marks = new(() => new WindowMark(MarkName));

    // Within the host's wait for the feature's thread as it exits.
    private static readonly TimeSpan PlacementWait = TimeSpan.FromSeconds(1.5);

    // Minimize to Tray's mark, on the windows in its tray.
    private static readonly Lazy<WindowMark> TrayMarks = new(() => new WindowMark(WindowsTrayWindowsSystem.MarkName));

    private readonly Action<Action> _post;
    private readonly Action<Action> _guard;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly WindowRequests _requests;
    private readonly AppOfWindowReader _apps = new(new WindowsAppIdentityReader());
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private readonly Dictionary<uint, (int Count, WinEventHook Moves)> _followed = [];
    private readonly Dictionary<nint, uint> _followedWindows = [];
    private readonly Dictionary<nint, Task> _placements = [];
    private IReadOnlyList<ConnectedRuleDisplay> _displays = [];

    /// <param name="post">Runs an action back on the feature's thread.</param>
    /// <param name="guard">Runs a report inside the feature thread's error boundary.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="requests">Where windows go to Window Tabs for Open as a tab of.</param>
    public WindowsRulesSystem(Action<Action> post, Action<Action> guard, DropDownWindows dropDown, WindowTabsWindows tabs, WindowRequests requests)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(requests);

        _post = post;
        _guard = guard;
        _dropDown = dropDown;
        _tabs = tabs;
        _requests = requests;
    }

    public event EventHandler<nint>? WindowMoved;

    public Rectangle? MainWorkArea => Displays.Main()?.WorkArea;

    public Rectangle? PointerWorkArea => InputState.PointerPosition is { } pointer ? Displays.At(pointer)?.WorkArea : null;

    public RuleRefusal Check(nint window)
    {
        if (!TopLevelWindows.Exists(window))
        {
            return RuleRefusal.Gone;
        }

        var processId = TopLevelWindows.ProcessIdOf(window);
        if (processId == _ownProcess)
        {
            return RuleRefusal.OtherFeature;
        }

        var className = TopLevelWindows.ClassName(window);
        if (!TopLevelWindows.MayHaveTaskbarButton(window) || TopLevelWindows.Describe(window).IsDesktop
            || TopLevelWindows.PlaceOf(window) is not { HasFrame: true }
            || className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow")
        {
            return RuleRefusal.NotMain;
        }

        // The stand-in Windows shows for a window that stopped responding.
        if (className == "Ghost" || TopLevelWindows.IsNotResponding(window))
        {
            return RuleRefusal.NotResponding;
        }

        if (_dropDown.Contains(window) || _tabs.Contains(window) || TrayMarks.Value.IsOn(window))
        {
            return RuleRefusal.OtherFeature;
        }

        // Windows refuses a program a window of a process with a higher integrity level; one whose
        // level can't be read is left alone too.
        return Elevation.CompareIntegrityLevel(processId) is null or > 0 ? RuleRefusal.RunsAsAdministrator : RuleRefusal.None;
    }

    public WindowFacts? Facts(nint window)
    {
        if (!TopLevelWindows.Exists(window))
        {
            return null;
        }

        var app = _apps.Of(TopLevelWindows.Describe(window));
        return new WindowFacts(app, TopLevelWindows.Title(window), TopLevelWindows.ClassName(window));
    }

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsCloaked(nint window) => TopLevelWindows.IsCloaked(window);

    public bool IsArranged(nint window) => TopLevelWindows.IsArranged(window);

    public bool IsFullScreen(nint window) => FullScreenCheck.Covers(window);

    public Rectangle? Bounds(nint window) => TopLevelWindows.Bounds(window);

    public Rectangle? VisibleBounds(nint window) => TopLevelWindows.VisibleBounds(window);

    public WindowPlacement? Placement(nint window) => TopLevelWindows.Placement(window);

    public uint Dpi(nint window) => TopLevelWindows.Dpi(window);

    public Rectangle? WorkAreaOf(nint window) => Displays.Of(window)?.WorkArea;

    public Rectangle? WorkAreaOf(RuleDisplay display) => ConnectedDisplays.WorkAreaOf(display, _displays);

    public IReadOnlyList<nint> TaskbarWindows() =>
        [.. TopLevelWindows.List().Where(window => !window.IsDesktop && TopLevelWindows.MayHaveTaskbarButton(window.Handle)).Select(window => window.Handle)];

    public bool Place(nint window, Rectangle bounds) => TopLevelWindows.Place(window, bounds);

    public void SetPlacement(nint window, Rectangle normalBounds, WindowState show) => Queue(window, () => TopLevelWindows.SetPlacement(window, normalBounds, show));

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public bool IsTopmost(nint window) => TopLevelWindows.IsTopmost(window);

    public bool SetTopmost(nint window, bool topmost) => TopLevelWindows.SetTopmost(window, topmost);

    public void RemoveTaskbarButton(nint window) => Queue(window, () => TaskbarButtons.Remove(window));

    public void AddTaskbarButton(nint window) => Queue(window, () => TaskbarButtons.Add(window));

    public bool IsLayered(nint window) => LayeredStyle.IsLayered(window);

    public LayeredAttributes? LayeredAttributes(nint window) => LayeredStyle.Read(window);

    // Giving a window the layered style waits for its app; its opacity is set once it has the style.
    public void SetOpacity(nint window, LayeredAttributes attributes, bool addLayered) => Queue(window, () =>
    {
        if (!addLayered || LayeredStyle.SetLayered(window, true))
        {
            LayeredStyle.Set(window, attributes);
        }
    });

    public void PutBackOpacity(nint window, bool removeLayered, LayeredAttributes? own) => Queue(window, () =>
    {
        LayeredStyle.Set(window, own ?? new LayeredAttributes(0, 255, LayeredEffects.Alpha));
        if (removeLayered)
        {
            LayeredStyle.SetLayered(window, false);
        }
    });

    public bool GroupWith(nint window, nint target) => _requests.GroupWith(window, target);

    public bool Mark(nint window, nint value) => Marks.Value.Set(window, value);

    public nint MarkOf(nint window) => Marks.Value.Value(window);

    public void Unmark(nint window) => Marks.Value.Remove(window);

    public IReadOnlyList<nint> Marked() => Marks.Value.FindMarked();

    // Calls that wait for an app or for Explorer run on the thread pool, one at a time for each
    // window, after the one before it, so they happen in the order they were asked for.
    private void Queue(nint window, Action work)
    {
        foreach (var done in _placements.Where(placement => placement.Value.IsCompleted).Select(placement => placement.Key).ToList())
        {
            _placements.Remove(done);
        }

        var before = _placements.GetValueOrDefault(window) ?? Task.CompletedTask;
        _placements[window] = before.ContinueWith(_ => work(), TaskScheduler.Default);
    }

    public void Follow(nint window)
    {
        var processId = TopLevelWindows.ProcessIdOf(window);
        if (processId == 0 || _followedWindows.ContainsKey(window))
        {
            return;
        }

        _followedWindows[window] = processId;
        if (_followed.TryGetValue(processId, out var followed))
        {
            _followed[processId] = (followed.Count + 1, followed.Moves);
            return;
        }

        var hook = new WinEventHook(WinEventHook.LocationChangeEvent, WinEventHook.LocationChangeEvent, processId);
        hook.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow && _followedWindows.ContainsKey(e.Window))
            {
                WindowMoved?.Invoke(this, e.Window);
            }
        });
        _followed[processId] = (1, hook);
    }

    public void Unfollow(nint window)
    {
        if (!_followedWindows.Remove(window, out var processId) || !_followed.TryGetValue(processId, out var followed))
        {
            return;
        }

        if (followed.Count > 1)
        {
            _followed[processId] = (followed.Count - 1, followed.Moves);
            return;
        }

        followed.Moves.Dispose();
        _followed.Remove(processId);
    }

    /// <summary>
    /// Reads the connected displays again on the thread pool, as the feature starts and after the
    /// displays change, and keeps them on the feature's thread, then runs <paramref name="then"/> there.
    /// </summary>
    public void RefreshDisplays(Action? then = null) =>
        Task.Run(ConnectedDisplays.Read).ContinueWith(
            read => _post(() =>
            {
                if (read.IsCompletedSuccessfully)
                {
                    _displays = read.Result;
                }

                then?.Invoke();
            }),
            TaskScheduler.Default);

    // Placements that wait for an app that stopped responding are left after a while.
    public void Dispose()
    {
        foreach (var (_, moves) in _followed.Values)
        {
            moves.Dispose();
        }

        _followed.Clear();
        _followedWindows.Clear();
        Task.WaitAll([.. _placements.Values], PlacementWait);
        _placements.Clear();
    }
}
