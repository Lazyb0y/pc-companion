using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Infrastructure.Tests;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>, which fires the timers that
/// are due on the test's thread, periodic ones once per period.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward, firing each due timer when the clock reaches its time.</summary>
    public void Advance(TimeSpan time)
    {
        var end = Now + time;
        while (NextDue(end) is { } due)
        {
            Now = due;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue();
            }
        }

        Now = end;
    }

    private DateTimeOffset? NextDue(DateTimeOffset end)
    {
        DateTimeOffset? next = null;
        foreach (var timer in _timers)
        {
            if (timer.Due is { } due && due <= end && (next is null || due < next))
            {
                next = due;
            }
        }

        return next;
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
            _period = period;
            return true;
        }

        public void FireIfDue()
        {
            if (Due <= clock.Now)
            {
                Due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : Due + _period;
                callback(state);
            }
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
