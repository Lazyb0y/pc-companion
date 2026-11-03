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

    private readonly FakeTitleBarWindows _windows = new();
    private readonly FakeTitleBarActionsSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<TitleBarController> _logger = new();
    private readonly TitleBarController _controller;

    public TitleBarControllerTests()
    {
        // A window whose title bar answers as Edge's does: tabs on the left, the empty strip on the right.
        _windows.Add(Window, new Rectangle(100, 100, 800, 600));
        _windows.Answers = (_, point) => point.Y < 140 && point.X >= 300 ? HitTestAnswer.Caption : HitTestAnswer.Elsewhere;
        _controller = new TitleBarController(_windows, _system, _clock, action => action(), _logger);
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
    public void StopRemovesTheClaimAndDropsTheClicksWaiting()
    {
        MoveTo(OnTitleBar);
        Click(5);

        _controller.Stop();
        _clock.Advance(TitleBarController.CheckDelay);

        Assert.Null(_system.Claimed);
        Assert.Empty(_system.Minimized);
    }

    private void MoveTo(Point point)
    {
        _windows.PointerPosition = point;
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.None, 0, 0));
    }

    private void Click(uint time) => _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.MiddleDown, 0, time));
}
