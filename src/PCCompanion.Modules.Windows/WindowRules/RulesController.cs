using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Decides what Window Rules does to each window, as "When rules apply" in
/// docs/features/windows/window-rules.md says. It hears of windows from the feature's hooks and
/// acts through <see cref="IRulesSystem"/>. Use it on the feature's thread only; its timers come
/// back there through <c>post</c>.
/// </summary>
public sealed partial class RulesController : IDisposable
{
    /// <summary>How long after a window is placed its app's moves are watched.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

    /// <summary>How long after a window's last move its place is checked.</summary>
    public static readonly TimeSpan SettleCheck = TimeSpan.FromMilliseconds(150);

    /// <summary>How long after a window is placed its place is checked, also without a move.</summary>
    public static readonly TimeSpan FirstCheck = TimeSpan.FromMilliseconds(300);

    /// <summary>How long after a window is ready a late title can still match a rule.</summary>
    public static readonly TimeSpan TitleWait = TimeSpan.FromSeconds(10);

    /// <summary>How many more times a window is placed when its app moves it back.</summary>
    public const int MaximumReapplies = 3;

    // How far, in pixels, a frame may be from the rule's before it counts as moved.
    private const int Tolerance = 2;

    private readonly IRulesSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;

    // Windows that aren't new: decided, or open when the rules started.
    private readonly HashSet<nint> _seen = [];

    // Windows that showed cloaked, which are ready once they're uncloaked.
    private readonly HashSet<nint> _cloaked = [];

    // Windows that rules matched, or that wait for their title, until they close.
    private readonly Dictionary<nint, RuledWindow> _windows = [];

    /// <summary>How long after the displays last changed the windows are placed again.</summary>
    public static readonly TimeSpan DisplayWait = TimeSpan.FromSeconds(2);

    // How long after Explorer restarts the buttons it added again are removed.
    private static readonly TimeSpan TaskbarSettle = TimeSpan.FromSeconds(2);

    private readonly HeldActions _held;
    private ITimer? _taskbarCreated;
    private ITimer? _displaysChanged;
    private bool _started;

    public RulesController(IRulesSystem system, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
        _held = new HeldActions(system);
        _system.WindowMoved += (_, window) => OnMoved(window);
    }

    /// <summary>The rules in use.</summary>
    public RuleSet Rules { get; private set; } = RuleSet.Empty;

    /// <summary>The windows rules matched or that wait for their title, for tests.</summary>
    public IReadOnlyCollection<nint> RuledWindows => _windows.Keys;

    /// <summary>The windows that rules hold on top, translucent or off the taskbar, for tests.</summary>
    public IReadOnlyCollection<nint> HeldWindows => _held.Windows;

    /// <summary>Whether <paramref name="window"/>'s app moves are still watched, for tests.</summary>
    public bool IsSettling(nint window) => _windows.TryGetValue(window, out var ruled) && ruled.Settling;

    /// <summary>
    /// Uses new rules. The first time, and whenever the rules start again after there were none,
    /// the windows open now count as not new, so they're never placed without Apply now, but they
    /// get the actions their rules hold. Windows already matched are matched again by their title
    /// now, without being placed.
    /// </summary>
    public void Apply(WindowRulesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var rules = new RuleSet(settings.Rules);
        Rules = rules;
        if (rules.LeftOut > 0)
        {
            LogRulesLeftOut(_logger, rules.LeftOut, WindowRulesSettings.MaximumRules);
        }

        foreach (var (index, problem) in rules.Problems)
        {
            LogRuleNotUsed(_logger, index + 1, problem);
        }

        if (!rules.HasUsable)
        {
            Stop();
            return;
        }

        if (!_started)
        {
            _started = true;
            foreach (var window in _system.TaskbarWindows())
            {
                _seen.Add(window);
                Adopt(window);
            }
        }

        foreach (var ruled in _windows.Values.ToList())
        {
            Rematch(ruled);
        }
    }

    /// <summary>Whether the controller watches windows: some rule can be used.</summary>
    public bool IsWatching => _started;

    /// <summary>A window showed: a new main window is ready, or waits until it's uncloaked.</summary>
    public void OnShown(nint window)
    {
        if (!_started || _seen.Contains(window) || _windows.ContainsKey(window) || _cloaked.Contains(window))
        {
            return;
        }

        if (!IsActionable(window))
        {
            return;
        }

        if (_system.IsCloaked(window))
        {
            _cloaked.Add(window);
            return;
        }

        Ready(window);
    }

    /// <summary>A window was uncloaked: one that showed cloaked, such as a Store app's frame, is ready.</summary>
    public void OnUncloaked(nint window)
    {
        if (_cloaked.Remove(window) && IsActionable(window))
        {
            Ready(window);
        }
    }

    /// <summary>A window's title changed: within its first 10 seconds, rules waiting for it are checked again.</summary>
    public void OnTitleChanged(nint window)
    {
        if (!_windows.TryGetValue(window, out var ruled) || ruled.Waiting.Count == 0)
        {
            return;
        }

        if (Elapsed(ruled.ReadyAt) > TitleWait)
        {
            StopWaiting(ruled);
            return;
        }

        if (_system.Facts(window) is not { } facts)
        {
            return;
        }

        var match = Rules.Match(facts, ruled.Waiting);
        LogTimedOut(match.TimedOut);
        if (match.Matched.Count == 0)
        {
            return;
        }

        ruled.Waiting = [.. ruled.Waiting.Except(match.Matched)];
        ruled.Matched = [.. ruled.Matched.Concat(match.Matched).Order()];
        var before = ruled.Outcome;
        ruled.Outcome = Rules.Combine(ruled.Matched);
        LogMatchedLate(_logger, match.Matched.Count);
        Act(ruled, before);
    }

    /// <summary>A window closed: it's forgotten, as Windows reuses handles.</summary>
    public void OnDestroyed(nint window)
    {
        _held.Forget(window);
        _seen.Remove(window);
        _cloaked.Remove(window);
        if (_windows.Remove(window, out var ruled))
        {
            EndSettling(ruled);
        }
    }

    /// <summary>The user started moving or sizing a window: rules don't place it again.</summary>
    public void OnMoveSizeStart(nint window)
    {
        if (_windows.TryGetValue(window, out var ruled))
        {
            ruled.UserMoved = true;
            EndSettling(ruled);
        }
    }

    /// <summary>
    /// A window started to minimize: its place isn't watched any more, and one held off the
    /// taskbar gets its button back while it's minimized.
    /// </summary>
    public void OnMinimizeStart(nint window)
    {
        if (_windows.TryGetValue(window, out var ruled))
        {
            EndSettling(ruled);
        }

        _held.OnMinimizeStart(window);
    }

    /// <summary>A window was restored: one held off the taskbar loses its button again.</summary>
    public void OnMinimizeEnd(nint window) => _held.OnMinimizeEnd(window);

    /// <summary>A window came to the front: one held on top whose app took it off is put back.</summary>
    public void OnForeground(nint window) => _held.OnForeground(window);

    /// <summary>
    /// Explorer restarted and adds every window's button again: 2 seconds later, the windows held
    /// off the taskbar lose theirs again.
    /// </summary>
    public void OnTaskbarCreated()
    {
        _taskbarCreated?.Dispose();
        _taskbarCreated = _time.CreateTimer(_ => _post(_held.OnTaskbarCreated), null, TaskbarSettle, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The displays changed, as when one is connected or disconnected or a display profile
    /// switched: 2 seconds after the last change, the open windows of rules with Apply again when
    /// the displays change are placed again, except snapped and full-screen windows.
    /// </summary>
    public void OnDisplaysChanged()
    {
        _displaysChanged?.Dispose();
        _displaysChanged = _time.CreateTimer(_ => _post(PlaceAgain), null, DisplayWait, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Puts back what rules hold at once, from any thread, as the host crashes. Returns how many windows.</summary>
    public int ReleaseNow() => _held.ReleaseAll();

    /// <summary>
    /// Puts back the windows left marked by a PC Companion that ended without putting them back:
    /// off the top, their own opacity and their taskbar buttons. Returns how many.
    /// </summary>
    public static int Recover(IRulesSystem system) => HeldActions.Recover(system);

    /// <summary>
    /// Applies <paramref name="rule"/>, as the page shows it, to the open windows it matches,
    /// placing included, except snapped and full-screen windows. Returns how many windows it
    /// applied to; none when the rule can't be used.
    /// </summary>
    public int ApplyNow(WindowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var matcher = new RuleMatcher(rule);
        if (!matcher.IsUsable)
        {
            return 0;
        }

        var outcome = RuleOutcome.Combine([rule], [0]);
        var applied = 0;
        foreach (var window in _system.TaskbarWindows())
        {
            if (_system.Check(window) != RuleRefusal.None || _system.Facts(window) is not { } facts
                || !matcher.MatchesApp(facts) || matcher.MatchesTitle(facts.Title) != TitleResult.Matches)
            {
                continue;
            }

            _seen.Add(window);
            var ruled = Ruled(window);
            ruled.UserMoved = false;
            if (outcome.Places && !_system.IsArranged(window) && !_system.IsFullScreen(window))
            {
                Place(ruled, outcome);
            }

            _held.Apply(window, outcome.OnTop, OpacityOf(outcome), outcome.HideFromTaskbar);
            applied++;
        }

        LogAppliedNow(_logger, applied);
        return applied;
    }

    public void Dispose() => Stop();

    // With no rules to use, nothing is watched or held, and the windows open when rules start again count as not new.
    private void Stop()
    {
        _held.ReleaseAll();
        _taskbarCreated?.Dispose();
        _taskbarCreated = null;
        _displaysChanged?.Dispose();
        _displaysChanged = null;
        foreach (var ruled in _windows.Values)
        {
            EndSettling(ruled);
        }

        _windows.Clear();
        _seen.Clear();
        _cloaked.Clear();
        _started = false;
    }

    private bool IsActionable(nint window)
    {
        var refusal = _system.Check(window);
        if (refusal == RuleRefusal.RunsAsAdministrator && _system.Facts(window) is { } facts && Rules.AnyApp(facts))
        {
            LogRunsAsAdministrator(_logger);
        }

        return refusal == RuleRefusal.None;
    }

    private void Ready(nint window)
    {
        if (_system.Facts(window) is not { } facts)
        {
            return;
        }

        _seen.Add(window);
        var match = Rules.Match(facts);
        LogTimedOut(match.TimedOut);
        if (match.Matched.Count == 0 && match.Waiting.Count == 0)
        {
            return;
        }

        var ruled = new RuledWindow(window, _time.GetTimestamp())
        {
            Matched = match.Matched,
            Waiting = match.Waiting,
            Outcome = Rules.Combine(match.Matched),
        };
        _windows[window] = ruled;
        if (match.Matched.Count > 0)
        {
            Act(ruled, before: null);
        }
    }

    // Applies what the rules now give a window, after it was ready or when a late title matched.
    // A window is placed again only if its place changed and the user hasn't moved it.
    private void Act(RuledWindow ruled, RuleOutcome? before)
    {
        var outcome = ruled.Outcome;
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var rules = string.Join(", ", outcome.Rules.Select(index => index + 1));
            var actions = Describe(outcome);
            LogApplied(_logger, rules, actions);
        }

        // A window that joins a group takes the group's place, so the rule's other actions don't apply.
        if (before is null && !ruled.WasOpen && outcome.TabOf is { } tabOf && TabTarget(ruled.Window, tabOf) is var target and not 0
            && _system.GroupWith(ruled.Window, target))
        {
            ruled.Grouped = true;
            LogGrouped(_logger);
            return;
        }

        var placementChanged = before is null
            || before.Display != outcome.Display || before.Place != outcome.Place || before.Open != outcome.Open;
        if (outcome.Places && placementChanged && !ruled.UserMoved && !ruled.WasOpen)
        {
            Place(ruled, outcome);
        }

        Hold(ruled);
    }

    private void PlaceAgain()
    {
        var placed = 0;
        foreach (var ruled in _windows.Values.ToList())
        {
            var outcome = ruled.Outcome;
            if (ruled.Grouped || !outcome.ApplyOnDisplayChange || !outcome.Places || !_system.Exists(ruled.Window)
                || _system.IsArranged(ruled.Window) || _system.IsFullScreen(ruled.Window))
            {
                continue;
            }

            ruled.UserMoved = false;
            Place(ruled, outcome);
            placed++;
        }

        if (placed > 0)
        {
            LogPlacedAgain(_logger, placed);
        }
    }

    // A window open as the rules start: it gets what its rules hold, without being placed.
    private void Adopt(nint window)
    {
        if (_system.Check(window) != RuleRefusal.None || _system.Facts(window) is not { } facts)
        {
            return;
        }

        var match = Rules.Match(facts);
        if (match.Matched.Count == 0)
        {
            return;
        }

        var ruled = Ruled(window);
        ruled.Matched = match.Matched;
        ruled.Outcome = Rules.Combine(match.Matched);
        Hold(ruled);
    }

    private void Hold(RuledWindow ruled)
    {
        if (!ruled.Grouped)
        {
            _held.Apply(ruled.Window, ruled.Outcome.OnTop, OpacityOf(ruled.Outcome), ruled.Outcome.HideFromTaskbar);
        }
    }

    // The front-most other window of the app, shown on the desktop that's showing.
    private nint TabTarget(nint window, ListedApp app)
    {
        foreach (var other in _system.TaskbarWindows())
        {
            if (other != window && !_system.IsCloaked(other) && !_system.IsMinimized(other) && _system.Facts(other) is { } facts && app.Matches(facts.App))
            {
                return other;
            }
        }

        return 0;
    }

    // A fixed opacity; one from the wheel comes with remembered places.
    private static int? OpacityOf(RuleOutcome outcome) =>
        outcome.Opacity is { KnownWhich: OpacityChoice.Fixed } opacity ? Math.Clamp(opacity.Percent, RuleOpacity.Lowest, RuleOpacity.Highest) : null;

    // The rules changed: what a window gets is worked out again from its title now, without placing it.
    private void Rematch(RuledWindow ruled)
    {
        if (_system.Facts(ruled.Window) is not { } facts)
        {
            return;
        }

        var match = Rules.Match(facts);
        ruled.Matched = match.Matched;
        ruled.Waiting = !ruled.WasOpen && Elapsed(ruled.ReadyAt) <= TitleWait ? match.Waiting : [];
        ruled.Outcome = Rules.Combine(match.Matched);
        Hold(ruled);
        if (ruled.Matched.Count == 0 && ruled.Waiting.Count == 0)
        {
            EndSettling(ruled);
            _windows.Remove(ruled.Window);
        }
    }

    private void StopWaiting(RuledWindow ruled)
    {
        ruled.Waiting = [];
        if (ruled.Matched.Count == 0)
        {
            EndSettling(ruled);
            _windows.Remove(ruled.Window);
        }
    }

    private RuledWindow Ruled(nint window)
    {
        if (!_windows.TryGetValue(window, out var ruled))
        {
            ruled = new RuledWindow(window, _time.GetTimestamp()) { WasOpen = true };
            _windows[window] = ruled;
        }

        return ruled;
    }

    private void Place(RuledWindow ruled, RuleOutcome outcome)
    {
        var window = ruled.Window;
        if (_system.Placement(window) is not { } placement || _system.WorkAreaOf(window) is not { } from)
        {
            return;
        }

        var dpi = _system.Dpi(window);
        var to = outcome.Display?.KnownWhich switch
        {
            DisplayChoice.Main => _system.MainWorkArea,
            DisplayChoice.UnderPointer => _system.PointerWorkArea,
            DisplayChoice.Chosen => _system.WorkAreaOf(outcome.Display),
            _ => null,
        } ?? from;

        // A maximized or minimized window is placed by its normal rectangle, without its borders.
        var visible = placement.State == WindowState.Normal ? _system.VisibleBounds(window) : RulePlacement.WithoutUsualBorders(placement.NormalBounds, dpi);
        if (visible is not { } current)
        {
            return;
        }

        var frame = RulePlacement.Frame(current, from, to, outcome.Place, movesDisplay: to != from);
        var normal = frame is { } placed ? RulePlacement.WithUsualBorders(placed, dpi) : placement.NormalBounds;
        switch (outcome.Open)
        {
            case OpenChoice.Maximized:
                _system.SetPlacement(window, normal, WindowState.Maximized);
                return;
            case OpenChoice.Minimized:
                _system.SetPlacement(window, normal, WindowState.Minimized);
                return;
            case OpenChoice.Normal when placement.State != WindowState.Normal:
                _system.SetPlacement(window, normal, WindowState.Normal);
                return;
        }

        if (frame is not { } target)
        {
            return;
        }

        if (placement.State != WindowState.Normal)
        {
            // It stays maximized or minimized, and restoring it goes to the rule's place.
            _system.SetPlacement(window, normal, placement.State);
            return;
        }

        ruled.Target = target;
        ruled.Applies = 0;
        PlaceNormal(ruled);
        StartSettling(ruled);
    }

    // The frame with the borders the window has now, which differ between apps and scales.
    private void PlaceNormal(RuledWindow ruled)
    {
        if (ruled.Target is not { } target || _system.Bounds(ruled.Window) is not { } bounds || _system.VisibleBounds(ruled.Window) is not { } visible)
        {
            return;
        }

        _system.Place(ruled.Window, RulePlacement.WithBorders(target, bounds, visible));
        ruled.Applies++;
    }

    private void StartSettling(RuledWindow ruled)
    {
        EndSettling(ruled);
        ruled.Settling = true;
        ruled.SettleStart = _time.GetTimestamp();
        _system.Follow(ruled.Window);
        ruled.SettleEnd = _time.CreateTimer(_ => _post(() => EndSettling(ruled)), null, SettleTime, Timeout.InfiniteTimeSpan);
        ArmCheck(ruled, FirstCheck);
    }

    private void EndSettling(RuledWindow ruled)
    {
        ruled.Check?.Dispose();
        ruled.Check = null;
        ruled.SettleEnd?.Dispose();
        ruled.SettleEnd = null;
        if (ruled.Settling)
        {
            ruled.Settling = false;
            _system.Unfollow(ruled.Window);
        }
    }

    private void OnMoved(nint window)
    {
        if (_windows.TryGetValue(window, out var ruled) && ruled.Settling && !ruled.UserMoved)
        {
            ArmCheck(ruled, SettleCheck);
        }
    }

    private void ArmCheck(RuledWindow ruled, TimeSpan after)
    {
        ruled.Check?.Dispose();
        ruled.Check = _time.CreateTimer(_ => _post(() => CheckPlace(ruled)), null, after, Timeout.InfiniteTimeSpan);
    }

    // The app moved the window back while it was watched: it's placed again, a few times at most.
    private void CheckPlace(RuledWindow ruled)
    {
        if (!ruled.Settling || ruled.UserMoved || ruled.Target is not { } target || _system.VisibleBounds(ruled.Window) is not { } visible)
        {
            return;
        }

        if (_system.Placement(ruled.Window) is { State: not WindowState.Normal })
        {
            // The app maximized or minimized it: that's left as the app made it.
            EndSettling(ruled);
            return;
        }

        if (RulePlacement.Distance(visible, target) <= Tolerance || Elapsed(ruled.SettleStart) > SettleTime)
        {
            return;
        }

        if (ruled.Applies > MaximumReapplies)
        {
            LogKeptMovingBack(_logger);
            EndSettling(ruled);
            return;
        }

        LogMovedBack(_logger, ruled.Applies);
        PlaceNormal(ruled);
    }

    private TimeSpan Elapsed(long since) => _time.GetElapsedTime(since);

    private void LogTimedOut(IReadOnlyList<int> timedOut)
    {
        foreach (var index in timedOut)
        {
            LogPatternTimedOut(_logger, index + 1);
        }
    }

    // Which actions, never the app or the title.
    private static string Describe(RuleOutcome outcome)
    {
        var actions = new List<string>();
        if (outcome.Display is not null)
        {
            actions.Add("display");
        }

        if (outcome.Place is not null)
        {
            actions.Add("place");
        }

        if (outcome.Open is { } open)
        {
            actions.Add($"open {open}");
        }

        if (outcome.OnTop)
        {
            actions.Add("on top");
        }

        if (outcome.Opacity is not null)
        {
            actions.Add("opacity");
        }

        if (outcome.HideFromTaskbar)
        {
            actions.Add("off the taskbar");
        }

        if (outcome.TabOf is not null)
        {
            actions.Add("as a tab");
        }

        return string.Join(", ", actions);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules applied rules {Rules} to a window: {Actions}.")]
    private static partial void LogApplied(ILogger logger, string rules, string actions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The displays changed, so Window Rules placed {Count} windows again.")]
    private static partial void LogPlacedAgain(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules asked Window Tabs to open a window as a tab.")]
    private static partial void LogGrouped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A window's title matched {Count} more rules after it showed.")]
    private static partial void LogMatchedLate(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules applied a rule now to {Count} open windows.")]
    private static partial void LogAppliedNow(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A window's app moved it back, so Window Rules placed it again (time {Applies}).")]
    private static partial void LogMovedBack(ILogger logger, int applies);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A window's app kept moving it back, so Window Rules left it there.")]
    private static partial void LogKeptMovingBack(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules left a window of an app running as administrator alone, since PC Companion doesn't run as administrator.")]
    private static partial void LogRunsAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules uses the first {Maximum} rules; {Count} more are left out.")]
    private static partial void LogRulesLeftOut(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window rule {Rule} can't be used: {Problem}.")]
    private static partial void LogRuleNotUsed(ILogger logger, int rule, RuleProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window rule {Rule}'s title pattern took too long, so it didn't match a window.")]
    private static partial void LogPatternTimedOut(ILogger logger, int rule);

    // What the rules did and do to one window.
    private sealed class RuledWindow(nint window, long readyAt)
    {
        public nint Window { get; } = window;

        public long ReadyAt { get; } = readyAt;

        public IReadOnlyList<int> Matched { get; set; } = [];

        public IReadOnlyList<int> Waiting { get; set; } = [];

        public RuleOutcome Outcome { get; set; } = RuleOutcome.Combine([], []);

        public Rectangle? Target { get; set; }

        public int Applies { get; set; }

        public bool UserMoved { get; set; }

        // Joined a group through Window Tabs, which keeps every tab alike.
        public bool Grouped { get; set; }

        // Open before the rules started, or applied to with Apply now: never placed by a late title.
        public bool WasOpen { get; init; }

        public bool Settling { get; set; }

        public long SettleStart { get; set; }

        public ITimer? SettleEnd { get; set; }

        public ITimer? Check { get; set; }
    }
}
