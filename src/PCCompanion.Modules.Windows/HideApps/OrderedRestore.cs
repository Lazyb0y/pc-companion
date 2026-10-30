using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Brings minimized windows back in their order, as "How" under "Bringing windows back" in
/// docs/features/windows/hide-apps.md says: restores them in place without activating them, then
/// puts each right behind the one before, one at a time. Each app moves its window when it
/// handles its messages, and a window put behind one that isn't in place yet lands in the wrong
/// place, so each move is waited for: checked on each order change Windows reports and by a
/// timer, for up to <see cref="MoveTimeout"/>. Use it on the feature's thread.
/// </summary>
public sealed partial class OrderedRestore : IDisposable
{
    /// <summary>How long a move is waited for before the next window is moved.</summary>
    public static readonly TimeSpan MoveTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>How often a move is checked when Windows reports no change in the order.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(15);

    // How many of a window's owned windows, such as palettes, can sit between it and the window before.
    private const int MaximumOwnedWindows = 50;

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

    /// <summary>Whether windows are still being put in order.</summary>
    public bool IsRunning => _run is not null;

    /// <summary>
    /// Brings back <paramref name="windows"/>, front to back, right behind <paramref name="behind"/>;
    /// with 0, the first window stays where it comes back and the others go behind it. Windows
    /// still being put in order from an earlier call are done first.
    /// </summary>
    public void Start(IReadOnlyList<nint> windows, nint behind)
    {
        ArgumentNullException.ThrowIfNull(windows);

        // Each app restores its window before it handles the move asked after it.
        foreach (var window in windows.Where(_system.IsMinimized))
        {
            _ = _system.RestoreWithoutActivating(window);
        }

        _runs.Enqueue(new Run(new Queue<nint>(windows), behind, _time.GetTimestamp()));
        if (_run is null)
        {
            _system.WatchOrder(true);
            Advance();
        }
    }

    /// <summary>Windows reported a change in the order of windows.</summary>
    public void OnOrderChanged() => CheckMove();

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void CheckMove()
    {
        if (_run is not { Moving: not 0 } run)
        {
            return;
        }

        if (IsRightBehind(run.Previous, run.Moving))
        {
            run.Previous = run.Moving;
        }
        else if (_time.GetElapsedTime(run.MoveStarted) >= MoveTimeout)
        {
            // The next window goes behind the last one that's in place.
            run.TimedOut++;
        }
        else
        {
            return;
        }

        run.Moving = 0;
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
            }

            var run = _run;
            if (!run.Left.TryDequeue(out var window))
            {
                var took = _time.GetElapsedTime(run.Started);
                LogRestored(_logger, run.Count, run.Skipped, run.TimedOut, took.TotalMilliseconds);
                _run = null;
                continue;
            }

            // An app that stopped responding brings its window back when it responds, behind the others.
            if (!_system.Exists(window) || _system.IsNotResponding(window))
            {
                run.Skipped++;
                continue;
            }

            run.Count++;
            if (run.Previous == 0)
            {
                run.Previous = window;
                continue;
            }

            if (!_system.PutBehind(window, run.Previous))
            {
                run.Skipped++;
                continue;
            }

            if (IsRightBehind(run.Previous, window))
            {
                run.Previous = window;
                continue;
            }

            run.Moving = window;
            run.MoveStarted = _time.GetTimestamp();
            _timer ??= _time.CreateTimer(_ => _post(CheckMove), null, CheckInterval, CheckInterval);
            return;
        }
    }

    // Whether the window is right behind the one before. Windows keeps a window's owned windows,
    // such as palettes, in front of it, so they can sit between the two.
    private bool IsRightBehind(nint previous, nint window)
    {
        var next = _system.NextVisibleBehind(previous);
        for (var step = 0; step < MaximumOwnedWindows && next != 0 && next != window && _system.RootOwner(next) == window; step++)
        {
            next = _system.NextVisibleBehind(next);
        }

        return next == window;
    }

    private sealed class Run(Queue<nint> left, nint previous, long started)
    {
        public Queue<nint> Left { get; } = left;

        /// <summary>The window the next one goes behind, or 0 before the first.</summary>
        public nint Previous { get; set; } = previous;

        public long Started { get; } = started;

        /// <summary>The window being moved, or 0.</summary>
        public nint Moving { get; set; }

        public long MoveStarted { get; set; }

        public int Count { get; set; }

        public int Skipped { get; set; }

        public int TimedOut { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps brought back {Count} windows in order in {Milliseconds:F1} ms; {Skipped} skipped, {TimedOut} not moved in time.")]
    private static partial void LogRestored(ILogger logger, int count, int skipped, int timedOut, double milliseconds);
}
