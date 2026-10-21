using System;
using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Testing;

/// <summary>
/// A fake that a feature's own thread uses while a test waits for it with <see cref="WaitUntil"/>:
/// it records what happened, in order, and the test's condition is looked at again each time the
/// fake records something or says it changed.
/// </summary>
/// <typeparam name="TSelf">The fake itself, which the test's conditions get.</typeparam>
public abstract class RecordingFake<TSelf>
    where TSelf : RecordingFake<TSelf>
{
    private readonly List<string> _events = [];

    /// <summary>What happened, in order.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (Gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Guards the fake's state; a change made under it, other than an event, calls <see cref="Changed"/>.</summary>
    protected object Gate { get; } = new();

    /// <summary>Waits until <paramref name="condition"/> holds; returns false after 10 seconds, or <paramref name="timeout"/>.</summary>
    public bool WaitUntil(Func<TSelf, bool> condition, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = Environment.TickCount64 + (long)(timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds;
        lock (Gate)
        {
            while (!condition((TSelf)this))
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    return false;
                }

                Monitor.Wait(Gate, TimeSpan.FromMilliseconds(Math.Min(left, 100)));
            }

            return true;
        }
    }

    /// <summary>Adds <paramref name="happened"/> to <see cref="Events"/> and wakes <see cref="WaitUntil"/>.</summary>
    protected void Record(string happened)
    {
        lock (Gate)
        {
            _events.Add(happened);
            Monitor.PulseAll(Gate);
        }
    }

    /// <summary>Wakes <see cref="WaitUntil"/> after a change that isn't an event.</summary>
    protected void Changed()
    {
        lock (Gate)
        {
            Monitor.PulseAll(Gate);
        }
    }
}
