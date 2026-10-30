using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Brings minimized windows back in their order, as "How" under "Bringing windows back" in
/// docs/features/windows/hide-apps.md says: restores them in place without activating them, waits
/// until they're back and <see cref="SettleTime"/> more, as restoring moves some windows, such as
/// maximized ones, after they're shown, then puts each right behind the one before, one at a time.
/// Each app moves its window when it handles its messages, and a window put behind one that isn't
/// in place yet lands in the wrong place, so each move is waited for: checked on each order change
/// Windows reports and by a timer, for up to <see cref="MoveTimeout"/>. Use it on the feature's
/// thread.
/// </summary>
public sealed partial class OrderedRestore : IDisposable
{
    /// <summary>How long a move is waited for before the next window is moved.</summary>
    public static readonly TimeSpan MoveTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>How often a move, or the windows coming back, is checked when Windows reports no change.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>How long the windows are waited for to come back before they're put in order anyway.</summary>
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long after the windows are back they're put in order, as restoring still moves some.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);

    // How many windows that Alt + Tab doesn't show, such as palettes and Windows' own helper
    // windows, can sit between a window and the window before.
    private const int MaximumHelperWindows = 200;

    private readonly IHideAppsSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly Queue<Run> _runs = new();
    private Run? _run;
    private ITimer? _timer;

    /// <param name="post">
    /// Runs an action later on the feature's thread, inside its error boundary, as the timer needs.
    /// </param>
    public OrderedRestore(IHideAppsSystem system, TimeProvider time, Action<Action> post, ILogger logger)
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

    /// <summary>Whether windows are still being brought back.</summary>
    public bool IsRunning => _run is not null;

    /// <summary>
    /// Brings back <paramref name="windows"/>, front to back, right behind <paramref name="behind"/>;
    /// with 0, the first window stays where it comes back and the others go behind it. Windows
    /// still being brought back from an earlier call are done first.
    /// </summary>
    public void Start(IReadOnlyList<nint> windows, nint behind)
    {
        ArgumentNullException.ThrowIfNull(windows);

        _runs.Enqueue(new Run([.. windows], behind, _time.GetTimestamp()));
        if (_run is null)
        {
            _system.WatchOrder(true);
            Advance();
        }
    }

    /// <summary>Windows reported a change in the order of windows.</summary>
    public void OnOrderChanged() => Check();

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        if (_run is not { } run)
        {
            return;
        }

        if (run.Moving != 0)
        {
            if (IsRightBehind(run.Previous, run.Moving))
            {
                run.Previous = run.Moving;
            }
            else if (_time.GetElapsedTime(run.MoveStarted) >= MoveTimeout)
            {
                // The next window goes behind the last one that's in place.
                run.TimedOut++;
                var there = _system.NextVisibleBehind(run.Previous);
                var movingClass = _system.ClassName(run.Moving);
                var thereClass = _system.ClassName(there);
                LogNotMoved(_logger, movingClass, thereClass);
            }
            else
            {
                return;
            }

            run.Moving = 0;
        }

        Advance();
    }

    private void Advance()
    {
        while (true)
        {
            if (_run is null)
            {
                if (!_runs.TryDequeue(out var next))
                {
                    _timer?.Dispose();
                    _timer = null;
                    _system.WatchOrder(false);
                    return;
                }

                _run = next;
                foreach (var minimized in next.Windows.Where(each => _system.Exists(each) && _system.IsMinimized(each)))
                {
                    _ = _system.RestoreWithoutActivating(minimized);
                }

                next.Begin(Phase.Restoring, _time.GetTimestamp());
            }

            var run = _run;
            if (run.Phase == Phase.Restoring)
            {
                if (!AreBack(run) && _time.GetElapsedTime(run.PhaseStarted) < RestoreTimeout)
                {
                    EnsureTimer();
                    return;
                }

                run.Begin(Phase.Settling, _time.GetTimestamp());
            }

            if (run.Phase == Phase.Settling)
            {
                if (_time.GetElapsedTime(run.PhaseStarted) < SettleTime)
                {
                    EnsureTimer();
                    return;
                }

                run.Begin(Phase.Ordering, _time.GetTimestamp());
            }

            if (run.Moving != 0)
            {
                return;
            }

            if (!run.Left.TryDequeue(out var current))
            {
                var took = _time.GetElapsedTime(run.Started);
                LogRestored(_logger, run.Count, run.Skipped, run.TimedOut, took.TotalMilliseconds);
                _run = null;
                continue;
            }

            // An app that stopped responding brings its window back when it responds, behind the others.
            if (!_system.Exists(current) || _system.IsNotResponding(current))
            {
                run.Skipped++;
                continue;
            }

            run.Count++;
            if (run.Previous == 0 || IsRightBehind(run.Previous, current))
            {
                run.Previous = current;
                continue;
            }

            if (!_system.PutBehind(current, run.Previous))
            {
                run.Skipped++;
                continue;
            }

            if (IsRightBehind(run.Previous, current))
            {
                run.Previous = current;
                continue;
            }

            run.Moving = current;
            run.MoveStarted = _time.GetTimestamp();
            EnsureTimer();
            return;
        }
    }

    private void EnsureTimer() => _timer ??= _time.CreateTimer(_ => _post(Check), null, CheckInterval, CheckInterval);

    // Whether every window that can come back is back; one whose app stopped responding comes back when it responds.
    private bool AreBack(Run run) =>
        run.Windows.All(window => !_system.Exists(window) || !_system.IsMinimized(window) || _system.IsNotResponding(window));

    // Whether the window is the first window Alt + Tab shows behind the one before. Owned windows,
    // such as palettes, sit in front of their owner, and Windows' own helper windows, such as a
    // Store app's core window, can sit anywhere.
    private bool IsRightBehind(nint previous, nint window)
    {
        var next = _system.NextVisibleBehind(previous);
        for (var step = 0; step < MaximumHelperWindows && next != 0 && next != window && !_system.IsAppWindow(next); step++)
        {
            next = _system.NextVisibleBehind(next);
        }

        return next == window;
    }

    private enum Phase
    {
        /// <summary>Waiting for the restored windows to come back.</summary>
        Restoring,

        /// <summary>Waiting for restoring to stop moving them.</summary>
        Settling,

        /// <summary>Putting them in order.</summary>
        Ordering,
    }

    private sealed class Run(List<nint> windows, nint behind, long started)
    {
        public List<nint> Windows { get; } = windows;

        public Queue<nint> Left { get; } = new(windows);

        /// <summary>The window the next one goes behind, or 0 before the first.</summary>
        public nint Previous { get; set; } = behind;

        public long Started { get; } = started;

        public Phase Phase { get; private set; }

        public long PhaseStarted { get; private set; } = started;

        /// <summary>The window being moved, or 0.</summary>
        public nint Moving { get; set; }

        public long MoveStarted { get; set; }

        public int Count { get; set; }

        public int Skipped { get; set; }

        public int TimedOut { get; set; }

        public void Begin(Phase phase, long now)
        {
            Phase = phase;
            PhaseStarted = now;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A window of class {Moving} didn't move behind the window before in time; right behind that window is one of class {There}.")]
    private static partial void LogNotMoved(ILogger logger, string moving, string there);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps brought back {Count} windows in order in {Milliseconds:F1} ms; {Skipped} skipped, {TimedOut} not moved in time.")]
    private static partial void LogRestored(ILogger logger, int count, int skipped, int timedOut, double milliseconds);
}
