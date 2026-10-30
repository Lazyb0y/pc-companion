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
    public void BringsWindowsBackThenPutsThemRightBehindAWindowInTheirOrder()
    {
        // Minimized windows sit behind the others, in no particular order.
        _system.Add(1);
        _system.Add(9);
        _system.Add(4).Minimized = true;
        _system.Add(2).Minimized = true;
        _system.Add(3).Minimized = true;

        _restore.Start([2, 3, 4], behind: 1);

        Assert.All([2, 3, 4], window => Assert.False(_system[window].Minimized));
        Assert.Equal(["watch order True", "restore 2", "restore 3", "restore 4"], _system.Calls);

        Settle();

        Assert.Equal([1, 2, 3, 4, 9], _system.Handles);
        Assert.Equal(["behind 2 1", "behind 3 2", "behind 4 3", "watch order False"], _system.Calls[4..]);
        Assert.False(_restore.IsRunning);
    }

    [Fact]
    public void TheWindowsArePutInOrderOnlyAfterTheyCameBackAndSettled()
    {
        _system.Add(1);
        _system.Add(9);
        _system.Add(2).Minimized = true;

        _restore.Start([2], behind: 1);
        _clock.Advance(OrderedRestore.SettleTime - OrderedRestore.CheckInterval);

        Assert.DoesNotContain("behind 2 1", _system.Calls);

        _clock.Advance(OrderedRestore.CheckInterval * 2);

        Assert.Contains("behind 2 1", _system.Calls);
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
        Settle();

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
        _system.Add(9);
        _system.Add(3);
        _system.Add(2);
        _restore.Start([2, 3], behind: 1);
        Settle();

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
        Settle();

        _clock.Advance(OrderedRestore.MoveTimeout - (OrderedRestore.CheckInterval * 2));
        Assert.DoesNotContain("behind 3 1", _system.Calls);

        _clock.Advance(OrderedRestore.CheckInterval * 2);
        Assert.Contains("behind 3 1", _system.Calls);

        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Assert.Equal([1, 3, 9, 2], _system.Handles);
        Assert.False(_restore.IsRunning);
    }

    [Fact]
    public void WindowsAltTabDoesntShowDontHoldUpAMove()
    {
        // A palette, which sits in front of its owner, and a helper window Windows keeps out of sight.
        _system.Add(1);
        _system.Add(9);
        _system.Add(3);
        _system.Order.Add(new FakeHideWindow { Handle = 20, Owner = 2 });
        _system.Add(2);
        _system.Order.Insert(1, new FakeHideWindow { Handle = 30, Cloaked = true });

        _restore.Start([2, 3], behind: 1);
        Settle();

        Assert.Equal([1, 20, 2, 3, 30, 9], _system.Handles);
        Assert.False(_restore.IsRunning);
    }

    [Fact]
    public void AWindowThatMovesItselfAsItComesBackIsStillPutInOrder()
    {
        _system.Add(1);
        _system.Add(9);
        _system.Add(3).Minimized = true;
        var moving = _system.Add(2);
        moving.Minimized = true;
        moving.MovesToTopOnRestore = true;

        _restore.Start([2, 3], behind: 1);
        Assert.Equal(2, _system.Order[0].Handle);
        Settle();

        Assert.Equal([1, 2, 3, 9], _system.Handles);
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
        Settle();

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
        Settle();

        Assert.Equal(["watch order True", "behind 3 2", "watch order False"], _system.Calls);
        Assert.Equal([9, 2, 3], _system.Handles);
    }

    [Fact]
    public void TheWindowsAreWaitedForToComeBackForASecondAtMost()
    {
        _system.Add(1);
        _system.Add(9);
        var late = _system.Add(2);
        late.Minimized = true;
        late.RunsAsAdministrator = true;

        _restore.Start([2], behind: 1);
        _clock.Advance(OrderedRestore.RestoreTimeout - OrderedRestore.CheckInterval);
        Assert.True(_restore.IsRunning);

        _clock.Advance((OrderedRestore.CheckInterval * 2) + OrderedRestore.SettleTime);
        Assert.False(_restore.IsRunning);
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
        Settle();

        Assert.DoesNotContain("behind 6 5", _system.Calls);

        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Settle();

        Assert.Contains("behind 6 5", _system.Calls);
        Assert.True(_restore.IsRunning);
        _system.ApplyMoves();
        _restore.OnOrderChanged();
        Assert.False(_restore.IsRunning);
    }

    // Past the time the restored windows are given to settle.
    private void Settle() => _clock.Advance(OrderedRestore.SettleTime + OrderedRestore.CheckInterval);
}
