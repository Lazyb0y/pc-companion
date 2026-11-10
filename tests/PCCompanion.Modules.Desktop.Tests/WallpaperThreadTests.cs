using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class WallpaperThreadTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void JobsRunInOrderOnAnMtaThreadOfTheirOwn()
    {
        var ran = new ConcurrentQueue<(int Job, int Thread, ApartmentState Apartment)>();
        var worker = new WallpaperThread(TimeSpan.FromSeconds(10), () => { });

        for (var job = 0; job < 5; job++)
        {
            var number = job;
            worker.Post(() => ran.Enqueue((number, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())));
        }

        Assert.True(worker.Finish(Timeout));
        Assert.Equal([0, 1, 2, 3, 4], ran.Select(entry => entry.Job));
        Assert.Single(ran.Select(entry => entry.Thread).Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, ran.First().Thread);
        Assert.All(ran, entry => Assert.Equal(ApartmentState.MTA, entry.Apartment));
    }

    [Fact]
    public void NoJobRunsOnceItsDisposed()
    {
        var worker = new WallpaperThread(TimeSpan.FromSeconds(10), () => { });
        worker.Dispose();
        var ran = false;

        worker.Post(() => ran = true);

        Assert.True(worker.Finish(Timeout));
        Assert.False(ran);
    }

    [Fact]
    public void AJobPostedAfterTheThreadEndedDoesNothing()
    {
        var worker = new WallpaperThread(TimeSpan.FromSeconds(10), () => { });
        Assert.True(worker.Finish(Timeout));
        var ran = false;

        worker.Post(() => ran = true);
        worker.Dispose();

        Assert.True(worker.Finish(Timeout));
        Assert.False(ran);
    }
}
