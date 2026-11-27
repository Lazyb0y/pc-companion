using System;
using System.Drawing;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>Edges, pushing, the wait after a trigger and the wheel; <see cref="CornerDetectorTests"/> has the corners.</summary>
public sealed class EdgeDetectorTests
{
    private static readonly Rectangle Main = Rectangle.FromLTRB(0, 0, 1920, 1080);
    private static readonly Point Middle = new(960, 540);
    private static readonly Point RightEdge = new(1919, 540);
    private static readonly Size Right30 = new(30, 0);

    // The desktop covers the display and belongs to the shell.
    private static readonly WindowPlace Desktop = Window(Main, isShell: true);

    private readonly FakeHotCornerEnvironment _environment = new() { PointerPosition = Middle };
    private readonly ManualTimeProvider _clock = new();
    private readonly CornerDetector _detector;

    public EdgeDetectorTests()
    {
        _detector = new CornerDetector(_environment, _clock);
        _environment.Windows.Add(Desktop);
        Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock } });
    }

    [Fact]
    public void AnEdgeTriggersOncePushedHardEnough()
    {
        Arrive(RightEdge);

        Assert.Equal(CornerDecision.Nothing, Push(Right30));
        Assert.Equal(CornerDecision.Nothing, Push(Right30));
        Assert.Equal(CornerDecision.Nothing, Push(Right30));
        Assert.Equal(Decision(CornerOutcome.Triggered), Push(Right30));
    }

    [Fact]
    public void TheMoveThatBringsThePointerToTheEdgeIsntAPush()
    {
        _environment.PointerPosition = RightEdge;

        Assert.Equal(CornerDecision.Nothing, _detector.Update(new Size(500, 0), isPosition: false));
        Assert.Equal(CornerDecision.Nothing, Push(Right30));
    }

    [Fact]
    public void APointerRestingOnAnEdgeNeverTriggersItAndLeavingIsNoResult()
    {
        Arrive(RightEdge);
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(CornerDecision.Nothing, _detector.Update());
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));
    }

    [Fact]
    public void MovementInwardOrAlongTheEdgeIsntAPush()
    {
        Arrive(RightEdge);

        for (var step = 0; step < 10; step++)
        {
            Assert.Equal(CornerDecision.Nothing, Push(new Size(-30, 5)));
        }
    }

    [Fact]
    public void MovementOlderThanASecondNoLongerCounts()
    {
        Arrive(RightEdge);
        Push(new Size(60, 0));
        _clock.Advance(TimeSpan.FromMilliseconds(1100));

        Assert.Equal(CornerDecision.Nothing, Push(new Size(60, 0)));
        Assert.Equal(Decision(CornerOutcome.Triggered), Push(new Size(50, 0)));
    }

    [Fact]
    public void MovingAlongTheEdgeStartsThePushAgain()
    {
        Arrive(RightEdge);
        Push(new Size(60, 0));

        Assert.Equal(CornerDecision.Nothing, PushAt(new Point(1919, 560), new Size(60, 0)));
        Assert.Equal(Decision(CornerOutcome.Triggered), PushAt(new Point(1919, 570), new Size(50, 0)));
    }

    [Theory]
    [InlineData(39)]
    [InlineData(1041)]
    public void NearTheEndsOfAnEdgeAPushDoesNothing(int y)
    {
        Arrive(new Point(1919, y));

        Assert.Equal(CornerDecision.Nothing, PushAt(new Point(1919, y), new Size(400, 0)));
    }

    [Fact]
    public void TheEndsOfAnEdgeScaleWithTheDisplay()
    {
        _detector.Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock } }, [new DisplayDetails(Main, Main, 192)]);
        var nearTheEnd = new Point(1919, 60);
        Arrive(nearTheEnd);

        Assert.Equal(CornerDecision.Nothing, PushAt(nearTheEnd, new Size(400, 0)));
    }

    [Theory]
    [InlineData(PushStrength.Light, 50)]
    [InlineData(PushStrength.Medium, 100)]
    [InlineData(PushStrength.Firm, 200)]
    public void EachPushStrengthNeedsItsMovement(PushStrength strength, int counts)
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock }, PushStrength = strength });
        Arrive(RightEdge);

        Assert.Equal(CornerDecision.Nothing, Push(new Size(counts - 1, 0)));
        Assert.Equal(Decision(CornerOutcome.Triggered), Push(new Size(1, 0)));
    }

    [Fact]
    public void EachSideHasItsEdgeAndItsWayOut()
    {
        Configure(new HotCornersSettings
        {
            Top = new CornerSettings { Action = HotCornerAction.TaskView },
            Bottom = new CornerSettings { Action = HotCornerAction.Search },
            Left = new CornerSettings { Action = HotCornerAction.Start },
        });

        foreach (var (point, push, edge) in new[] { (new Point(960, 0), new Size(0, -120), ScreenCorner.Top), (new Point(960, 1079), new Size(0, 120), ScreenCorner.Bottom), (new Point(0, 540), new Size(-120, 0), ScreenCorner.Left) })
        {
            MoveTo(Middle);
            Arrive(point);
            var decision = PushAt(point, push);
            Assert.Equal((CornerOutcome.Triggered, edge), (decision.Outcome, decision.Corner));
        }
    }

    [Fact]
    public void ThePartOfASideThatAnotherDisplayContinuesIsntAnEdge()
    {
        // A smaller display to the right of the main one, level with its top.
        var smaller = Rectangle.FromLTRB(1920, 0, 2880, 540);
        _detector.Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock } }, [Main, smaller]);

        Arrive(new Point(1919, 200));
        Assert.Null(_detector.CurrentCorner);

        MoveTo(Middle);
        Arrive(new Point(1919, 800));
        Assert.Equal(Decision(CornerOutcome.Triggered), PushAt(new Point(1919, 800), new Size(120, 0)));

        MoveTo(Middle);
        Arrive(new Point(2879, 300));
        Assert.Equal(ScreenCorner.Right, _detector.CurrentCorner);
    }

    [Fact]
    public void ACornerWinsOverTheEdgesThroughIt()
    {
        Configure(new HotCornersSettings { TopLeft = new CornerSettings { Action = HotCornerAction.TaskView }, Top = new CornerSettings { Action = HotCornerAction.Lock }, Left = new CornerSettings { Action = HotCornerAction.Lock } });

        Arrive(new Point(0, 0));

        Assert.Equal(ScreenCorner.TopLeft, _detector.CurrentCorner);
    }

    [Fact]
    public void ADeviceThatReportsPositionsWaitsHalfASecondOnAnEdge()
    {
        _environment.PointerPosition = RightEdge;
        Assert.Equal(new CornerDecision(CornerOutcome.Waiting, ScreenCorner.Right, HotCornerAction.Lock, TimeSpan.FromMilliseconds(500)), _detector.Update(Size.Empty, isPosition: true));

        _clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(Decision(CornerOutcome.Triggered), _detector.Update());
    }

    [Fact]
    public void ADeviceThatReportsPositionsWaitsForALongerActivationDelay()
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock }, ActivationDelayMilliseconds = 800 });
        _environment.PointerPosition = RightEdge;

        Assert.Equal(TimeSpan.FromMilliseconds(800), _detector.Update(Size.Empty, isPosition: true).Wait);
    }

    [Fact]
    public void LeavingAnEdgeAfterHalfAPushIsLeavingEarly()
    {
        Arrive(RightEdge);
        Push(new Size(40, 0));
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));

        Arrive(RightEdge);
        Push(new Size(50, 0));
        Assert.Equal(Decision(CornerOutcome.LeftEarly), MoveTo(Middle));
    }

    [Fact]
    public void AnEdgeDoesntTriggerAgainWithinTheWaitAfterATrigger()
    {
        Arrive(RightEdge);
        Push(new Size(120, 0));
        MoveTo(Middle);
        Arrive(RightEdge);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(new CornerDecision(CornerOutcome.Waiting, ScreenCorner.Right, HotCornerAction.Lock, TimeSpan.FromMilliseconds(250)), Push(new Size(120, 0)));

        _clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(Decision(CornerOutcome.Triggered), _detector.Update());
    }

    [Fact]
    public void ACornerDoesntTriggerAgainWithinTheWaitAfterATrigger()
    {
        Configure(new HotCornersSettings());
        var corner = new Point(0, 0);
        MoveTo(corner);
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        _detector.Update();
        MoveTo(Middle);

        var decision = MoveTo(corner);

        Assert.Equal((CornerOutcome.Waiting, TimeSpan.FromMilliseconds(350)), (decision.Outcome, decision.Wait));
    }

    [Fact]
    public void TheWaitAfterATriggerIsLongerWithALongDelay()
    {
        Configure(new HotCornersSettings { ActivationDelayMilliseconds = 600 });
        var corner = new Point(0, 0);
        MoveTo(corner);
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        _detector.Update();
        MoveTo(Middle);

        Assert.Equal(TimeSpan.FromMilliseconds(650), MoveTo(corner).Wait);
    }

    [Fact]
    public void CornersTriggerWhenPushedWhenSetSo()
    {
        Configure(new HotCornersSettings { CornersTrigger = CornersTrigger.Push });
        var corner = new Point(0, 0);
        Arrive(corner);
        _clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(CornerDecision.Nothing, _detector.Update());
        var decision = PushAt(corner, new Size(-60, -50));
        Assert.Equal((CornerOutcome.Triggered, ScreenCorner.TopLeft), (decision.Outcome, decision.Corner));
    }

    [Fact]
    public void AMouseButtonHeldOnAnEdgeIsNoResult()
    {
        _environment.IsMouseButtonHeld = true;

        Assert.Equal(CornerDecision.Nothing, Arrive(RightEdge));
        Assert.Equal(CornerDecision.Nothing, Push(new Size(200, 0)));
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));

        _environment.IsMouseButtonHeld = false;
        Arrive(RightEdge);
        _environment.IsMouseButtonHeld = true;
        Assert.Equal(CornerDecision.Nothing, Push(new Size(200, 0)));
    }

    [Fact]
    public void AnEdgeWhereABarHidesItselfIsSkipped()
    {
        _environment.AutoHideBars.Add((Main, ScreenSide.Right));
        Arrive(RightEdge);

        Assert.Equal(Decision(CornerOutcome.TaskbarHides), Push(new Size(120, 0)));
    }

    [Fact]
    public void ABarOnAnotherSideDoesntSkipTheEdge()
    {
        _environment.AutoHideBars.Add((Main, ScreenSide.Bottom));
        Arrive(RightEdge);

        Assert.Equal(Decision(CornerOutcome.Triggered), Push(new Size(120, 0)));
    }

    [Fact]
    public void AnEdgeChecksTheKeyAndFullScreenAsACornerDoes()
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock, Key = HotCornerKey.Control } });
        Arrive(RightEdge);
        Assert.Equal(Decision(CornerOutcome.KeyNotHeld), Push(new Size(120, 0)));

        _environment.Windows.Insert(0, Window(Main));
        Configure(new HotCornersSettings { Right = new CornerSettings { Action = HotCornerAction.Lock } });
        MoveTo(Middle);
        Arrive(RightEdge);
        Assert.Equal(Decision(CornerOutcome.FullScreenApp), Push(new Size(120, 0)));
    }

    [Fact]
    public void AnEdgeWithOnlyAWheelActionNeverTriggers()
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Wheel = HotCornerWheelAction.Volume } });
        Arrive(RightEdge);

        Assert.Equal(CornerDecision.Nothing, Push(new Size(500, 0)));
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));
    }

    [Fact]
    public void TheWheelAppliesOnTheEdgesLineWhileThePointerIsThere()
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Wheel = HotCornerWheelAction.Volume, WheelKey = HotCornerKey.Alt } });

        Arrive(RightEdge);
        Assert.Equal(new WheelSpot(ScreenCorner.Right, new Rectangle(1919, 0, 1, 1080), HotCornerWheelAction.Volume, HotCornerKey.Alt), _detector.Wheel);

        MoveTo(new Point(1918, 540));
        Assert.Null(_detector.Wheel);
    }

    [Fact]
    public void TheWheelAppliesOnACornersPixel()
    {
        Configure(new HotCornersSettings { TopLeft = new CornerSettings { Wheel = HotCornerWheelAction.Desktops } });

        Arrive(new Point(0, 0));

        Assert.Equal(new Rectangle(0, 0, 1, 1), _detector.Wheel?.Area);
    }

    [Fact]
    public void TheWheelDoesntApplyWhenTheChecksFailAsThePointerGetsThere()
    {
        Configure(new HotCornersSettings { Right = new CornerSettings { Wheel = HotCornerWheelAction.Volume }, ExcludedApps = [new ListedApp { Name = "Remote Desktop Connection", Program = @"C:\Windows\System32\mstsc.exe" }] });
        _environment.FrontApp = new AppOfWindow(null, null, @"C:\Windows\System32\mstsc.exe", 10);
        Arrive(RightEdge);
        Assert.Null(_detector.Wheel);

        _environment.FrontApp = null;
        _environment.Windows.Insert(0, Window(Main));
        MoveTo(Middle);
        Arrive(RightEdge);
        Assert.Null(_detector.Wheel);

        _environment.Windows.RemoveAt(0);
        _environment.IsMouseButtonHeld = true;
        MoveTo(Middle);
        Arrive(RightEdge);
        Assert.Null(_detector.Wheel);
    }

    [Fact]
    public void TheWheelOfAnEdgeThePointerIsOnWhenConfiguredWaitsUntilItComesBack()
    {
        _environment.PointerPosition = RightEdge;
        Configure(new HotCornersSettings { Right = new CornerSettings { Wheel = HotCornerWheelAction.Volume } });

        Assert.Null(_detector.Wheel);
    }

    [Fact]
    public void LeavingACornerThatWaitsForTheDelayIsLeavingEarlyAlsoAfterAPosition()
    {
        // A position read before the pointer followed it, as when it leaves by a jump.
        Configure(new HotCornersSettings { ActivationDelayMilliseconds = 500 });
        var corner = new Point(0, 0);
        Arrive(corner);
        _detector.Update(Size.Empty, isPosition: true);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        var decision = MoveTo(Middle);

        Assert.Equal((CornerOutcome.LeftEarly, ScreenCorner.TopLeft), (decision.Outcome, decision.Corner));
    }

    [Fact]
    public void TheDetectorRemembersWhereItLastSawThePointer()
    {
        MoveTo(new Point(100, 200));

        Assert.Equal(new Point(100, 200), _detector.LastPosition);
    }

    // A borderless window that isn't an overlay, the shell's or out of sight, unless the test says so.
    private static WindowPlace Window(Rectangle bounds, bool isShell = false) =>
        new(0, bounds, IsCloaked: false, IsMinimized: false, IsClickThrough: false, IsNoActivate: false, IsToolWindow: false, false, false, isShell);

    private static CornerDecision Decision(CornerOutcome outcome) =>
        new(outcome, ScreenCorner.Right, HotCornerAction.Lock, TimeSpan.Zero);

    private void Configure(HotCornersSettings settings) => _detector.Configure(settings, [Main]);

    // The pointer gets there; that move isn't a push.
    private CornerDecision Arrive(Point point)
    {
        _environment.PointerPosition = point;
        return _detector.Update(Size.Empty, isPosition: false);
    }

    private CornerDecision MoveTo(Point point)
    {
        _environment.PointerPosition = point;
        return _detector.Update();
    }

    private CornerDecision Push(Size movement) => _detector.Update(movement, isPosition: false);

    private CornerDecision PushAt(Point point, Size movement)
    {
        _environment.PointerPosition = point;
        return _detector.Update(movement, isPosition: false);
    }
}
