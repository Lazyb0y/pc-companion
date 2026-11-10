using System;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class AbsoluteTimerTests
{
    // The thread pool can take seconds to get to a callback on a busy build machine.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void TheTimerCallsBackAtTheMoment()
    {
        using var elapsed = new SemaphoreSlim(0);
        using var timer = new AbsoluteTimer(() => elapsed.Release());
        var at = DateTimeOffset.UtcNow.AddMilliseconds(300);

        timer.Set(at);

        Assert.True(elapsed.Wait(Timeout, TestContext.Current.CancellationToken));
        Assert.True(DateTimeOffset.UtcNow >= at.AddMilliseconds(-20));
    }

    [Fact]
    public void AMomentThatPassedCallsBackAtOnce()
    {
        using var elapsed = new SemaphoreSlim(0);
        using var timer = new AbsoluteTimer(() => elapsed.Release());

        timer.Set(DateTimeOffset.UtcNow.AddHours(-1));

        Assert.True(elapsed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SettingAgainReplacesTheMoment()
    {
        using var elapsed = new SemaphoreSlim(0);
        using var timer = new AbsoluteTimer(() => elapsed.Release());

        timer.Set(DateTimeOffset.UtcNow.AddHours(1));
        timer.Set(DateTimeOffset.UtcNow.AddMilliseconds(100));

        Assert.True(elapsed.Wait(Timeout, TestContext.Current.CancellationToken));
        Assert.False(elapsed.Wait(Quiet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ACancelledTimerDoesntCallBack()
    {
        using var elapsed = new SemaphoreSlim(0);
        using var timer = new AbsoluteTimer(() => elapsed.Release());

        timer.Set(DateTimeOffset.UtcNow.AddMilliseconds(200));
        timer.Cancel();

        Assert.False(elapsed.Wait(Quiet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ADisposedTimerDoesntCallBack()
    {
        using var elapsed = new SemaphoreSlim(0);
        var timer = new AbsoluteTimer(() => elapsed.Release());

        timer.Set(DateTimeOffset.UtcNow.AddMilliseconds(200));
        timer.Dispose();
        timer.Dispose();

        Assert.False(elapsed.Wait(Quiet, TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => timer.Set(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AClockChangeIsAnnounced()
    {
        Assert.True(AbsoluteTimer.IsClockChangeMessage(0x001E));
        Assert.False(AbsoluteTimer.IsClockChangeMessage(0x001A));
    }
}
