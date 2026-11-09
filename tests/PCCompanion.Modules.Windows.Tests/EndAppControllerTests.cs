using System;
using System.Drawing;
using System.Globalization;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class EndAppControllerTests : IDisposable
{
    private const string App = @"C:\Apps\App.exe";
    private const uint Escape = 0x1B;
    private const uint Enter = 0x0D;
    private const uint Tab = 0x09;

    private static readonly Rectangle Left = new(0, 0, 400, 300);
    private static readonly Rectangle Right = new(500, 0, 400, 300);
    private static readonly Point InLeft = new(100, 100);
    private static readonly Point InRight = new(600, 100);

    private readonly FakeEndAppSystem _system = new();
    private readonly FakeEndAppView _view = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<EndAppController> _logger = new();
    private readonly EndAppController _endApp;

    public EndAppControllerTests() => _endApp = new EndAppController(_system, _view, _view, _clock, action => action(), _logger);

    public void Dispose() => _endApp.Dispose();

    [Fact]
    public void TheShortcutCoversTheScreenAndFramesTheWindowInFront()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right);
        _system.ForegroundWindow = 2;

        _endApp.Start(EndAppSource.Shortcut);

        Assert.Equal(EndAppStage.Choosing, _endApp.Stage);
        Assert.True(_view.IsCovering);
        Assert.Equal((Right, 4, true), _view.Frame);
        Assert.True(_view.CanEndPointer);
    }

    [Fact]
    public void AMoveWhereThePointerRestsAsTheCoveringWindowShowsKeepsTheFrameOnTheWindowInFront()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right);
        _system.ForegroundWindow = 1;
        _system.PointerPosition = InRight;
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnPointerMoved(InRight);
        Assert.Equal(1, _endApp.Framed.Window);

        _endApp.OnPointerMoved(InRight with { X = InRight.X + 1 });
        Assert.Equal(2, _endApp.Framed.Window);
    }

    [Fact]
    public void AMoveWhereThePointerRestsAfterTabKeepsTheWindowTabReached()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right);
        _system.ForegroundWindow = 1;
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnPointerMoved(InLeft);

        _endApp.OnKey(Tab, shift: false);
        _endApp.OnPointerMoved(InLeft);

        Assert.Equal(2, _endApp.Framed.Window);
    }

    [Fact]
    public void TheMenuStartsOnTheFirstWindowTheTaskbarShows()
    {
        _system.AddWindow(1, 10, Left, taskbar: false);
        _system.AddWindow(2, 20, Right);
        _system.ForegroundWindow = 1;

        _endApp.Start(EndAppSource.Menu);

        Assert.Equal(2, _endApp.Framed.Window);
    }

    [Fact]
    public void WithoutTheFrontChoosingDoesntStart()
    {
        _system.AddWindow(1, 10, Left);
        _view.TakesFront = false;

        _endApp.Start(EndAppSource.Shortcut);

        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
        Assert.False(_view.IsCovering);
    }

    [Fact]
    public void TheFrameFollowsThePointerAndItsWidthTheDisplayScale()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right);
        _system.Dpi = 144;
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnPointerMoved(InRight);

        Assert.Equal((Right, 6, true), _view.Frame);
        _endApp.OnPointerMoved(new Point(450, 100));
        Assert.Null(_view.Frame);
        Assert.False(_view.CanEndPointer);
    }

    [Fact]
    public void AnAppThatCantBeEndedGetsTheGrayFrameAndTheUnavailablePointer()
    {
        _system.AddWindow(1, 50, Left, @"C:\Windows\explorer.exe");
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnPointerMoved(InLeft);

        Assert.Equal((Left, 4, false), _view.Frame);
        Assert.False(_view.CanEndPointer);
    }

    [Fact]
    public void TabAndShiftTabGoRoundTheWindowsTheTaskbarShows()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right);
        _system.AddWindow(3, 30, new Rectangle(0, 400, 400, 300));
        _system.ForegroundWindow = 1;
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnKey(Tab, shift: false);
        Assert.Equal(2, _endApp.Framed.Window);
        _endApp.OnKey(Tab, shift: false);
        _endApp.OnKey(Tab, shift: false);
        Assert.Equal(1, _endApp.Framed.Window);
        _endApp.OnKey(Tab, shift: true);
        Assert.Equal(3, _endApp.Framed.Window);
    }

    [Fact]
    public void TheTitleNamesTheFramedAppAndItsState()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Right, @"C:\Apps\Frozen.exe");
        _system.NotResponding.Add(2);
        _system.ForegroundWindow = 1;

        _endApp.Start(EndAppSource.Shortcut);
        Assert.Equal(Strings.EndAppChoosing + " " + Format(Strings.EndAppFramedResponding, "App"), _view.Titles[^1]);

        _endApp.OnPointerMoved(InRight);
        Assert.Equal(Strings.EndAppChoosing + " " + Format(Strings.EndAppFramedNotResponding, "Frozen"), _view.Titles[^1]);
    }

    [Fact]
    public void EscARightClickAndTheShortcutAgainStopChoosing()
    {
        _system.AddWindow(1, 10, Left);

        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnKey(Escape, shift: false);
        AssertStopped();

        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: true);
        AssertStopped();

        _endApp.Start(EndAppSource.Shortcut);
        _endApp.Start(EndAppSource.Shortcut);
        AssertStopped();
    }

    [Fact]
    public void LosingTheFrontAndAChangeOfDisplaysStopChoosing()
    {
        _system.AddWindow(1, 10, Left);

        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnDeactivated();
        AssertStopped();

        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnDisplaysChanged();
        AssertStopped();
    }

    [Fact]
    public void ThirtySecondsWithoutInputStopChoosing()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);

        _clock.Advance(TimeSpan.FromSeconds(20));
        _endApp.OnPointerMoved(InLeft);
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(EndAppStage.Choosing, _endApp.Stage);

        _clock.Advance(TimeSpan.FromSeconds(11));
        AssertStopped();
    }

    [Fact]
    public void AnAppThatStoppedRespondingIsAskedAboutWithEndAsTheDefault()
    {
        _system.AddWindow(1, 10, Left);
        _system.NotResponding.Add(1);
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnClicked(InLeft, right: false);

        Assert.Equal(EndAppStage.Asking, _endApp.Stage);
        var page = _view.Question.Page;
        Assert.Equal(Format(Strings.EndAppQuestionHeading, "App"), page.Heading);
        Assert.Equal(Format(Strings.EndAppQuestionNotResponding, "App"), page.Text);
        Assert.Equal([Strings.EndAppQuestionEnd, Strings.EndAppQuestionCancel], page.Buttons);
        Assert.Equal(0, page.DefaultButton);
        Assert.Equal(Format(Strings.EndAppQuestionProgram, "App.exe") + " " + PluralStrings.Format("EndAppQuestionProcesses", 1), page.Footer);
        Assert.Contains(10u, _system.Opened);
    }

    [Fact]
    public void ARespondingAppIsAskedAboutWithCancelAsTheDefault()
    {
        _system.AddWindow(1, 10, Left);
        _system.ForegroundWindow = 1;
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnKey(Enter, shift: false);

        Assert.Equal(Format(Strings.EndAppQuestionResponding, "App"), _view.Question.Page.Text);
        Assert.Equal(1, _view.Question.Page.DefaultButton);
    }

    [Fact]
    public void EndingEndsTheAppAndItsHelpersOfTheSameProgramOnly()
    {
        var later = FakeEndAppSystem.Started.AddSeconds(1);
        _system.AddWindow(1, 10, Left);
        _system.AddProcess(11, App, parent: 10, start: later);
        _system.AddProcess(12, @"C:\Apps\Other.exe", parent: 10, start: later);
        _system.AddProcess(13, App, parent: 99, start: later);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);
        Assert.Equal(Format(Strings.EndAppQuestionProgram, "App.exe") + " " + PluralStrings.Format("EndAppQuestionProcesses", 2), _view.Question.Page.Footer);

        _view.Question.Answer(0);

        Assert.Equal([10u, 11u], _system.Ended);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
        Assert.False(_view.IsCovering);
        Assert.Empty(_view.Notifications);

        // Once after checking it could be ended, and once after ending it.
        Assert.Equal(2, _system.Processes[10].Disposals);
    }

    [Fact]
    public void CancelEndsNothing()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _view.Question.Answer(1);

        Assert.Empty(_system.Ended);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);

        // Once after checking it could be ended, and once as the question was canceled.
        Assert.Equal(2, _system.Processes[10].Disposals);
    }

    [Fact]
    public void TheQuestionFollowsTheAppsState()
    {
        _system.AddWindow(1, 10, Left);
        _system.NotResponding.Add(1);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _system.NotResponding.Remove(1);
        _clock.Advance(EndAppController.WatchInterval);

        Assert.Equal(1, _view.Question.Changes);
        Assert.Equal(Format(Strings.EndAppQuestionResponding, "App"), _view.Question.Page.Text);
        Assert.Equal(1, _view.Question.Page.DefaultButton);

        _clock.Advance(EndAppController.WatchInterval);
        Assert.Equal(1, _view.Question.Changes);
    }

    [Fact]
    public void AnAppThatExitsWhileAskedClosesTheQuestion()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _system.Processes[10].HasExited = true;
        _clock.Advance(EndAppController.WatchInterval);

        Assert.True(_view.Question.Closed);
        Assert.Empty(_system.Ended);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
    }

    [Fact]
    public void FileExplorerIsExplainedWithTaskManager()
    {
        _system.AddWindow(1, 50, Left, @"C:\Windows\explorer.exe");
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        var page = _view.Question.Page;
        Assert.Equal(Format(Strings.EndAppCantEndHeading, "explorer"), page.Heading);
        Assert.Equal(Strings.EndAppCantEndFileExplorer, page.Text);
        Assert.Equal([Strings.EndAppOpenTaskManager, Strings.EndAppClose], page.Buttons);
        Assert.Equal(1, page.DefaultButton);

        _view.Question.Answer(0);

        Assert.Equal(1, _view.TaskManagerOpened);
        Assert.Empty(_system.Ended);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
    }

    [Fact]
    public void AnAppRunningAsAdministratorIsExplained()
    {
        _system.AddWindow(1, 10, Left);
        _system.Refused.Add(10);
        _system.Elevated.Add(10);
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnClicked(InLeft, right: false);

        Assert.Equal(Format(Strings.EndAppCantEndAdministrator, "App"), _view.Question.Page.Text);
        _view.Question.Answer(1);
        Assert.Equal(0, _view.TaskManagerOpened);
    }

    [Fact]
    public void AProcessWindowsRefusesWhenChosenIsExplained()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _system.Refused.Add(10);

        _endApp.OnClicked(InLeft, right: false);

        Assert.Equal(Format(Strings.EndAppCantEndNotAllowed, "App"), _view.Question.Page.Text);
    }

    [Fact]
    public void AStandInForSeveralWindowsCantTellWhich()
    {
        _system.AddWindow(1, 90, Left, @"C:\Windows\System32\dwm.exe", className: "Ghost");
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnClicked(InLeft, right: false);

        Assert.Equal(Strings.EndAppCantEndThisApp, _view.Question.Page.Heading);
        Assert.Equal(Strings.EndAppCantEndCantTell, _view.Question.Page.Text);
    }

    [Fact]
    public void AStandInsAppIsEndedThroughTheWindowItStandsFor()
    {
        _system.AddWindow(1, 90, Left, @"C:\Windows\System32\dwm.exe", className: "Ghost");
        _system.HiddenWindows.Add((2, 20, Left));
        _system.AddProcess(20, @"C:\Apps\Frozen.exe");
        _system.NotResponding.Add(2);
        _system.ForegroundWindow = 1;
        _endApp.Start(EndAppSource.Shortcut);

        _endApp.OnKey(Enter, shift: false);
        Assert.Equal(0, _view.Question.Page.DefaultButton);
        _view.Question.Answer(0);

        Assert.Equal([20u], _system.Ended);
    }

    [Fact]
    public void AnAppStillRunningAfterEndingIsNotified()
    {
        _system.AddWindow(1, 10, Left);
        _system.Processes[10].Lingers = true;
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _view.Question.Answer(0);

        Assert.Equal([(Strings.EndAppName, Format(Strings.EndAppNotEnded, "App"))], _view.Notifications);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
    }

    [Fact]
    public void TheShortcutWhileAskedDoesNothing()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _endApp.Start(EndAppSource.Shortcut);

        Assert.Equal(EndAppStage.Asking, _endApp.Stage);
        Assert.Equal(1, _view.CoveringShown);
    }

    [Fact]
    public void TheCoveringWindowGoesOnceTheQuestionTakesTheFront()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);
        Assert.True(_view.IsCovering);

        _endApp.OnDeactivated();

        Assert.False(_view.IsCovering);
        Assert.Null(_view.Frame);
        Assert.Equal(EndAppStage.Asking, _endApp.Stage);
    }

    [Fact]
    public void DisposingClosesTheQuestion()
    {
        _system.AddWindow(1, 10, Left);
        _endApp.Start(EndAppSource.Shortcut);
        _endApp.OnClicked(InLeft, right: false);

        _endApp.Dispose();

        Assert.True(_view.Question.Closed);
        Assert.Empty(_system.Ended);
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
    }

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);

    private void AssertStopped()
    {
        Assert.Equal(EndAppStage.Idle, _endApp.Stage);
        Assert.False(_view.IsCovering);
        Assert.Null(_view.Frame);
    }
}
