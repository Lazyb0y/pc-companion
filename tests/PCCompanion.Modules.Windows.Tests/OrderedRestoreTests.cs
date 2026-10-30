using System;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class OrderedRestoreTests : IDisposable
{
    private readonly FakeHideAppsSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly OrderedRestore _restore;

    public OrderedRestoreTests()
    {
        _restore = new OrderedRestore(_system, _clock, action => action(), new RecordingLogger<OrderedRestore>());
    }

    public void Dispose() => _restore.Dispose();

    [Fact]
    public void BringsWindowsBackRightBehindAWindowInTheirOrder()
    {
        // Minimized windows sit behind the others, in no particular order.
        _system.Add(1);
        _system.Add(9);
        _system.Add(4).Minimized = true;
        _system.Add(2).Minimized = true;
        _system.Add(3).Minimized = true;

        _restore.Start([2, 3, 4], behind: 1);

        Assert.Equal([1, 2, 3, 4, 9], _system.Handles);
        Assert.All([2, 3, 4], window => Assert.False(_system[window].Minimized));
        Assert.Equal(["restore 2", "restore 3", "restore 4", "watch order True", "behind 2 1", "behind 3 2", "behind 4 3", "watch order False"], _system.Calls);
        Assert.False(_restore.IsRunning);
    }

    [Fact]
    public void EachMoveIsWaitedForBeforeTheNext()
    {
        _system.MovesLater = true;
        _system.Add(1);
        _system.Add(9);
        _system.Add(3).Minimized = true;
        _system.Add(2).Minimized = true;

        _restore.Start([2, 3], behind: 1);

        Assert.DoesNotContain("behind 3 2", _system.Calls);
        Assert.True(_restore.IsRunning);

        _system.ApplyMoves();
        _restore.OnOrderChanged();

        Assert.Contains("behind 3 2", _system.Calls);
        Assert.True(_restore.IsRunning);
        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Assert.Equal([1, 2, 3, 9], _system.Handles);
        Assert.False(_restore.IsRunning);
        Assert.False(_system.WatchesOrder);
    }

    [Fact]
    public void TheTimerChecksAMoveWhenWindowsReportsNoChange()
    {
        _system.MovesLater = true;
        _system.Add(1);
        _system.Add(3).Minimized = true;
        _system.Add(2).Minimized = true;
        _restore.Start([2, 3], behind: 1);

        _system.ApplyMoves();
        _clock.Advance(OrderedRestore.CheckInterval);

        Assert.Contains("behind 3 2", _system.Calls);
    }

    [Fact]
    public void AWindowThatDoesntMoveInTimeIsLeftAndTheNextGoesBehindTheLastInPlace()
    {
        _system.MovesLater = true;
        _system.Add(1);
        _system.Add(9);
        _system.Add(2).NeverMoves = true;
        _system.Add(3);

        _restore.Start([2, 3], behind: 1);
        _clock.Advance(OrderedRestore.MoveTimeout - OrderedRestore.CheckInterval);
        Assert.DoesNotContain("behind 3 1", _system.Calls);

        _clock.Advance(OrderedRestore.CheckInterval);
        Assert.Contains("behind 3 1", _system.Calls);

        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Assert.Equal([1, 3, 9, 2], _system.Handles);
        Assert.False(_restore.IsRunning);
    }

    [Fact]
    public void WindowsThatClosedStoppedRespondingOrCantBeMovedAreSkipped()
    {
        _system.Add(1);
        _system.Add(2).Closed = true;
        _system.Add(3).NotResponding = true;
        _system.Add(4).RunsAsAdministrator = true;
        _system.Add(5);

        _restore.Start([2, 3, 4, 5], behind: 1);

        Assert.DoesNotContain("behind 2 1", _system.Calls);
        Assert.DoesNotContain("behind 3 1", _system.Calls);
        Assert.Contains("behind 4 1", _system.Calls);
        Assert.Contains("behind 5 1", _system.Calls);
        Assert.Equal([1, 5, 2, 3, 4], _system.Handles);
    }

    [Fact]
    public void WithoutAWindowToGoBehindTheFirstStaysWhereItComesBack()
    {
        _system.Add(9);
        _system.Add(3);
        _system.Add(2);

        _restore.Start([2, 3], behind: 0);

        Assert.Equal(["watch order True", "behind 3 2", "watch order False"], _system.Calls);
        Assert.Equal([9, 2, 3], _system.Handles);
    }

    [Fact]
    public void ALaterStartWaitsForTheWindowsBeingPutInOrder()
    {
        _system.MovesLater = true;
        _system.Add(1);
        _system.Add(9);
        _system.Add(2);
        _system.Add(5);
        _system.Add(8);
        _system.Add(6);

        _restore.Start([2], behind: 1);
        _restore.Start([6], behind: 5);

        Assert.DoesNotContain("behind 6 5", _system.Calls);

        _system.ApplyMoves();
        _restore.OnOrderChanged();

        Assert.Contains("behind 6 5", _system.Calls);
        Assert.True(_restore.IsRunning);
        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Assert.False(_restore.IsRunning);
    }
}
