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

    [Fact]
    public void AStoppedListenerGetsNothing()
    {
        var name = UniqueName();
        var listener = SessionSignal.Listen(name, () => Assert.Fail("A stopped listener was called."));
        Assert.NotNull(listener);

        listener.Dispose();

        Assert.False(SessionSignal.Send(name));
    }

    private static string UniqueName() => $@"Local\PCCompanion.Tests.{Guid.NewGuid():N}";
}
