using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class CornerDetectorTests
{
    private static readonly Rectangle Main = Rectangle.FromLTRB(0, 0, 1920, 1080);
    private static readonly Rectangle Second = Rectangle.FromLTRB(1920, 0, 3840, 1080);
    private static readonly Point Middle = new(960, 540);
    private static readonly Point TopLeft = new(0, 0);
    private static readonly Point SecondTopRight = new(3839, 0);

    // The desktop covers every display and belongs to the shell.
    private static readonly WindowPlace Desktop = Window(Rectangle.FromLTRB(0, 0, 3840, 1080), isShell: true);

    private static readonly ExcludedApp RemoteDesktop = new() { Name = "Remote Desktop Connection", Program = @"C:\Windows\System32\mstsc.exe" };
    private static readonly ExcludedApp Photos = new() { Name = "Photos", AppId = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" };

    private readonly FakeHotCornerEnvironment _environment = new() { PointerPosition = Middle };
    private readonly ManualTimeProvider _clock = new();
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
        Assert.Equal(Decision(CornerOutcome.LeftEarly), MoveTo(Middle));

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(CornerDecision.Nothing, _detector.Update());
    }

    [Fact]
    public void LeavingAfterTheCornerTriggeredOrWasSkippedIsntLeavingEarly()
    {
        TriggerTopLeft();
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));

        _environment.IsMouseButtonHeld = true;
        Assert.Equal(Decision(CornerOutcome.MouseButtonHeld), MoveTo(TopLeft));
        _environment.IsMouseButtonHeld = false;
        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));
    }

    [Fact]
    public void LeavingACornerThePointerWasInWhenConfiguredIsntLeavingEarly()
    {
        _environment.PointerPosition = TopLeft;
        _detector.Configure(new HotCornersSettings(), [Main]);

        Assert.Equal(CornerDecision.Nothing, MoveTo(Middle));
    }

    [Fact]
    public void JumpingStraightIntoAnotherCornerWaitsForItsDelay()
    {
        // A pen or touch can put the pointer anywhere at once.
        _detector.Configure(new HotCornersSettings { BottomRight = new CornerSettings { Action = HotCornerAction.Lock } }, [Main]);
        MoveTo(TopLeft);

        var decision = MoveTo(new Point(1919, 1079));

        Assert.Equal((CornerOutcome.Waiting, ScreenCorner.BottomRight), (decision.Outcome, decision.Corner));
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
    public void AnUnknownPointerPositionIsNoCorner()
    {
        MoveTo(TopLeft);
        Assert.Equal(CornerDecision.Nothing, MoveTo(null));

        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(CornerDecision.Nothing, _detector.Update());
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
    [InlineData(true)]
    [InlineData(false)]
    public void APointerConfinedToPartOfTheScreenSkipsTheCornerWhateverTheFullScreenSetting(bool ignoreFullScreenApps)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, IgnoreFullScreenApps = ignoreFullScreenApps }, [Main]);
        _environment.PointerClip = Rectangle.FromLTRB(0, 0, 800, 600);

        Assert.Equal(Decision(CornerOutcome.PointerConfined), MoveTo(TopLeft));
    }

    [Fact]
    public void APointerClipThatCoversEveryDisplayConfinesNothing()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main, Second]);
        _environment.PointerClip = Rectangle.FromLTRB(0, 0, 3840, 1080);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void APointerConfinedToOneOfTwoDisplaysSkipsItsCorners()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main, Second]);
        _environment.PointerClip = Main;

        Assert.Equal(Decision(CornerOutcome.PointerConfined), MoveTo(TopLeft));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APointerAnAppHidSkipsTheCornerWhateverTheFullScreenSetting(bool ignoreFullScreenApps)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, IgnoreFullScreenApps = ignoreFullScreenApps }, [Main]);
        _environment.PointerVisibility = PointerVisibility.Hidden;

        Assert.Equal(Decision(CornerOutcome.PointerHidden), MoveTo(TopLeft));
    }

    [Fact]
    public void APointerWindowsHidesForTouchDoesntSkipTheCorner()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        _environment.PointerVisibility = PointerVisibility.Suppressed;

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Theory]
    [InlineData(true, CornerOutcome.FullScreenApp)]
    [InlineData(false, CornerOutcome.Triggered)]
    public void ABorderlessWindowCoveringTheDisplaySkipsTheCornerUnlessTurnedOff(bool ignoreFullScreenApps, CornerOutcome outcome)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, IgnoreFullScreenApps = ignoreFullScreenApps }, [Main]);
        _environment.Windows.AddRange([Window(Main), Desktop]);

        Assert.Equal(Decision(outcome), MoveTo(TopLeft));
    }

    [Fact]
    public void AFullScreenAppSkipsOnlyTheCornersOfItsOwnDisplay()
    {
        ConfigureTwoDisplays();
        _environment.Windows.AddRange([Window(Second), Desktop]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
        MoveTo(Middle);
        Assert.Equal(CornerOutcome.FullScreenApp, MoveTo(SecondTopRight).Outcome);
    }

    [Fact]
    public void AFullScreenAppThatIsntInFrontStillSkipsTheCornersOfItsDisplay()
    {
        // A video on the second display while a window on the main display is in front.
        ConfigureTwoDisplays();
        _environment.Windows.AddRange([Window(Rectangle.FromLTRB(100, 100, 900, 700), hasFrame: true), Window(Second), Desktop]);

        Assert.Equal(CornerOutcome.FullScreenApp, MoveTo(SecondTopRight).Outcome);
    }

    [Theory]
    [InlineData("click-through")]
    [InlineData("no-activate")]
    [InlineData("tool window")]
    [InlineData("cloaked")]
    [InlineData("minimized")]
    public void OverlaysAndWindowsOutOfSightArePassedOver(string kind)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        var window = kind switch
        {
            "click-through" => Window(Main) with { IsClickThrough = true },
            "no-activate" => Window(Main) with { IsNoActivate = true },
            "tool window" => Window(Main) with { IsToolWindow = true },
            "cloaked" => Window(Main) with { IsCloaked = true },
            _ => Window(Main) with { IsMinimized = true },
        };
        _environment.Windows.AddRange([window, Desktop]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void AnAlwaysOnTopWindowThatDoesntCoverTheDisplayIsPassedOver()
    {
        // A picture-in-picture video in front of a full-screen game.
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        _environment.Windows.AddRange([Window(Rectangle.FromLTRB(1500, 700, 1900, 1000), isTopmost: true), Window(Main), Desktop]);

        Assert.Equal(Decision(CornerOutcome.FullScreenApp), MoveTo(TopLeft));
    }

    [Fact]
    public void AWindowInFrontOfAFullScreenAppDecides()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        _environment.Windows.AddRange([Window(Rectangle.FromLTRB(100, 100, 900, 700), hasFrame: true), Window(Main), Desktop]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void AMaximizedWindowWithItsFrameIsntFullScreen()
    {
        // With a taskbar that hides itself, a maximized window's frame reaches past the display.
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        _environment.Windows.AddRange([Window(Rectangle.FromLTRB(-8, -8, 1928, 1088), hasFrame: true), Desktop]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void TheShellsWindowsArentFullScreen()
    {
        // Task View, then the desktop.
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        _environment.Windows.AddRange([Window(Main, isShell: true), Desktop]);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void ExclusiveFullScreenCountsOnlyOnTheDisplayOfTheWindowInFront()
    {
        ConfigureTwoDisplays();
        _environment.FullScreenMode = FullScreenMode.Exclusive;
        _environment.FrontWindowDisplay = Second;

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
        MoveTo(Middle);
        Assert.Equal(CornerOutcome.FullScreenApp, MoveTo(SecondTopRight).Outcome);
    }

    [Fact]
    public void PresentationModeCountsOnEveryDisplay()
    {
        ConfigureTwoDisplays();
        _environment.FullScreenMode = FullScreenMode.Presentation;

        Assert.Equal(Decision(CornerOutcome.FullScreenApp), MoveTo(TopLeft));
        MoveTo(Middle);
        Assert.Equal(CornerOutcome.FullScreenApp, MoveTo(SecondTopRight).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AListedAppInFrontSkipsTheCornersOfEveryDisplayWhateverTheFullScreenSetting(bool ignoreFullScreenApps)
    {
        _detector.Configure(
            new HotCornersSettings
            {
                TopRight = new CornerSettings { Action = HotCornerAction.Start },
                ActivationDelayMilliseconds = 0,
                IgnoreFullScreenApps = ignoreFullScreenApps,
                ExcludedApps = [RemoteDesktop],
            },
            [Main, Second]);
        _environment.FrontApp = new AppOfWindow(null, null, @"C:\WINDOWS\system32\MSTSC.EXE", 1);

        Assert.Equal(Decision(CornerOutcome.AppExcluded), MoveTo(TopLeft));
        MoveTo(Middle);
        Assert.Equal(CornerOutcome.AppExcluded, MoveTo(SecondTopRight).Outcome);
    }

    [Theory]
    [InlineData("Microsoft.Windows.Photos_8wekyb3d8bbwe!App", null, CornerOutcome.AppExcluded)]
    [InlineData(null, "microsoft.windows.photos_8wekyb3d8bbwe!app", CornerOutcome.AppExcluded)]
    [InlineData("Chrome.Profile.1", null, CornerOutcome.Triggered)]
    public void AnAppIdMatchesWhetherTheWindowOrItsPackageHasIt(string? windowAppId, string? packageAppId, CornerOutcome outcome)
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, ExcludedApps = [Photos] }, [Main]);
        _environment.FrontApp = new AppOfWindow(windowAppId, packageAppId, @"C:\Windows\System32\ApplicationFrameHost.exe", 1);

        Assert.Equal(Decision(outcome), MoveTo(TopLeft));
    }

    [Fact]
    public void AnAppThatIsntListedDoesntSkipTheCorner()
    {
        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0, ExcludedApps = [RemoteDesktop, Photos] }, [Main]);
        _environment.FrontApp = new AppOfWindow(null, null, @"C:\Windows\System32\notepad.exe", 1);

        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
    }

    [Fact]
    public void TheAppInFrontIsReadOnlyWhenTheDelayEndsAndOnlyWhileAppsAreListed()
    {
        _environment.FrontApp = new AppOfWindow(null, null, @"C:\Windows\System32\mstsc.exe", 1);

        _detector.Configure(new HotCornersSettings { ActivationDelayMilliseconds = 0 }, [Main]);
        Assert.Equal(Decision(CornerOutcome.Triggered), MoveTo(TopLeft));
        Assert.Equal(0, _environment.AppReads);

        _detector.Configure(new HotCornersSettings { ExcludedApps = [RemoteDesktop] }, [Main]);
        MoveTo(Middle);
        MoveTo(TopLeft);
        Assert.Equal(0, _environment.AppReads);

        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(Decision(CornerOutcome.AppExcluded), _detector.Update());
        Assert.Equal(1, _environment.AppReads);
    }

    [Fact]
    public void WindowsAreReadOnlyWhenTheDelayEndsAndOnlyUntilOneDecides()
    {
        _environment.Windows.AddRange([Window(Main), Window(Rectangle.FromLTRB(100, 100, 900, 700), hasFrame: true), Desktop]);

        MoveTo(TopLeft);
        Assert.Equal(0, _environment.WindowSearches);

        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(Decision(CornerOutcome.FullScreenApp), _detector.Update());
        Assert.Equal((1, 1), (_environment.WindowSearches, _environment.WindowsRead));
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

    [Fact]
    public void TheCurrentCornerIsTheCornerWithAnActionThePointerIsIn()
    {
        Assert.Null(_detector.CurrentCorner);

        MoveTo(TopLeft);
        Assert.Equal(ScreenCorner.TopLeft, _detector.CurrentCorner);

        // The top-right corner has no action.
        MoveTo(new Point(1919, 0));
        Assert.Null(_detector.CurrentCorner);
    }

    [Fact]
    public void ConfiguringFindsTheCornerThePointerIsIn()
    {
        _environment.PointerPosition = TopLeft;

        _detector.Configure(new HotCornersSettings(), [Main]);
        Assert.Equal(ScreenCorner.TopLeft, _detector.CurrentCorner);

        _detector.Configure(new HotCornersSettings { TopLeft = new CornerSettings() }, [Main]);
        Assert.Null(_detector.CurrentCorner);
    }

    // A borderless window that isn't an overlay, the shell's or out of sight, unless the test says so.
    private static WindowPlace Window(Rectangle bounds, bool hasFrame = false, bool isTopmost = false, bool isShell = false) =>
        new(0, bounds, IsCloaked: false, IsMinimized: false, IsClickThrough: false, IsNoActivate: false, IsToolWindow: false, isTopmost, hasFrame, isShell);

    private static CornerDecision Decision(CornerOutcome outcome) =>
        new(outcome, ScreenCorner.TopLeft, HotCornerAction.TaskView, TimeSpan.Zero);

    // The main display and one to its right, with the top-right corner on Start.
    private void ConfigureTwoDisplays() =>
        _detector.Configure(
            new HotCornersSettings { TopRight = new CornerSettings { Action = HotCornerAction.Start }, ActivationDelayMilliseconds = 0 },
            [Main, Second]);

    private static CornerDecision Waiting(TimeSpan wait) =>
        new(CornerOutcome.Waiting, ScreenCorner.TopLeft, HotCornerAction.TaskView, wait);

    private CornerDecision MoveTo(Point? position)
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
}
