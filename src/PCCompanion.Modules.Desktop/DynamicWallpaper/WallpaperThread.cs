using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// The wallpaper thread: an MTA thread of its own that runs jobs one at a time, in order, where
/// calls to Explorer can wait without holding the host's UI thread. A job that takes longer than its
/// limit has its call cancelled (<see cref="CallCancellation"/>), so a hung Explorer can't keep the
/// thread forever. See "Threads" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed class WallpaperThread : IWallpaperWorker
{
    private readonly BlockingCollection<Action> _jobs = [];
    private readonly Thread _thread;
    private readonly TimeSpan _limit;
    private readonly Action _slow;
    private uint _threadId;
    private bool _canCancel;

    /// <param name="limit">How long a job may take before its call is cancelled.</param>
    /// <param name="slow">Called on the thread pool when a job's call is cancelled.</param>
    public WallpaperThread(TimeSpan limit, Action slow)
    {
        _limit = limit;
        _slow = slow;
        _thread = new Thread(Run) { IsBackground = true, Name = "Dynamic Wallpaper" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Post(Action job)
    {
        try
        {
            _jobs.Add(job);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // Disposed: no more jobs run.
        }
    }

    public bool Finish(TimeSpan timeout)
    {
        Dispose();
        if (!_thread.Join(timeout))
        {
            return false;
        }

        _jobs.Dispose();
        return true;
    }

    public void Dispose()
    {
        try
        {
            _jobs.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Finished already.
        }
    }

    private void Run()
    {
        try
        {
            CallCancellation.Allow();
            _threadId = CallCancellation.CurrentThreadId;
            _canCancel = true;
        }
        catch (COMException)
        {
            // Calls can't be cancelled then; they still run.
        }

        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            using var watch = new Timer(_ => Cancel(), null, _limit, Timeout.InfiniteTimeSpan);
            job();
        }
    }

    private void Cancel()
    {
        if (_canCancel && CallCancellation.Cancel(_threadId))
        {
            _slow();
        }
    }
}
