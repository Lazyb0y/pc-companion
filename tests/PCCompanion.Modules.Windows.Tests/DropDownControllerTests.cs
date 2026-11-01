using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownControllerTests : IDisposable
{
    // Another app's window, which has the front at first.
    private const nint Other = 9;
    private const string NotesProgram = @"C:\Apps\Notes.exe";

    // The work area's top half, across its width.
    private static readonly Rectangle TopHalf = new(0, 0, 1920, 516);

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeDropDownSystem _system;
    private readonly DropDownController _controller;
    private readonly List<DropDownReport> _reports = [];

    public DropDownControllerTests()
    {
        _system = new FakeDropDownSystem(_clock);
        _controller = new DropDownController(_system, _clock, _thread.Enqueue, new RecordingLogger<DropDownController>());
        _controller.Reported += (_, report) => _reports.Add(report);
        Apply(new DropDownAppsSettings
        {
            Enabled = true,
            Apps =
            [
                new DropDownApp { Name = "Notes", Program = NotesProgram, Shortcut = DropDownShortcut.WindowsControlAboveTab },
                new DropDownApp { Name = "Terminal", AppId = "Terminal.App", Shortcut = DropDownShortcut.WindowsControlAlt1 },
            ],
        });
        _system.Add(Other, processId: 99, program: @"C:\Apps\Other.exe");
        _system.ForegroundWindow = Other;
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void TheFirstPressSlidesTheAppsFrontMostWindowDownAndActivatesIt()
    {
        var front = _system.Add(1, program: NotesProgram);
        var behind = _system.Add(2, program: NotesProgram);

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(front.Bounds));
        Assert.True(front.Visible);
        Assert.False(front.Marked);
        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal(new Rectangle(300, 200, 800, 600), behind.Bounds);
        Assert.Equal([new DropDownReport(0, DropDownOutcome.Shown)], _reports);
        Assert.Equal(0, _system.Warnings);
        Assert.Contains(10u, _system.Watched);

        // Placed above the work area, then slid down one move per frame.
        Assert.Equal("place 1 -7,-516,1934,523", _system.Calls[0]);
        Assert.True(_system.Moves.Count > 5);
        Assert.Equal(_system.Moves.OrderBy(move => move.Y), _system.Moves);
    }

    [Fact]
    public void TheWindowTakesThePagesSizeCenteredAtTheTopOfTheWorkArea()
    {
        var window = _system.Add(1, program: NotesProgram);
        Apply(Settings() with { Width = 60, Height = 40 });

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(new Rectangle(384, 0, 1152, 413), FakeDropDownSystem.Borders.Inside(window.Bounds));
    }

    [Fact]
    public void PressingAgainSlidesItUpHidesItAndGivesTheFrontBack()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        _system.Moves.Clear();

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.False(window.Visible);
        Assert.True(window.Marked);
        Assert.Equal(Other, _system.ForegroundWindow);
        Assert.Contains($"front {Other}", _system.Calls);
        Assert.Equal(_system.Moves.OrderByDescending(move => move.Y), _system.Moves);
        Assert.True(window.Bounds.Bottom <= FakeDropDownSystem.Borders.Bottom);
        Assert.Equal(DropDownOutcome.Hidden, _reports[^1].Outcome);
    }

    [Fact]
    public void AHiddenWindowShowsAgainAtTheNextPress()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        Press(DropDownShortcut.WindowsControlAboveTab);

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(window.Visible);
        Assert.False(window.Marked);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal([DropDownOutcome.Shown, DropDownOutcome.Hidden, DropDownOutcome.Shown], _reports.Select(report => report.Outcome));
    }

    [Fact]
    public void WithAnotherWindowInFrontThePressBringsItToTheFront()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        _system.ForegroundWindow = Other;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(window.Visible);
        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal(DropDownOutcome.BroughtToFront, _reports[^1].Outcome);
    }

    [Fact]
    public void ADialogOfTheWindowInFrontCountsAsTheWindowInFront()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        _system.Add(3, program: NotesProgram).Owner = 1;
        _system.ForegroundWindow = 3;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.False(window.Visible);
        Assert.False(_system.Get(3).Visible);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithoutAnimationsOrTheSlideTheWindowShowsAndHidesAtOnce(bool animationsOn, bool slide)
    {
        var window = _system.Add(1, program: NotesProgram);
        _system.AnimationsOn = animationsOn;
        Apply(Settings() with { Slide = slide });

        Press(DropDownShortcut.WindowsControlAboveTab);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.False(window.Visible);
        Assert.Empty(_system.Moves);
        Assert.Equal("place 1 -7,0,1934,523", _system.Calls[0]);
    }

    [Fact]
    public void ADisplayAboveMeansNoSlide()
    {
        var window = _system.Add(1, program: NotesProgram);
        _system.OtherDisplays.Add(new Rectangle(0, -1080, 1920, 1080));

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Empty(_system.Moves);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
    }

    [Fact]
    public void AMinimizedWindowIsShownFromANormalPlaceAboveTheWorkArea()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Minimized = true;
        _system.PlacesAtOnce = false;

        _controller.Press(0);
        RunThread();

        // Hidden first; nothing more happens until Windows has placed it. Its invisible borders
        // aren't known while it's minimized, so they're measured once it shows.
        Assert.False(window.Visible);
        var request = Assert.Single(_system.PlacementRequests);
        Assert.Equal((new Rectangle(0, -516, 1920, 516), false), (request.Normal, request.Minimized));

        _system.AnswerPlacement();
        Settle();

        Assert.False(window.Minimized);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
        Assert.Equal(1, _system.ForegroundWindow);
    }

    [Fact]
    public void AMaximizedWindowIsHiddenThenPlacedSoItNeverShowsAtItsOldPlace()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Maximized = true;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.False(window.Maximized);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
        var hide = _system.Calls.IndexOf("hide 1");
        var placement = _system.Calls.FindIndex(call => call.StartsWith("placement 1", StringComparison.Ordinal));
        Assert.InRange(hide, 0, placement - 1);
    }

    [Fact]
    public void AStoreAppInTheSharedFrameDoesntDropDown()
    {
        var window = _system.Add(1, program: @"C:\Windows\System32\ApplicationFrameHost.exe");
        window.AppId = "Terminal.App";
        window.ClassName = "ApplicationFrameWindow";

        Press(DropDownShortcut.WindowsControlAlt1);

        AssertRefused(window, 1, DropDownOutcome.SharedAppFrame);
    }

    [Fact]
    public void AWindowWithADialogOpenDoesntDropDown()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Enabled = false;

        Press(DropDownShortcut.WindowsControlAboveTab);

        AssertRefused(window, 0, DropDownOutcome.DialogOpen);
    }

    [Fact]
    public void AnAppRunningAsAdministratorDoesntDropDown()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.RefusesChanges = true;

        Press(DropDownShortcut.WindowsControlAboveTab);

        AssertRefused(window, 0, DropDownOutcome.RunsAsAdministrator);
    }

    [Fact]
    public void AShownWindowWithADialogOpenStaysWhereItIs()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        window.Enabled = false;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(window.Visible);
        Assert.Equal(1, _system.Warnings);
        Assert.Equal(DropDownOutcome.DialogOpen, _reports[^1].Outcome);
    }

    [Fact]
    public void AWindowOnAnotherDesktopIsHiddenThenShownHereAMomentLater()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Cloaked = true;

        _controller.Press(0);
        RunThread();
        Assert.False(window.Visible);
        Assert.DoesNotContain("show 1", _system.Calls);

        Settle();

        Assert.True(window.Visible);
        Assert.False(window.Cloaked);
        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(window.Bounds));
        Assert.Equal([new DropDownReport(0, DropDownOutcome.BroughtFromOtherDesktop)], _reports);
    }

    [Fact]
    public void AWindowThatStaysOnTheOtherDesktopIsNotActivated()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Cloaked = true;
        window.StaysCloaked = true;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(Other, _system.ForegroundWindow);
        Assert.Equal(1, _system.Warnings);
        Assert.Equal(DropDownOutcome.StayedOnOtherDesktop, _reports[^1].Outcome);
    }

    [Fact]
    public void ADropDownWindowShownOnAnotherDesktopIsBroughtHere()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        window.Cloaked = true;
        _system.ForegroundWindow = Other;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(window.Visible);
        Assert.False(window.Cloaked);
        Assert.Equal(DropDownOutcome.BroughtFromOtherDesktop, _reports[^1].Outcome);
    }

    [Fact]
    public void OwnedWindowsHideFirstAndComeBackOnceTheSlideEnds()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        var palette = _system.Add(3, program: NotesProgram);
        palette.Owner = 1;

        Press(DropDownShortcut.WindowsControlAboveTab);
        Assert.False(palette.Visible);
        Assert.True(palette.Marked);
        Assert.True(_system.Calls.IndexOf("hide 3") < _system.Calls.IndexOf("hide 1"));

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(palette.Visible);
        Assert.False(palette.Marked);
        Assert.True(_system.Calls.LastIndexOf("show 3") > _system.Calls.LastIndexOf("show 1"));
    }

    [Fact]
    public void KeepOnTopPutsTheShownWindowOnTopAndTakesItOffWhenItHides()
    {
        var window = _system.Add(1, program: NotesProgram);
        Apply(Settings() with { KeepOnTop = true });

        Press(DropDownShortcut.WindowsControlAboveTab);
        Assert.True(window.Topmost);
        Assert.True(window.MarkedOnTop);

        Press(DropDownShortcut.WindowsControlAboveTab);
        Assert.False(window.Topmost);
        Assert.False(window.MarkedOnTop);
    }

    [Fact]
    public void AWindowOnTopByItsOwnChoiceIsLeftOnTop()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.Topmost = true;
        Apply(Settings() with { KeepOnTop = true });

        Press(DropDownShortcut.WindowsControlAboveTab);
        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.True(window.Topmost);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("topmost", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenTheWindowClosesTheNextPressTakesAnotherOfTheAppsWindows()
    {
        var first = _system.Add(1, program: NotesProgram);
        var second = _system.Add(2, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);

        first.Destroyed = true;
        _controller.OnDestroyed(1);
        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(second.Bounds));
        Assert.Equal(2, _system.ForegroundWindow);
    }

    [Fact]
    public void WindowsAreFoundByTheAppIdTheySetWhateverTheirProcess()
    {
        _system.Add(1, processId: 20, program: @"C:\Browser\browser.exe").AppId = "Browser.Profile1";
        var profile = _system.Add(2, processId: 21, appId: "Terminal.App", program: @"C:\Browser\browser.exe");

        Press(DropDownShortcut.WindowsControlAlt1);

        Assert.Equal(TopHalf, FakeDropDownSystem.Borders.Inside(profile.Bounds));
        Assert.Contains(21u, _system.Watched);
    }

    [Fact]
    public void ToolWindowsAndOwnedWindowsAreNotTaken()
    {
        _system.Add(1, program: NotesProgram).ToolWindow = true;
        _system.Add(2, program: NotesProgram).Owner = Other;

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Empty(_reports);
        Assert.All(_system.Windows, window => Assert.Equal(new Rectangle(300, 200, 800, 600), window.Bounds));
    }

    [Fact]
    public void APressIsForTheAppWithThatShortcutAndOnlyOneAppHasEach()
    {
        Apply(Settings() with
        {
            Apps =
            [
                new DropDownApp { Name = "Notes", Program = NotesProgram, Shortcut = (DropDownShortcut)99 },
                new DropDownApp { Name = "Terminal", AppId = "Terminal.App", Shortcut = DropDownShortcut.WindowsControlAlt2 },
                new DropDownApp { Name = "Mail", Program = @"C:\Apps\Mail.exe", Shortcut = DropDownShortcut.WindowsControlAlt2 },
            ],
        });
        var terminal = _system.Add(1, appId: "Terminal.App");

        Press(DropDownShortcut.WindowsControlAlt2);
        Press(DropDownShortcut.None);

        Assert.Equal([DropDownShortcut.None, DropDownShortcut.WindowsControlAlt2, DropDownShortcut.None], _controller.Shortcuts);
        Assert.Equal([new DropDownReport(1, DropDownOutcome.Shown)], _reports);
        Assert.True(terminal.Visible);
    }

    [Fact]
    public void ASizeTheUserDraggedIsKeptForTheApp()
    {
        var window = _system.Add(1, program: NotesProgram);
        Press(DropDownShortcut.WindowsControlAboveTab);
        window.Bounds = window.Bounds with { Width = 974, Height = 265 };

        Press(DropDownShortcut.WindowsControlAboveTab);
        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(new Rectangle(480, 0, 960, 258), FakeDropDownSystem.Borders.Inside(window.Bounds));
    }

    [Fact]
    public void AnAppThatKeepsALargerSizeIsCenteredFromThatSize()
    {
        var window = _system.Add(1, program: NotesProgram);
        window.MinimumSize = new Size(1014, 707);
        Apply(Settings() with { Width = 30, Height = 20 });

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal(new Rectangle(460, 0, 1000, 700), FakeDropDownSystem.Borders.Inside(window.Bounds));

        // Its own size isn't taken for a size the user dragged.
        Press(DropDownShortcut.WindowsControlAboveTab);
        Press(DropDownShortcut.WindowsControlAboveTab);
        Assert.Equal(new Rectangle(460, 0, 1000, 700), FakeDropDownSystem.Borders.Inside(window.Bounds));
    }

    [Fact]
    public void APressDuringTheSlideInEndsItAndHidesTheWindow()
    {
        var window = _system.Add(1, program: NotesProgram);
        _controller.Press(0);
        RunSteps(3);

        Press(DropDownShortcut.WindowsControlAboveTab);

        Assert.False(window.Visible);
        Assert.Equal([DropDownOutcome.Shown, DropDownOutcome.Hidden], _reports.Select(report => report.Outcome));
    }

    private static DropDownAppsSettings Settings() => new()
    {
        Enabled = true,
        Apps =
        [
            new DropDownApp { Name = "Notes", Program = NotesProgram, Shortcut = DropDownShortcut.WindowsControlAboveTab },
            new DropDownApp { Name = "Terminal", AppId = "Terminal.App", Shortcut = DropDownShortcut.WindowsControlAlt1 },
        ],
    };

    private void Apply(DropDownAppsSettings settings) => _controller.Apply(settings);

    private void AssertRefused(FakeDropWindow window, int index, DropDownOutcome outcome)
    {
        Assert.Equal([new DropDownReport(index, outcome)], _reports);
        Assert.Equal(1, _system.Warnings);
        Assert.True(window.Visible);
        Assert.False(window.Marked);
        Assert.Equal(new Rectangle(300, 200, 800, 600), window.Bounds);
        Assert.Equal(Other, _system.ForegroundWindow);
    }

    // A press, then everything it set off: the slide's frames and the checks after it.
    private void Press(DropDownShortcut shortcut)
    {
        _controller.Press(shortcut);
        Settle();
    }

    private void Settle()
    {
        for (var step = 0; step < 100; step++)
        {
            RunThread();
            _clock.Advance(TimeSpan.FromMilliseconds(16));
        }

        RunThread();
    }

    private void RunThread()
    {
        while (_thread.TryDequeue(out var action))
        {
            action();
        }
    }

    private void RunSteps(int count)
    {
        for (var step = 0; step < count && _thread.TryDequeue(out var action); step++)
        {
            action();
        }
    }
}
