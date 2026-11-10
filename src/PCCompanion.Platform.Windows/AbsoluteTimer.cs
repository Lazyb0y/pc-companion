using System;
using System.ComponentModel;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Calls back on the thread pool at a moment, as Windows keeps time in UTC: as soon as the PC wakes
/// when the moment passed during sleep, at once when the clock is set past it, and not before it
/// when the clock is set back. No thread waits in between: a waitable timer signals, and a thread
/// pool wait serves it. Set it again for each moment, until disposed.
/// </summary>
public sealed class AbsoluteTimer : IDisposable
{
    private const uint TimerAllAccess = 0x1F0003;

    private readonly Action _elapsed;
    private readonly SafeWaitHandle _timer;
    private readonly TimerHandle _handle;
    private readonly RegisteredWaitHandle _wait;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <param name="elapsed">
    /// Called on a thread pool thread at each moment that's set. It must not throw, and must not
    /// dispose the timer.
    /// </param>
    /// <exception cref="Win32Exception">Windows refused the timer.</exception>
    public unsafe AbsoluteTimer(Action elapsed)
    {
        ArgumentNullException.ThrowIfNull(elapsed);

        _elapsed = elapsed;
        var timer = PInvoke.CreateWaitableTimerEx(null, (string?)null, 0, TimerAllAccess);
        if (timer.IsInvalid)
        {
            throw new Win32Exception();
        }

        _timer = new SafeWaitHandle(timer.DangerousGetHandle(), ownsHandle: true);
        timer.SetHandleAsInvalid();
        _handle = new TimerHandle(_timer);
        _wait = ThreadPool.RegisterWaitForSingleObject(_handle, OnSignal, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Whether a message, which every top-level window receives, says that the clock or the time zone changed.</summary>
    public static bool IsClockChangeMessage(uint message) => message == PInvoke.WM_TIMECHANGE;

    /// <summary>Calls back at <paramref name="at"/>, instead of any moment set before; at once when it has passed.</summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public unsafe void Set(DateTimeOffset at)
    {
        // A positive time is a moment in UTC; a negative one would be an interval.
        var dueTime = Math.Max(at.UtcDateTime.ToFileTimeUtc(), 1);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!PInvoke.SetWaitableTimer(_timer, dueTime, 0, null, null, false))
            {
                throw new Win32Exception();
            }
        }
    }

    /// <summary>Calls back at no moment until <see cref="Set"/> is called again.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                PInvoke.CancelWaitableTimer(_timer);
            }
        }
    }

    /// <summary>
    /// Stops the timer. A callback that's running finishes first, so it should be short, and none
    /// comes after this returns.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PInvoke.CancelWaitableTimer(_timer);
        }

        using (var unregistered = new ManualResetEvent(false))
        {
            if (_wait.Unregister(unregistered))
            {
                unregistered.WaitOne();
            }
        }

        _handle.Dispose();
    }

    private void OnSignal(object? state, bool timedOut)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        _elapsed();
    }

    // A waitable timer as a WaitHandle, for the thread pool's wait.
    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle timer) => SafeWaitHandle = timer;
    }
}
