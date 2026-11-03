using System;
using System.Drawing;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TitleBarControllerTests : IDisposable
{
    private const nint Window = 10;
    private const nint Other = 20;
    private static readonly Point OnTitleBar = new(400, 120);
    private static readonly Point OnTab = new(150, 120);
    private static readonly PointerTrigger MiddleClick = new(PointerButton.Middle, PointerKeys.None);
    private static readonly PointerTrigger Wheel = new(PointerButton.Wheel, PointerKeys.None);
    private static readonly ListedApp Listed = new() { Name = "App", Program = @"C:\Apps\10.exe" };

    private readonly FakeTitleBarWindows _windows = new();
    private readonly FakeTitleBarActionsSystem _system = new();
    private readonly FakeWindowOpacitySystem _opacity = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<TitleBarController> _logger = new();
    private readonly TitleBarController _controller;

    public TitleBarControllerTests()
    {
        // A window whose title bar answers as Edge's does: tabs on the left, the empty strip on the right.
        _windows.Add(Window, new Rectangle(100, 100, 800, 600));
        _windows.Answers = (_, point) => point.Y < 140 && point.X >= 300 ? HitTestAnswer.Caption : HitTestAnswer.Elsewhere;
        var opacity = new WindowOpacity(_opacity, action => action(), action => action(), exception => throw exception);
        _controller = new TitleBarController(_windows, _system, opacity, _clock, action => action(), _logger);
        _controller.Configure(new TitleBarActionsSettings { Enabled = true });
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void ThePointerRestingOnATitleBarClaimsThatPointForTheClicksThatHaveAnAction()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, ControlMiddleClick = TitleBarClickAction.SendToBack });

        MoveTo(OnTitleBar);

        var claim = Assert.IsType<PointerClaim>(_system.Claimed);
        Assert.Equal(new Rectangle(398, 118, 5, 5), claim.Area);
        Assert.Equal([MiddleClick, new PointerTrigger(PointerButton.Middle, PointerKeys.Control)], claim.Triggers.OrderBy(trigger => trigger.Keys));
        Assert.Equal(Window, _controller.ClaimedWindow);
    }

    [Fact]
    public void LeavingTheTitleBarRemovesTheClaim()
    {
        MoveTo(OnTitleBar);
        MoveTo(OnTab);

        Assert.Null(_system.Claimed);
        Assert.Equal(0, _controller.ClaimedWindow);
    }

    [Fact]
    public void AMomentAfterThePointerStopsTheClaimFollowsItsPosition()
    {
        // The window answered for where the pointer was, before its position caught up.
        MoveTo(new Point(420, 120));
        _windows.PointerPosition = OnTitleBar;

        _clock.Advance(TitleBarController.SettleDelay);

        Assert.Equal(new Rectangle(398, 118, 5, 5), _system.Claimed?.Area);
    }

    [Fact]
    public void ClaimingAndRemovingTheClaimAreLoggedOnceEach()
    {
        MoveTo(OnTitleBar);
        MoveTo(new Point(410, 120));
        MoveTo(OnTab);

        Assert.Single(_logger.Entries, entry => entry.Message.Contains("claimed the title bar", StringComparison.Ordinal));
        Assert.Single(_logger.Entries, entry => entry.Message.Contains("removed its claim", StringComparison.Ordinal));
    }

    [Fact]
    public void AClickOnAWindowThatRefusesTheQuestionIsLogged()
    {
        _windows.Refuses = true;
        MoveTo(OnTitleBar);

        Click(1);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_system.Minimized);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("got no action: Refused", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutActionsNothingIsClaimed()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = TitleBarClickAction.None });

        MoveTo(OnTitleBar);

        Assert.Empty(_system.Claims);
    }

    [Fact]
    public void AKeptMiddleClickMinimizesTheClaimedWindow()
    {
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Equal([Window], _system.Minimized);
        Assert.Equal(0, _system.Sounds);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("Minimize (Kept)", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowWithoutAMinimizeButtonIsntMinimizedAndTheWarningSoundPlays()
    {
        _system.WithoutMinimizeButton.Add(Window);
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Empty(_system.Minimized);
        Assert.Equal(1, _system.Sounds);
    }

    [Fact]
    public void SendToBackGivesTheFrontToTheNextWindowOnlyWhenTheWindowHadIt()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = TitleBarClickAction.SendToBack });
        _system.Behind[Window] = Other;
        MoveTo(OnTitleBar);

        _system.ForegroundWindow = Other;
        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));
        Assert.Empty(_system.BroughtToFront);

        _system.ForegroundWindow = Window;
        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 2));
        Assert.Equal([Window, Window], _system.SentToBack);
        Assert.Equal([Other], _system.BroughtToFront);
    }

    [Fact]
    public void AnAlwaysOnTopWindowIsntSentToTheBack()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = TitleBarClickAction.SendToBack });
        _system.Topmost.Add(Window);
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Empty(_system.SentToBack);
        Assert.Equal(1, _system.Sounds);
    }

    [Fact]
    public void MinimizeToTrayAndHideAppPassTheWindowOnToTheirFeatures()
    {
        _controller.Configure(new TitleBarActionsSettings
        {
            Enabled = true,
            ShiftMiddleClick = TitleBarClickAction.MinimizeToTray,
            ControlMiddleClick = TitleBarClickAction.HideApp,
        });
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(new PointerTrigger(PointerButton.Middle, PointerKeys.Shift), OnTitleBar, 0, 1));
        _controller.OnKept(new KeptPointerInputEventArgs(new PointerTrigger(PointerButton.Middle, PointerKeys.Control), OnTitleBar, 0, 2));

        Assert.Equal(3, _system.Claimed!.Triggers.Count);
        Assert.Equal([Window], _system.SentToTray);
        Assert.Equal([Window], _system.AppsHidden);
        Assert.Empty(_system.Minimized);
        Assert.Equal(0, _system.Sounds);
    }

    [Theory]
    [InlineData(TitleBarClickAction.MinimizeToTray)]
    [InlineData(TitleBarClickAction.HideApp)]
    public void WhileTheirFeatureIsOffTheClickIsStillKeptAndTheWarningSoundPlays(TitleBarClickAction action)
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = action });
        (_system.TrayRuns, _system.HideAppsRuns) = (false, false);
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Contains(MiddleClick, _system.Claimed!.Triggers);
        Assert.Equal(1, _system.Sounds);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("FeatureOff", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(TitleBarClickAction.MinimizeToTray)]
    [InlineData(TitleBarClickAction.HideApp)]
    public void AWindowWithoutAMinimizeButtonIsntPassedOn(TitleBarClickAction action)
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = action });
        _system.WithoutMinimizeButton.Add(Window);
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Empty(_system.SentToTray);
        Assert.Empty(_system.AppsHidden);
        Assert.Equal(1, _system.Sounds);
    }

    [Fact]
    public void TheKeysChooseTheAction()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, ShiftMiddleClick = TitleBarClickAction.SendToBack });
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(new PointerTrigger(PointerButton.Middle, PointerKeys.Shift), OnTitleBar, 0, 1));

        Assert.Equal([Window], _system.SentToBack);
        Assert.Empty(_system.Minimized);
    }

    [Fact]
    public void AKeptClickWhenAnotherWindowIsThereRunsNothing()
    {
        MoveTo(OnTitleBar);
        _windows.Add(Other, new Rectangle(350, 100, 200, 200));

        _controller.OnKept(new KeptPointerInputEventArgs(MiddleClick, OnTitleBar, 0, 1));

        Assert.Empty(_system.Minimized);
    }

    [Fact]
    public void AClickLetThroughStillGetsItsActionOnceTheWindowAnswers()
    {
        MoveTo(OnTitleBar);
        _system.Outcomes[5] = PointerInputOutcome.LetThrough;

        Click(5);
        Assert.Empty(_system.Minimized);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Equal([Window], _system.Minimized);
        Assert.Equal(0, _system.HookSetAgain);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("Minimize (LetThrough)", StringComparison.Ordinal));
    }

    [Fact]
    public void AClickTheHookKeptIsntActedOnAgain()
    {
        MoveTo(OnTitleBar);
        _system.Outcomes[5] = PointerInputOutcome.Kept;

        Click(5);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_system.Minimized);
    }

    [Fact]
    public void AClickTheHookNeverSawWhileClaimedSetsItAgainWithAWarning()
    {
        MoveTo(OnTitleBar);

        Click(5);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Equal(1, _system.HookSetAgain);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("removed the pointer hook", StringComparison.Ordinal));
        Assert.Equal([Window], _system.Minimized);
    }

    [Fact]
    public void AClickBeforeTheWindowAnsweredGetsItsActionWithoutSettingTheHookAgain()
    {
        _windows.AnswersLater = true;
        MoveTo(OnTitleBar);
        _windows.AnswersLater = false;

        Click(5);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Equal(0, _system.HookSetAgain);
        Assert.Equal([Window], _system.Minimized);
    }

    [Fact]
    public void AClickOnATabGetsNoAction()
    {
        MoveTo(OnTab);

        Click(5);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_system.Minimized);
        Assert.Equal(0, _system.Sounds);
    }

    [Fact]
    public void AClickWithKeysThatHaveNoActionIsntChecked()
    {
        MoveTo(OnTitleBar);
        _system.HeldKeys = PointerKeys.Alt;
        _windows.Asked.Clear();

        Click(5);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_windows.Asked);
        Assert.Empty(_system.Minimized);
    }

    [Fact]
    public void AListedAppsTitleBarIsntClaimedAndAClickThereIsLoggedWithoutAnAction()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, ExcludedApps = [Listed] });
        MoveTo(OnTitleBar);
        Assert.Empty(_system.Claims);

        Click(1);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_system.Minimized);
        Assert.Equal(0, _system.Sounds);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("AppListed", StringComparison.Ordinal));
    }

    [Fact]
    public void ListingTheAppWhoseTitleBarIsClaimedRemovesTheClaim()
    {
        MoveTo(OnTitleBar);
        Assert.NotNull(_system.Claimed);

        _controller.Configure(new TitleBarActionsSettings { Enabled = true, ExcludedApps = [Listed] });
        MoveTo(OnTitleBar);

        Assert.Null(_system.Claimed);
        Assert.Equal(0, _controller.ClaimedWindow);
    }

    [Fact]
    public void AClickNearTheTopOfAWindowThatGetsNothingIsLogged()
    {
        _windows.NotCandidates.Add(Window);
        MoveTo(OnTitleBar);

        Click(1);
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Empty(_system.Minimized);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("WindowLeftOut", StringComparison.Ordinal));
    }

    [Fact]
    public void StopRemovesTheClaimAndDropsTheClicksWaiting()
    {
        MoveTo(OnTitleBar);
        Click(5);

        _controller.Stop();
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Null(_system.Claimed);
        Assert.Empty(_system.Minimized);
    }

    [Fact]
    public void TheWheelIsClaimedWhenItChangesOpacity()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, MiddleClick = TitleBarClickAction.None, ControlWheel = TitleBarWheelAction.Opacity });

        MoveTo(OnTitleBar);

        Assert.Equal([new PointerTrigger(PointerButton.Wheel, PointerKeys.Control)], Assert.IsType<PointerClaim>(_system.Claimed).Triggers);
    }

    [Fact]
    public void AKeptNotchChangesTheWindowsOpacity()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, Wheel = TitleBarWheelAction.Opacity });
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -120, 1));
        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -120, 2));

        Assert.Equal(80, _opacity.OpacityOf(Window));
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("opacity to 80% (Kept)", StringComparison.Ordinal));
    }

    [Fact]
    public void SmallerStepsAddUpToANotch()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, Wheel = TitleBarWheelAction.Opacity });
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -40, 1));
        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -40, 2));
        Assert.Equal(100, _opacity.OpacityOf(Window));

        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -40, 3));
        Assert.Equal(90, _opacity.OpacityOf(Window));
    }

    [Fact]
    public void ANotchLetThroughStillChangesTheOpacity()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, Wheel = TitleBarWheelAction.Opacity });
        MoveTo(OnTitleBar);
        _system.Outcomes[5] = PointerInputOutcome.LetThrough;

        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.Wheel, -120, 5));
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Equal(90, _opacity.OpacityOf(Window));
    }

    [Fact]
    public void AWindowThatDrawsItselfPlaysTheWarningSoundOnce()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, Wheel = TitleBarWheelAction.Opacity });
        _opacity.Layered.Add(Window);
        _opacity.DrawItself.Add(Window);
        MoveTo(OnTitleBar);

        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -120, 1));
        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -120, 2));

        Assert.Equal(1, _system.Sounds);
        Assert.Empty(_opacity.Broken);
    }

    [Fact]
    public void StoppingPutsTheWindowsBack()
    {
        _controller.Configure(new TitleBarActionsSettings { Enabled = true, Wheel = TitleBarWheelAction.Opacity });
        MoveTo(OnTitleBar);
        _controller.OnKept(new KeptPointerInputEventArgs(Wheel, OnTitleBar, -360, 1));

        Assert.Equal(1, _controller.Stop());

        Assert.Equal(100, _opacity.OpacityOf(Window));
        Assert.Empty(_opacity.Marks);
    }

    private void MoveTo(Point point)
    {
        _windows.PointerPosition = point;
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.None, 0, 0));
    }

    private void Click(uint time) => _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.MiddleDown, 0, time));
}
