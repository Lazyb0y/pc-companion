using System;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class SessionSignalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void EachSentSignalReachesTheListener()
    {
        var name = UniqueName();
        var count = 0;
        using var received = new AutoResetEvent(initialState: false);
        using var listener = SessionSignal.Listen(name, () =>
        {
            Interlocked.Increment(ref count);
            received.Set();
        });

        Assert.NotNull(listener);
        Assert.True(SessionSignal.Send(name));
        Assert.True(received.WaitOne(Timeout));
        Assert.True(SessionSignal.Send(name));
        Assert.True(received.WaitOne(Timeout));
        Assert.Equal(2, Volatile.Read(ref count));
    }

    [Fact]
    public void ASignalNothingListensForIsNotSent()
    {
        Assert.False(SessionSignal.Send(UniqueName()));
    }

    // A listener that just stopped may still have the signal open for a moment, so whether Send
    // finds it isn't checked.
    [Fact]
    public void AStoppedListenerGetsNothing()
    {
        var name = UniqueName();
        var calls = 0;
        var listener = SessionSignal.Listen(name, () => Interlocked.Increment(ref calls));
        Assert.NotNull(listener);

        listener.Dispose();
        _ = SessionSignal.Send(name);
        Thread.Sleep(500);

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void StoppingWaitsForACallThatIsRunning()
    {
        var name = UniqueName();
        var finished = false;
        using var started = new ManualResetEventSlim();
        var listener = SessionSignal.Listen(name, () =>
        {
            started.Set();
            Thread.Sleep(300);
            Volatile.Write(ref finished, true);
        });
        Assert.NotNull(listener);
        Assert.True(SessionSignal.Send(name));
        Assert.True(started.Wait(Timeout, TestContext.Current.CancellationToken));

        listener.Dispose();

        Assert.True(Volatile.Read(ref finished));
    }

    private static string UniqueName() => $@"Local\PCCompanion.Tests.{Guid.NewGuid():N}";
}
