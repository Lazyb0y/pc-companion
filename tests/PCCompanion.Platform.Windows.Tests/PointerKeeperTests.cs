using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class PointerKeeperTests
{
    private static readonly PointerTrigger MiddleClick = new(PointerButton.Middle, PointerKeys.None);
    private static readonly PointerTrigger Wheel = new(PointerButton.Wheel, PointerKeys.None);
    private static readonly PointerTrigger Back = new(PointerButton.Back, PointerKeys.None);
    private static readonly PointerTrigger Forward = new(PointerButton.Forward, PointerKeys.None);
    private static readonly Point Inside = new(105, 205);
    private static readonly Point Outside = new(300, 300);

    [Fact]
    public void AClaimedMiddleClickIsKeptForItsClaimAndItsReleaseForNoOne()
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(MiddleClick)];

        Assert.Equal(new PointerDecision(true, 0), keeper.Decide(claims, PointerInput.MiddleDown, Inside, PointerKeys.None, 1));
        Assert.Equal(new PointerDecision(true, -1), keeper.Decide(claims, PointerInput.MiddleUp, Inside, PointerKeys.None, 2));
    }

    [Fact]
    public void AMiddleClickOutsideTheAreaIsLetThroughWithItsRelease()
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(MiddleClick)];

        Assert.Equal(new PointerDecision(false, -1), keeper.Decide(claims, PointerInput.MiddleDown, Outside, PointerKeys.None, 1));
        Assert.Equal(new PointerDecision(false, -1), keeper.Decide(claims, PointerInput.MiddleUp, Inside, PointerKeys.None, 2));
    }

    [Theory]
    [InlineData(PointerKeys.Control)]
    [InlineData(PointerKeys.Alt)]
    [InlineData(PointerKeys.Shift | PointerKeys.Control)]
    public void OnlyTheClaimsKeysExactlyAreKept(PointerKeys held)
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(MiddleClick, new PointerTrigger(PointerButton.Middle, PointerKeys.Shift))];

        Assert.False(keeper.Decide(claims, PointerInput.MiddleDown, Inside, held, 1).Keep);
        Assert.True(keeper.Decide(claims, PointerInput.MiddleDown, Inside, PointerKeys.Shift, 2).Keep);
    }

    [Fact]
    public void TheWheelIsKeptOnlyWhenClaimed()
    {
        var keeper = new PointerKeeper();

        Assert.False(keeper.Decide([Claim(MiddleClick)], PointerInput.Wheel, Inside, PointerKeys.None, 1).Keep);
        Assert.Equal(new PointerDecision(true, 0), keeper.Decide([Claim(Wheel)], PointerInput.Wheel, Inside, PointerKeys.None, 2));
    }

    [Fact]
    public void TheFirstClaimThatKeepsTheInputWins()
    {
        var keeper = new PointerKeeper();
        var elsewhere = new PointerClaim(new Rectangle(280, 280, 40, 40), new HashSet<PointerTrigger> { MiddleClick });
        PointerClaim[] claims = [elsewhere, Claim(MiddleClick), Claim(MiddleClick)];

        Assert.Equal(1, keeper.Decide(claims, PointerInput.MiddleDown, Inside, PointerKeys.None, 1).Claim);
        Assert.Equal(0, keeper.Decide(claims, PointerInput.MiddleDown, Outside, PointerKeys.None, 2).Claim);
    }

    [Fact]
    public void AKeptPressKeepsItsReleaseAfterTheClaimIsGone()
    {
        var keeper = new PointerKeeper();
        keeper.Decide([Claim(MiddleClick)], PointerInput.MiddleDown, Inside, PointerKeys.None, 1);

        Assert.True(keeper.Decide([], PointerInput.MiddleUp, Outside, PointerKeys.None, 2).Keep);
        Assert.False(keeper.Decide([], PointerInput.MiddleUp, Outside, PointerKeys.None, 3).Keep);
    }

    [Fact]
    public void TheOutcomeOfEachInputIsFoundByItsTime()
    {
        var keeper = new PointerKeeper();
        keeper.Decide([Claim(MiddleClick)], PointerInput.MiddleDown, Inside, PointerKeys.None, 10);
        keeper.Decide([Claim(MiddleClick)], PointerInput.Wheel, Inside, PointerKeys.None, 11);

        Assert.Equal(PointerInputOutcome.Kept, keeper.Outcome(10));
        Assert.Equal(PointerInputOutcome.LetThrough, keeper.Outcome(11));
        Assert.Equal(PointerInputOutcome.NotSeen, keeper.Outcome(12));
    }

    [Fact]
    public void OnlyTheLatestInputsAreRemembered()
    {
        var keeper = new PointerKeeper();
        for (uint time = 1; time <= PointerKeeper.HistoryLength + 1; time++)
        {
            keeper.Decide([Claim(Wheel)], PointerInput.Wheel, Inside, PointerKeys.None, time);
        }

        Assert.Equal(PointerInputOutcome.NotSeen, keeper.Outcome(1));
        Assert.Equal(PointerInputOutcome.Kept, keeper.Outcome(2));
        Assert.Equal(PointerInputOutcome.Kept, keeper.Outcome(PointerKeeper.HistoryLength + 1));
    }

    [Fact]
    public void AClaimKeepsOnlyInsideItsArea()
    {
        var claim = Claim(MiddleClick);

        Assert.True(claim.Keeps(MiddleClick, new Point(100, 200)));
        Assert.True(claim.Keeps(MiddleClick, new Point(109, 209)));
        Assert.False(claim.Keeps(MiddleClick, new Point(110, 205)));
        Assert.False(claim.Keeps(Wheel, Inside));
    }

    [Fact]
    public void AClaimedBackClickIsKeptWithItsRelease()
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(Back)];

        Assert.Equal(new PointerDecision(true, 0), keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 1));
        Assert.Equal(new PointerDecision(true, -1), keeper.Decide(claims, PointerInput.BackUp, Inside, PointerKeys.None, 2));
        Assert.Equal(new PointerDecision(false, -1), keeper.Decide(claims, PointerInput.ForwardDown, Inside, PointerKeys.None, 3));
        Assert.Equal(new PointerDecision(false, -1), keeper.Decide(claims, PointerInput.ForwardUp, Inside, PointerKeys.None, 4));
    }

    [Fact]
    public void EachButtonsReleaseFollowsItsOwnPress()
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(Forward)];

        keeper.Decide(claims, PointerInput.ForwardDown, Inside, PointerKeys.None, 1);
        keeper.Decide(claims, PointerInput.MiddleDown, Inside, PointerKeys.None, 2);

        Assert.False(keeper.Decide(claims, PointerInput.MiddleUp, Inside, PointerKeys.None, 3).Keep);
        Assert.True(keeper.Decide(claims, PointerInput.ForwardUp, Inside, PointerKeys.None, 4).Keep);
    }

    [Fact]
    public void TheKeysHeldWithBackMustMatchExactly()
    {
        var keeper = new PointerKeeper();
        PointerClaim[] claims = [Claim(new PointerTrigger(PointerButton.Back, PointerKeys.Control))];

        Assert.False(keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 1).Keep);
        Assert.False(keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.Control | PointerKeys.Shift, 2).Keep);
        Assert.True(keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.Control, 3).Keep);
    }

    [Fact]
    public void OverAWindowTheTriggerSkipsTheClickIsLetThrough()
    {
        var keeper = new PointerKeeper();
        var place = new FakePlace { Window = 42 };
        PointerClaim[] claims = [Claim(Back) with { ExceptIn = new Dictionary<PointerTrigger, IReadOnlySet<nint>> { [Back] = new HashSet<nint> { 42 } } }];

        Assert.Equal(new PointerDecision(false, -1), keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 1, place));
        Assert.False(keeper.Decide(claims, PointerInput.BackUp, Inside, PointerKeys.None, 2, place).Keep);

        place.Window = 7;
        Assert.True(keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 3, place).Keep);
    }

    [Fact]
    public void TheWindowUnderThePointerIsReadOnlyForATriggerThatSkipsSome()
    {
        var keeper = new PointerKeeper();
        var place = new FakePlace();
        PointerClaim[] claims = [Claim(Back, Forward) with { ExceptIn = new Dictionary<PointerTrigger, IReadOnlySet<nint>> { [Back] = new HashSet<nint> { 42 } } }];

        keeper.Decide(claims, PointerInput.ForwardDown, Inside, PointerKeys.None, 1, place);
        Assert.Equal(0, place.WindowReads);

        keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 2, place);
        Assert.Equal(1, place.WindowReads);
    }

    [Fact]
    public void AClaimThatStepsAsideForACapturedPointerLetsTheClickThrough()
    {
        var keeper = new PointerKeeper();
        var place = new FakePlace { Captured = true };
        PointerClaim[] claims = [Claim(Back) with { NotWhileCaptured = true }, Claim(Back)];

        Assert.Equal(new PointerDecision(true, 1), keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 1, place));

        place.Captured = false;
        Assert.Equal(new PointerDecision(true, 0), keeper.Decide(claims, PointerInput.BackDown, Inside, PointerKeys.None, 2, place));
    }

    [Fact]
    public void ACapturedPointerIsAskedAboutOnlyForAClaimThatStepsAside()
    {
        var keeper = new PointerKeeper();
        var place = new FakePlace { Captured = true };

        Assert.True(keeper.Decide([Claim(MiddleClick)], PointerInput.MiddleDown, Inside, PointerKeys.None, 1, place).Keep);
        Assert.Equal(0, place.CaptureReads);
    }

    [Fact]
    public void TheOutcomeOfABackClickIsRemembered()
    {
        var keeper = new PointerKeeper();

        keeper.Decide([Claim(Back)], PointerInput.BackDown, Inside, PointerKeys.None, 20);
        keeper.Decide([Claim(Back)], PointerInput.ForwardDown, Inside, PointerKeys.None, 21);

        Assert.Equal(PointerInputOutcome.Kept, keeper.Outcome(20));
        Assert.Equal(PointerInputOutcome.LetThrough, keeper.Outcome(21));
    }

    // A claim on the 10 by 10 pixel square at (100, 200).
    private static PointerClaim Claim(params PointerTrigger[] triggers) => new(new Rectangle(100, 200, 10, 10), new HashSet<PointerTrigger>(triggers));

    private sealed class FakePlace : IPointerPlace
    {
        public nint Window { get; set; }

        public bool Captured { get; set; }

        public int WindowReads { get; private set; }

        public int CaptureReads { get; private set; }

        public nint WindowAt(Point point)
        {
            WindowReads++;
            return Window;
        }

        public bool IsPointerCaptured()
        {
            CaptureReads++;
            return Captured;
        }
    }
}
