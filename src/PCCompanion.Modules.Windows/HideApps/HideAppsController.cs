using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Decides which windows Hide app and Hide others minimize, keeps the list of the windows they
/// minimized, and brings them back. See docs/features/windows/hide-apps.md. Use it on the
/// feature's thread, except <see cref="Count"/>.
/// </summary>
public sealed partial class HideAppsController : IDisposable
{
    // Windows' stand-in for a window whose app stopped responding, in Desktop Window Manager's process.
    private const string GhostClass = "Ghost";

    // How often, and how many times, windows asked to come back are checked for having come back, to give the front back.
    private static readonly TimeSpan BackCheck = TimeSpan.FromMilliseconds(50);
    private const int BackChecks = 20;

    private readonly IHideAppsSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly OrderedRestore _restore;
    private readonly HideAppsMinimized _minimized;

    // Front to back, as they were when minimized; a later press's windows come first.
    private readonly List<PutAway> _list = [];
    private readonly HashSet<uint> _watched = [];
    private readonly List<ITimer> _timers = [];
    private HideAppsSettings _settings = new();
    private AppOfWindow? _keptApp;
    private bool _watchesRestores;
    private int _count;

    /// <param name="post">
    /// Runs an action later on the feature's thread, inside its error boundary, as timers need.
    /// </param>
    /// <param name="minimized">The list's windows, kept for Minimize to Tray, which leaves them on the taskbar.</param>
    public HideAppsController(IHideAppsSystem system, TimeProvider time, Action<Action> post, ILogger logger, HideAppsMinimized minimized)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(minimized);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
        _minimized = minimized;
        _restore = new OrderedRestore(system, time, post, logger);
    }

    /// <summary>How many windows Hide Apps minimized and can bring back, for the PC Companion icon's menu. It can be read on any thread.</summary>
    public int Count => Volatile.Read(ref _count);

    public void Apply(HideAppsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
    }

    /// <summary>Minimizes every window of the app in front, the window in front last.</summary>
    public PressResult HideApp()
    {
        var started = _time.GetTimestamp();
        if (FrontApp() is not var (windows, apps, root, app))
        {
            return Finish(HideAction.HideApp, PressOutcome.NoAppInFront, 0, [], started);
        }

        var stays = new List<StayReason>();
        var targets = windows
            .Where(window => IsCandidate(window) && apps.Of(window).IsSameAppAs(app))
            .Where(window => MayGo(window.Handle, stays))
            .ToList();

        // The window in front goes last, so Windows brings the window behind the app's windows to the front.
        var order = targets.Where(window => window.Handle != root).ToList();
        if (targets.FirstOrDefault(window => window.Handle == root) is { } rootTarget)
        {
            order.Add(rootTarget);
        }

        var added = new List<PutAway>();
        foreach (var window in order)
        {
            if (Put(window, apps.Of(window), HideAction.HideApp, inFront: window.Handle == root, stays) is { } put)
            {
                added.Add(put);
            }
        }

        // Kept in the order they had, front to back.
        _list.InsertRange(0, added.OrderBy(put => targets.FindIndex(window => window.Handle == put.Window)));
        Changed();
        return Finish(HideAction.HideApp, added.Count > 0 ? PressOutcome.Hid : PressOutcome.NothingToDo, added.Count, stays, started);
    }

    /// <summary>
    /// Minimizes every window of every other app; pressed again while the app it kept is in front,
    /// brings them back.
    /// </summary>
    public PressResult HideOthers()
    {
        var started = _time.GetTimestamp();
        if (FrontApp() is not var (windows, apps, root, app))
        {
            return Finish(HideAction.HideOthers, PressOutcome.NoAppInFront, 0, [], started);
        }

        if (_keptApp is { } kept && kept.IsSameAppAs(app) && _list.Any(put => put.Way == HideAction.HideOthers))
        {
            var back = _list.Where(put => put.Way == HideAction.HideOthers).ToList();
            var behind = windows.LastOrDefault(window => IsCandidate(window) && apps.Of(window).IsSameAppAs(app))?.Handle ?? root;
            BringBack(back, behind, giveFrontTo: _system.ForegroundWindow);
            return Finish(HideAction.HideOthers, PressOutcome.BroughtBack, back.Count, [], started);
        }

        var display = _settings.FrontDisplayOnly ? _system.DisplayOf(root) : null;
        var stays = new List<StayReason>();
        var added = new List<PutAway>();
        foreach (var window in windows.Where(window => IsCandidate(window) && !apps.Of(window).IsSameAppAs(app)))
        {
            if (window.IsTopmost)
            {
                stays.Add(StayReason.Topmost);
            }
            else if (display is not null && _system.DisplayOf(window.Handle) != display)
            {
                stays.Add(StayReason.OtherDisplay);
            }
            else if (MayGo(window.Handle, stays) && Put(window, apps.Of(window), HideAction.HideOthers, inFront: false, stays) is { } put)
            {
                added.Add(put);
            }
        }

        _list.InsertRange(0, added);
        _keptApp = app;
        Changed();
        return Finish(HideAction.HideOthers, added.Count > 0 ? PressOutcome.Hid : PressOutcome.NothingToDo, added.Count, stays, started);
    }

    /// <summary>Brings back every window that Hide Apps minimized, behind the app in front.</summary>
    public PressResult ShowAll()
    {
        var started = _time.GetTimestamp();
        if (_list.Count == 0)
        {
            return Finish(HideAction.ShowAll, PressOutcome.NothingToDo, 0, [], started);
        }

        var all = _list.ToList();
        var front = FrontApp();
        var behind = front is var (windows, apps, root, app)
            ? windows.LastOrDefault(window => IsCandidate(window) && apps.Of(window).IsSameAppAs(app))?.Handle ?? root
            : 0;
        BringBack(all, behind, giveFrontTo: front is not null ? _system.ForegroundWindow : 0);
        return Finish(HideAction.ShowAll, PressOutcome.BroughtBack, all.Count, [], started);
    }

    /// <summary>
    /// A window started to come back from minimized, by any means. When Hide Apps minimized it,
    /// it leaves the list, and its app's other windows in the list come back behind it.
    /// </summary>
    public void OnRestored(nint window)
    {
        var index = _list.FindIndex(put => put.Window == window);
        if (index < 0)
        {
            return;
        }

        var came = _list[index];
        _list.RemoveAt(index);
        var others = _settings.BringBackWholeApp ? _list.Where(put => put.App.IsSameAppAs(came.App)).ToList() : [];
        if (others.Count > 0)
        {
            BringBack(others, window, giveFrontTo: window);
            LogWholeApp(_logger, others.Count);
        }

        Changed();
    }

    /// <summary>A window of a watched process was destroyed.</summary>
    public void OnDestroyed(nint window)
    {
        if (_list.RemoveAll(put => put.Window == window) > 0)
        {
            Changed();
        }
    }

    /// <summary>Windows reported a change in the order of windows.</summary>
    public void OnOrderChanged() => _restore.OnOrderChanged();

    /// <summary>Stops watching; the windows stay as they are.</summary>
    public void Dispose()
    {
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }

        _timers.Clear();
        _restore.Dispose();
    }

    // The windows Alt + Tab shows, as App Windows switches between them, that aren't minimized.
    private static bool IsShown(TopLevelWindow window) => WindowSwitcher.IsSwitchable(window, includeMinimized: false);

    private bool IsCandidate(TopLevelWindow window) => IsShown(window) && _system.ClassName(window.Handle) != GhostClass;

    // The visible windows, the app reader for them, and the window in front's main window and app; null with no app in front.
    private (IReadOnlyList<TopLevelWindow> Windows, AppOfWindowReader Apps, nint Root, AppOfWindow App)? FrontApp()
    {
        var front = _system.ForegroundWindow;
        if (front == 0)
        {
            return null;
        }

        var root = _system.RootOwner(front);
        var windows = _system.ListWindows();
        if (windows.FirstOrDefault(window => window.Handle == root) is not { } rootWindow || !IsCandidate(rootWindow))
        {
            return null;
        }

        var apps = new AppOfWindowReader(_system);
        return (windows, apps, root, apps.Of(rootWindow));
    }

    private bool MayGo(nint window, List<StayReason> stays)
    {
        if (!_system.HasMinimizeButton(window))
        {
            stays.Add(StayReason.NoMinimizeButton);
            return false;
        }

        // Minimizing can cancel the dialog, as it does in Windows Forms apps.
        if (!_system.IsEnabled(window))
        {
            stays.Add(StayReason.DialogOpen);
            return false;
        }

        return true;
    }

    // Minimizes a window, or asks its app to when Windows refuses, as for an app running as administrator.
    private PutAway? Put(TopLevelWindow window, AppOfWindow app, HideAction way, bool inFront, List<StayReason> stays)
    {
        // Before Windows reports the minimize, so Minimize to Tray leaves the window on the taskbar.
        _minimized.Add(window.Handle);
        if (inFront ? _system.Minimize(window.Handle) : _system.MinimizeWithoutActivating(window.Handle))
        {
            return new PutAway(window.Handle, window.ProcessId, app, way, Asked: false);
        }

        if (_system.AskToMinimize(window.Handle))
        {
            return new PutAway(window.Handle, window.ProcessId, app, way, Asked: true);
        }

        stays.Add(StayReason.Refused);
        return null;
    }

    // Brings back windows from the list, front to back, right behind a window.
    private void BringBack(List<PutAway> windows, nint behind, nint giveFrontTo)
    {
        // Out of the list first, so the restores they cause aren't taken for windows coming back on their own.
        _list.RemoveAll(windows.Contains);
        Changed();

        var direct = windows.Where(put => !put.Asked).Select(put => put.Window).ToList();
        if (direct.Count > 0)
        {
            _restore.Start(direct, behind);
        }

        // Windows of apps running as administrator can only be asked; each takes the front as it comes back.
        var asked = windows.Where(put => put.Asked).Select(put => put.Window).ToList();
        for (var at = asked.Count - 1; at >= 0; at--)
        {
            _ = _system.AskToRestore(asked[at]);
        }

        if (asked.Count > 0 && giveFrontTo != 0)
        {
            GiveFrontBackOnceBack(asked, giveFrontTo);
        }
    }

    private void GiveFrontBackOnceBack(List<nint> asked, nint front)
    {
        var checks = 0;
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Contains(timer))
            {
                return;
            }

            checks++;
            var back = asked.All(window => !_system.Exists(window) || !_system.IsMinimized(window));
            if (back || checks >= BackChecks)
            {
                _timers.Remove(timer);
                timer.Dispose();
                if (_system.Exists(front) && !_system.Activate(front))
                {
                    LogFrontNotBack(_logger);
                }
            }
        }), null, BackCheck, BackCheck);
        _timers.Add(timer);
    }

    // Keeps the watched processes, the restore hook and the menu's count in step with the list.
    private void Changed()
    {
        var processes = _list.Select(put => put.ProcessId).ToHashSet();
        foreach (var processId in processes.Where(processId => !_watched.Contains(processId)).ToList())
        {
            _system.Watch(processId);
            _watched.Add(processId);
        }

        foreach (var processId in _watched.Where(processId => !processes.Contains(processId)).ToList())
        {
            _system.Unwatch(processId);
            _watched.Remove(processId);
        }

        if (_watchesRestores != _list.Count > 0)
        {
            _watchesRestores = _list.Count > 0;
            _system.WatchRestores(_watchesRestores);
        }

        _minimized.Set(_list.Select(put => put.Window));
        Volatile.Write(ref _count, _list.Count);
    }

    // Plays the warning sound when the press did nothing or left a window that should have gone, and logs it.
    private PressResult Finish(HideAction action, PressOutcome outcome, int count, List<StayReason> stays, long started)
    {
        var result = new PressResult(outcome, count, stays.Count(IsWarned));
        if (outcome is PressOutcome.NoAppInFront or PressOutcome.NothingToDo || result.Warned > 0)
        {
            _system.PlayWarning();
        }

        var reasons = string.Join(", ", stays.GroupBy(reason => reason).Select(group => $"{group.Key} {group.Count()}"));
        var took = _time.GetElapsedTime(started);
        LogPress(_logger, action, outcome, count, stays.Count, reasons, took.TotalMilliseconds, _list.Count);
        return result;
    }

    private static bool IsWarned(StayReason reason) => reason is StayReason.DialogOpen or StayReason.Refused;

    /// <param name="Asked">Minimized by a request to its app, as for an app running as administrator, so it's brought back the same way.</param>
    private sealed record PutAway(nint Window, uint ProcessId, AppOfWindow App, HideAction Way, bool Asked);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps: {Action}, {Outcome}, {Count} windows; {Stayed} stayed ({Reasons}); {Milliseconds:F1} ms; {Listed} windows are in the list.")]
    private static partial void LogPress(ILogger logger, HideAction action, PressOutcome outcome, int count, int stayed, string reasons, double milliseconds, int listed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps brought back {Count} more windows of an app whose window came back.")]
    private static partial void LogWholeApp(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't give the front back after windows of an app running as administrator came back.")]
    private static partial void LogFrontNotBack(ILogger logger);
}
