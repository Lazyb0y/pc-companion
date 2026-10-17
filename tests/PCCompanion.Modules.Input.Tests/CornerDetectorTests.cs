using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Input.HotCorners;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class CornerDetectorTests
{
    private static readonly Rectangle Main = Rectangle.FromLTRB(0, 0, 1920, 1080);
    private static readonly Point Middle = new(960, 540);
    private static readonly Point TopLeft = new(0, 0);

    private readonly FakeEnvironment _environment = new() { PointerPosition = Middle };
    private readonly ManualClock _clock = new();
    private readonly CornerDetector _detector;

    public CornerDetectorTests()
    {
        _detector = new CornerDetector(_environment, _clock);
        _detector.Configure(new HotCornersSettings(), [Main]);
    }

    [Fact]
    public void ACornerTriggersAfterTheActivationDelay()
    {
        Assert.Equal(Waiting(TimeSpan.FromMilliseconds(150)), MoveTo(TopLeft));

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(Waiting(TimeSpan.FromMilliseconds(50)), _detector.Update());

        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(Decision(CornerOutcome.Triggered), _detector.Update());
    }

    [Fact]
    public void WithoutADelayACornerTriggersAtOnce()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void ACornerTriggersOnceUntilThePointerLeavesIt()
    {
        TriggerTopLeft();

        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(CornerDecision.Nothing, _detector.Update());

        Assert.Equal(CornerDecision.Nothing, MoveTo(new Point(1, 0)));
        MoveTo(TopLeft);
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(Decision(CornerOutcome.Triggered), _detector.Update());
    }

    [Fact]
    public void LeavingBeforeTheDelayEndsCancelsTheCorner()
    {
        MoveTo(TopLeft);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        MoveTo(Middle);

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(CornerDecision.Nothing, _detector.Update());
    }

    [Fact]
    public void ComingBackStartsTheDelayAgain()
    {
        MoveTo(TopLeft);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        MoveTo(Middle);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(Waiting(TimeSpan.FromMilliseconds(150)), MoveTo(TopLeft));
    }

    [Fact]
    public void ACornerWithoutAnActionDoesNothing()
    {
        Assert.Equal(CornerDecision.Nothing, MoveTo(new Point(1919, 0)));
    }

    [Fact]
    public void ThePointerNearACornerIsNotInIt()
    {
        Assert.Equal(CornerDecision.Nothing, MoveTo(new Point(0, 1)));
    }

    [Fact]
    public void AButtonHeldWhenThePointerArrivesSkipsTheCornerUntilThePointerLeaves()
    {
        _environment.IsMouseButtonHeld = true;
        Assert.Equal(Decision(CornerOutcome.MouseButtonHeld), MoveTo(TopLeft));

        _environment.IsMouseButtonHeld = false;
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(CornerDecision.Nothing, _detector.Update());
    }

    [Fact]
    public void AButtonHeldWhenTheDelayEndsSkipsTheCorner()
    {
        MoveTo(TopLeft);
        _environment.IsMouseButtonHeld = true;
        _clock.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(Decision(CornerOutcome.MouseButtonHeld), _detector.Update());
    }

    [Theory]
    [InlineData(HotCornerKey.None, CornerOutcome.KeyNotHeld)]
    [InlineData(HotCornerKey.Shift, CornerOutcome.KeyNotHeld)]
    [InlineData(HotCornerKey.Control, CornerOutcome.Triggered)]
    public void ACornerThatRequiresAKeyTriggersOnlyWithIt(HotCornerKey held, CornerOutcome outcome)
    {
        var settings = new HotCornersSettings
        {
            TopLeft = new CornerSettings { Action = HotCornerAction.TaskView, Key = HotCornerKey.Control },
            ActivationDelayMilliseconds = 0,
        };
        _detector.Configure(settings, [Main]);
        _environment.HeldKey = held;

        Assert.Equal(Decision(outcome), MoveTo(TopLeft));
    }

    [Theory]
    [InlineData(true, CornerOutcome.FullScreenApp)]
    [InlineData(false, CornerOutcome.Triggered)]
    public void AFullScreenAppSkipsTheCornerUnlessTurnedOff(bool ignoreFullScreenApps, CornerOutcome outcome)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, IgnoreFullScreenApps = ignoreFullScreenApps }, [Main]);
        _environment.IsFullScreenAppInFront = true;

        Assert.Equal(Decision(outcome), MoveTo(TopLeft));
    }

    [Fact]
    public void ACornerThePointerIsInWhenConfiguredWaitsForThePointerToComeBack()
    {
        _environment.PointerPosition = TopLeft;
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);

        Assert.Equal(CornerDecision.Nothing, _detector.Update());
        MoveTo(Middle);
        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void ADecisionNamesTheCornerAndItsAction()
    {
        var settings = new HotCornersSettings
        {
            BottomRight = new CornerSettings { Action = HotCornerAction.Lock },
            ActivationDelayMilliseconds = 0,
        };
        _detector.Configure(settings, [Main]);

        Assert.Equal(new CornerDecision(CornerOutcome.Triggered, ScreenCorner.BottomRight, HotCornerAction.Lock, TimeSpan.Zero), MoveTo(new Point(1919, 1079)));
    }

    [Fact]
    public void EveryCornerOfOneDisplayTriggers()
    {
        Assert.Equal(
            [new(0, 0), new(1919, 0), new(0, 1079), new(1919, 1079)],
            CornersThatTrigger([Main]));
    }

    [Fact]
    public void DisplaysSideBySideShareNoCorners()
    {
        Assert.Equal(
            [new(0, 0), new(0, 1079), new(3839, 0), new(3839, 1079)],
            CornersThatTrigger([Main, Rectangle.FromLTRB(1920, 0, 3840, 1080)]));
    }

    [Fact]
    public void StackedDisplaysShareNoCorners()
    {
        Assert.Equal(
            [new(0, 1079), new(1919, 1079), new(0, -1080), new(1919, -1080)],
            CornersThatTrigger([Main, Rectangle.FromLTRB(0, -1080, 1920, 0)]));
    }

    [Fact]
    public void ALargerDisplayKeepsTheCornersThatStickOut()
    {
        // A 4K display to the right, reaching above and below the main one.
        Assert.Equal(
            [new(0, 0), new(0, 1079), new(1920, -200), new(5759, -200), new(1920, 1959), new(5759, 1959)],
            CornersThatTrigger([Main, Rectangle.FromLTRB(1920, -200, 5760, 1960)]));
    }

    [Fact]
    public void ADisplayLeftOfTheMainOneHasNegativePositions()
    {
        // Shorter than the main display, so the main display's bottom-left corner stays.
        Assert.Equal(
            [new(1919, 0), new(0, 1079), new(1919, 1079), new(-1280, 0), new(-1280, 1023)],
            CornersThatTrigger([Main, Rectangle.FromLTRB(-1280, 0, 0, 1024)]));
    }

    [Fact]
    public void DisplaysThatTouchOnlyAtACornerShareThatCorner()
    {
        Assert.Equal(
            [new(0, 0), new(1919, 0), new(0, 1079), new(3839, 1080), new(1920, 2159), new(3839, 2159)],
            CornersThatTrigger([Main, Rectangle.FromLTRB(1920, 1080, 3840, 2160)]));
    }

    private static CornerDecision Decision(CornerOutcome outcome) =>
        new(outcome, ScreenCorner.TopLeft, HotCornerAction.TaskView, TimeSpan.Zero);

    private static CornerDecision Waiting(TimeSpan wait) =>
        new(CornerOutcome.Waiting, ScreenCorner.TopLeft, HotCornerAction.TaskView, wait);

    private CornerDecision MoveTo(Point position)
    {
        _environment.PointerPosition = position;
        return _detector.Update();
    }

    private void TriggerTopLeft()
    {
        MoveTo(TopLeft);
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(Decision(CornerOutcome.Triggered), _detector.Update());
    }

    // Moves the pointer to each corner point of each display, in order, and returns the ones that trigger.
    private List<Point> CornersThatTrigger(Rectangle[] displays)
    {
        var settings = new HotCornersSettings
        {
            TopRight = new CornerSettings { Action = HotCornerAction.Start },
            BottomLeft = new CornerSettings { Action = HotCornerAction.Search },
            BottomRight = new CornerSettings { Action = HotCornerAction.ShowDesktop },
            ActivationDelayMilliseconds = 0,
        };
        _detector.Configure(settings, displays);

        return [.. displays
            .SelectMany(display => new Point[]
            {
                new(display.Left, display.Top),
                new(display.Right - 1, display.Top),
                new(display.Left, display.Bottom - 1),
                new(display.Right - 1, display.Bottom - 1),
            })
            .Where(point =>
            {
                MoveTo(Middle);
                return MoveTo(point).Outcome == CornerOutcome.Triggered;
            })];
    }

    private sealed class FakeEnvironment : IHotCornerEnvironment
    {
        public Point PointerPosition { get; set; }

        public bool IsMouseButtonHeld { get; set; }

        public bool IsFullScreenAppInFront { get; set; }

        public HotCornerKey HeldKey { get; set; }

        public bool IsKeyHeld(HotCornerKey key) => key == HeldKey;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
