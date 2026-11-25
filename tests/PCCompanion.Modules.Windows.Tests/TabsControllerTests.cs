using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TabsControllerTests : IDisposable
{
    private const nint Notes = 1;
    private const nint Mail = 2;
    private const nint Report = 3;
    private const nint Palette = 4;
    private const nint Other = 9;
    private static readonly Rectangle NotesFrame = new(300, 200, 1000, 600);

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeTabsSystem _system;
    private readonly List<FakeTabStrip> _strips = [];
    private readonly FakeDropHighlight _highlight = new();
    private readonly WindowTabsWindows _shared = new();
    private readonly RecordingLogger<TabsController> _logger = new();
    private readonly TabsController _controller;
    private int _settingsOpened;

    public TabsControllerTests()
    {
        _system = new FakeTabsSystem(_thread.Enqueue);
        _controller = new TabsController(
            _system,
            () =>
            {
                var strip = new FakeTabStrip();
                _strips.Add(strip);
                return strip;
            },
            _highlight,
            _clock,
            _thread.Enqueue,
            _logger,
            _shared,
            rightToLeft: false,
            (index, count) => $"Tab {index} of {count}",
            () => _settingsOpened++);
        _system.Controller = _controller;
        _controller.Apply(new WindowTabsSettings { Enabled = true });
        _system.Add(Other, new Rectangle(50, 700, 400, 300));
        _system.Add(Report, new Rectangle(150, 150, 700, 450));
        _system.Add(Mail, new Rectangle(100, 100, 800, 500));
        _system.Add(Notes, NotesFrame);
        _system.ForegroundWindow = Notes;
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void DroppingAWindowWithCtrlOnAnothersTitleBarMakesAGroup()
    {
        Group(Notes, Mail);

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.Equal([Mail], _controller.Fronts);
        Assert.Equal(NotesFrame, _system[Mail].Visible);
        Assert.False(_system[Notes].Shown);
        Assert.False(_system.Marks[Notes]);
        Assert.Equal(Mail, _system.ForegroundWindow);
        Assert.True(_shared.Contains(Notes) && _shared.Contains(Mail));
        var strip = Assert.Single(_strips);
        Assert.True(strip.Shown);
        Assert.Equal(Mail, strip.Above);
        Assert.Equal(new Rectangle(300, 168, 400, 32), strip.View!.Place.Bounds);
    }

    [Fact]
    public void TheOldTabHidesOnlyTwoFramesAfterTheNewOneShows()
    {
        _controller.OnMoveSizeStart(Mail);
        Drag(Mail, TitleOf(Notes), control: true);
        _controller.OnMoveSizeEnd(Mail);
        RunThreadOnly();

        Assert.True(_system[Mail].Shown);
        Assert.True(_system[Notes].Shown);

        Run();

        Assert.False(_system[Notes].Shown);
    }

    [Fact]
    public void WithoutCtrlOrWithDroppingOffADroppedWindowStaysAlone()
    {
        DropOn(Mail, TitleOf(Notes), control: false);
        _controller.Apply(new WindowTabsSettings { Enabled = true, GroupByDropping = false });
        DropOn(Report, TitleOf(Notes), control: true);

        Assert.Empty(_controller.Groups);
        Assert.Empty(_strips);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void ResizingIsNoDrop()
    {
        _controller.OnMoveSizeStart(Mail);
        _system.PointerPosition = TitleOf(Notes);
        _system.IsControlHeld = true;
        _system[Mail].Visible = _system[Mail].Visible with { Width = 500 };
        _controller.OnLocationChange(Mail);
        _controller.OnMoveSizeEnd(Mail);
        Run();

        Assert.Empty(_controller.Groups);
    }

    [Fact]
    public void ADropOnAStripNeedsNoKeyAndGoesWhereItWasDropped()
    {
        Group(Notes, Mail);
        var place = _strips[0].View!.Place;

        DropOn(Report, new Point(place.Bounds.Left + 10, place.Bounds.Top + 10), control: false);

        Assert.Equal([[Report, Notes, Mail]], _controller.Groups);
        Assert.Equal([Report], _controller.Fronts);
        Assert.False(_system[Mail].Shown);
        Assert.Null(_highlight.Area);
    }

    [Fact]
    public void WhileDraggingWithCtrlTheWindowsTopIsHighlighted()
    {
        _controller.OnMoveSizeStart(Mail);
        Drag(Mail, TitleOf(Notes), control: true);

        Assert.Equal(new Rectangle(300, 200, 1000, 48), _highlight.Area);

        Drag(Mail, new Point(800, 600), control: true);

        Assert.Null(_highlight.Area);
    }

    [Fact]
    public void AWindowThatCantBeATabIsRefusedWithTheWarningSound()
    {
        _system[Mail].Refusal = TabRefusal.RunsAsAdministrator;
        DropOn(Mail, TitleOf(Notes), control: true);

        _system[Notes].Refusal = TabRefusal.SharedAppFrame;
        DropOn(Report, TitleOf(Notes), control: true);

        Assert.Empty(_controller.Groups);
        Assert.Equal(2, _system.Warnings);
        Assert.True(_system[Notes].Shown);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("refused a window: RunsAsAdministrator", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("refused a window: SharedAppFrame", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowThatCantBeATabDroppedOnAStripIsRefusedThere()
    {
        Group(Notes, Mail);
        _system[Report].Refusal = TabRefusal.NotResizable;
        var place = _strips[0].View!.Place;

        DropOn(Report, new Point(place.Bounds.Left + 10, place.Bounds.Top + 10), control: false);

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.Equal(1, _system.Warnings);
        Assert.True(_system[Report].Shown);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("refused a window: NotResizable", StringComparison.Ordinal));
    }

    [Fact]
    public void ClickingATabShowsItAtTheGroupsPlaceAndHidesTheOther()
    {
        Group(Notes, Mail);

        _strips[0].Choose(0);
        Run();

        Assert.Equal([Notes], _controller.Fronts);
        Assert.True(_system[Notes].Shown);
        Assert.False(_system[Mail].Shown);
        Assert.Equal(NotesFrame, _system[Notes].Visible);
        Assert.False(_system.Marks.ContainsKey(Notes));
        Assert.True(_system.Marks.ContainsKey(Mail));
        Assert.Equal(Notes, _system.ForegroundWindow);
    }

    [Fact]
    public void TheOldTabStaysUntilAHungTabShowsAndALateOneHidesAgain()
    {
        GroupThree();
        _system[Notes].Hung = true;

        _strips[0].Choose(0);
        Run();
        _clock.Advance(TimeSpan.FromSeconds(5));
        Run();

        Assert.True(_system[Report].Shown);
        Assert.Equal([Report], _controller.Fronts);

        _strips[0].Choose(1);
        Run();
        _system.Respond(Notes);
        Run();
        _clock.Advance(TimeSpan.FromMilliseconds(30));
        Run();

        Assert.Equal([Mail], _controller.Fronts);
        Assert.False(_system[Notes].Shown);
        Assert.False(_system[Report].Shown);
    }

    [Fact]
    public void ATabWithADialogOpenStaysInFront()
    {
        Group(Notes, Mail);
        _system[Mail].Enabled = false;

        _strips[0].Choose(0);
        Run();

        Assert.Equal([Mail], _controller.Fronts);
        Assert.False(_system[Notes].Shown);
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AMaximizedGroupShowsTabsMaximizedAndGivesHiddenTabsItsNormalPlace()
    {
        Group(Notes, Mail);
        _system[Mail].Maximized = true;
        _system[Mail].Visible = FakeTabsSystem.WorkArea;
        _controller.OnLocationChange(Mail);
        Advance(TimeSpan.FromMilliseconds(150));

        Assert.Contains((Notes, Rectangle.FromLTRB(293, 200, 1307, 807)), _system.HiddenPlacements);

        _strips[0].Choose(0);
        Run();

        Assert.Contains($"show {Notes} {TabShow.Maximized}", _system.Calls);
        Assert.True(_system[Notes].Maximized);
    }

    [Fact]
    public void ATabShownOnADisplayWithAnotherScaleGetsItsFrameBackAfterItsAppResizesIt()
    {
        Group(Notes, Mail);
        _system[Notes].Dpi = 144;

        _strips[0].Choose(0);
        Run();
        _system[Notes].Visible = NotesFrame with { Width = 1500, Height = 900 };
        Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(NotesFrame, _system[Notes].Visible);
    }

    [Fact]
    public void ATabShownOnADisplayWithTheSameScaleIsLeftWhereItsAppPutsIt()
    {
        Group(Notes, Mail);

        _strips[0].Choose(0);
        Run();
        _system[Notes].Visible = NotesFrame with { Width = 1500 };
        Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(1500, _system[Notes].Visible.Width);
    }

    [Fact]
    public void HiddenTabsGetTheNewPlaceOnlyOnceADragEnds()
    {
        Group(Notes, Mail);
        _controller.OnMoveSizeStart(Mail);
        _system[Mail].Visible = NotesFrame with { X = 400 };
        _controller.OnLocationChange(Mail);
        Advance(TimeSpan.FromMilliseconds(300));

        Assert.Empty(_system.HiddenPlacements);

        _controller.OnMoveSizeEnd(Mail);
        Run();

        Assert.Equal([(Notes, Rectangle.FromLTRB(393, 200, 1407, 807))], _system.HiddenPlacements);
    }

    [Fact]
    public void TheShortcutsGoRoundTheTabsAndAnnounceThem()
    {
        GroupThree();

        _controller.Next();
        Run();
        Assert.Equal([Notes], _controller.Fronts);

        _controller.Previous();
        Run();
        Assert.Equal([Report], _controller.Fronts);
        Assert.Equal(["Tab 1 of 3", "Tab 3 of 3"], _strips[0].Announcements);
    }

    [Fact]
    public void TheShortcutsWithoutAGroupInFrontPlayTheWarningSound()
    {
        _system.ForegroundWindow = Other;

        _controller.Next();
        _controller.MoveForward();

        Assert.Equal(2, _system.Warnings);
    }

    [Fact]
    public void MovingTheTabInFrontChangesItsPlaceAlongTheStrip()
    {
        GroupThree();

        _controller.MoveBack();
        Run();

        Assert.Equal([[Notes, Report, Mail]], _controller.Groups);
        Assert.Equal([Report], _controller.Fronts);
        Assert.Equal(1, _strips[0].View!.Front);

        _controller.MoveBack();
        _controller.MoveBack();
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void TheGroupShortcutPutsTheWindowInFrontInFrontOfTheWindowBehindIt()
    {
        _controller.GroupFrontWithBehind();
        Run();

        Assert.Equal([[Mail, Notes]], _controller.Groups);
        Assert.Equal([Notes], _controller.Fronts);
        Assert.Equal(new Rectangle(100, 100, 800, 500), _system[Notes].Visible);
        Assert.False(_system[Mail].Shown);
        Assert.Equal(Notes, _system.ForegroundWindow);
        Assert.Equal(["Tab 2 of 2"], _strips[0].Announcements);
    }

    [Fact]
    public void TheGroupShortcutJoinsTheGroupBehindPastWindowsThatCantBeTabs()
    {
        Group(Notes, Mail);
        _system.Add(Other + 1, new Rectangle(200, 200, 300, 300)).Refusal = TabRefusal.NotResizable;
        _system.Activate(Report);

        _controller.GroupFrontWithBehind();
        Run();

        Assert.Equal([[Notes, Mail, Report]], _controller.Groups);
        Assert.Equal([Report], _controller.Fronts);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void TheGroupShortcutMovesATabInFrontToTheGroupBehind()
    {
        Group(Notes, Mail);

        _controller.GroupFrontWithBehind();
        Run();

        Assert.Equal([[Report, Mail]], _controller.Groups);
        Assert.Equal([Mail], _controller.Fronts);
        Assert.Equal(new Rectangle(150, 150, 700, 450), _system[Mail].Visible);
        Assert.True(_system[Notes].Shown);
        Assert.False(_system.Marks.ContainsKey(Notes));
        Assert.True(_strips[0].Disposed);
    }

    [Fact]
    public void TheWindowBehindIsOnTheSameDisplayAndShown()
    {
        _system[Mail].Display = new Rectangle(1920, 0, 1920, 1080);
        _system[Report].Minimized = true;

        _controller.GroupFrontWithBehind();
        Run();

        Assert.Equal([[Other, Notes]], _controller.Groups);
    }

    [Fact]
    public void WithNothingBehindOrAWindowThatCantBeATabInFrontTheGroupShortcutPlaysTheWarningSound()
    {
        _system.Activate(Other);
        _system[Mail].Enabled = false;
        _system[Report].Cloaked = true;
        _system[Notes].Refusal = TabRefusal.SharedAppFrame;

        _controller.GroupFrontWithBehind();
        _system.Activate(Mail);
        _controller.GroupFrontWithBehind();
        Run();

        Assert.Empty(_controller.Groups);
        Assert.Equal(2, _system.Warnings);
    }

    [Fact]
    public void TheTakeOutShortcutShowsTheTabInFrontBesideItsGroup()
    {
        GroupThree();

        _controller.TakeOutFront();
        Run();

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.Equal([Mail], _controller.Fronts);
        Assert.True(_system[Mail].Shown);
        Assert.Equal(NotesFrame with { X = 332, Y = 232 }, _system[Report].Visible);
        Assert.Equal(Report, _system.ForegroundWindow);
        Assert.Equal(Mail, _system.WindowsBehind(Report)[0]);

        _controller.TakeOutFront();
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void GroupingAndTakingOutActOnTheGivenWindow()
    {
        _controller.GroupWithBehind(Mail);
        Run();

        Assert.Equal([[Report, Mail]], _controller.Groups);
        Assert.Empty(_strips[0].Announcements);

        _controller.TakeOutOf(Report);
        Assert.Equal(1, _system.Warnings);

        _controller.TakeOutOf(Mail);
        Run();
        Assert.Empty(_controller.Groups);
        Assert.True(_system[Report].Shown);
    }

    [Fact]
    public void AnAppShowingItsHiddenTabBringsItToTheFront()
    {
        Group(Notes, Mail);

        _system.AppShows(Notes);
        Run();

        Assert.Equal([Notes], _controller.Fronts);
        Assert.False(_system[Mail].Shown);
        Assert.Equal(NotesFrame, _system[Notes].Visible);
        Assert.False(_system.Marks.ContainsKey(Notes));
    }

    [Fact]
    public void AnAppShowingATabThatShowedAsItsLastGroupEndedBringsItToTheFront()
    {
        // Notes shows as its group ends, and Windows no longer tells Window Tabs that it did.
        Group(Notes, Mail);
        _controller.TakeOutFront();
        Run();
        Assert.Empty(_controller.Groups);
        Assert.True(_system[Notes].Shown);

        Group(Notes, Mail);
        _system.AppShows(Notes);
        Run();

        Assert.Equal([Notes], _controller.Fronts);
        Assert.False(_system[Mail].Shown);
    }

    [Fact]
    public void AGroupIsKeptWithItsTabsAppsAndTheOneInFrontAsItChanges()
    {
        var changes = 0;
        _controller.KeptChanged += (_, _) => changes++;

        Group(Notes, Mail);

        var kept = Assert.Single(_controller.Kept);
        Assert.Equal([Tab(Notes), Tab(Mail)], kept.Tabs);
        Assert.Equal(1, kept.Front);
        Assert.True(changes > 0);

        _strips[0].Choose(0);
        Run();
        Assert.Equal(0, Assert.Single(_controller.Kept).Front);

        _controller.MoveForward();
        Assert.Equal([Tab(Mail), Tab(Notes)], Assert.Single(_controller.Kept).Tabs);
    }

    [Fact]
    public void AGroupThatEndsIsForgottenButOneThatEndsAsWindowTabsStopsIsKept()
    {
        Group(Notes, Mail);
        _controller.TakeOutFront();
        Run();
        Assert.Empty(_controller.Kept);

        Group(Notes, Mail);
        _controller.ReleaseAll();
        Run();

        Assert.Empty(_controller.Groups);
        Assert.Equal([Tab(Notes), Tab(Mail)], Assert.Single(_controller.Kept).Tabs);
    }

    [Fact]
    public void TurningKeepGroupsOffForgetsTheGroupsAndTurningItOnKeepsTheOnesThereAre()
    {
        Group(Notes, Mail);

        _controller.Apply(new WindowTabsSettings { Enabled = true, KeepGroups = false });
        Assert.Empty(_controller.Kept);
        _strips[0].Choose(0);
        Run();
        Assert.Empty(_controller.Kept);

        _controller.Apply(new WindowTabsSettings { Enabled = true });
        Assert.Equal(0, Assert.Single(_controller.Kept).Front);
    }

    [Fact]
    public void AKeptGroupIsMadeAgainAtItsTabInFrontsPlaceOnceEachOfItsAppsHasAWindow()
    {
        const nint Paint = 42;
        _system.ForegroundWindow = Other;
        var ended = 0;
        _controller.RegroupingEnded += (_, _) => ended++;

        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(Mail), Tab(Paint)], Front = 1 }], Until(60));
        Run();
        Assert.Empty(_controller.Groups);
        Assert.True(_controller.IsRegrouping);

        _system.Add(Paint, new Rectangle(10, 10, 500, 400));
        _controller.OnNewWindow(Paint);
        Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal([[Notes, Mail, Paint]], _controller.Groups);
        Assert.Equal([Mail], _controller.Fronts);
        Assert.Equal(new Rectangle(100, 100, 800, 500), _system[Mail].Visible);
        Assert.True(_system[Mail].Shown);
        Assert.False(_system[Notes].Shown);
        Assert.False(_system[Paint].Shown);
        Assert.Equal(new Rectangle(100, 100, 800, 500), _system[Notes].Visible);
        Assert.True(_system.Marks.ContainsKey(Notes) && _system.Marks.ContainsKey(Paint));
        Assert.Equal(Other, _system.ForegroundWindow);
        Assert.True(_strips[0].Shown);
        Assert.False(_controller.IsRegrouping);
        Assert.Equal(1, ended);
        Assert.Equal(1, Assert.Single(_controller.Kept).Front);
    }

    [Fact]
    public void TheWindowInFrontStaysInFrontWhenItsGroupIsMadeAgain()
    {
        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(Mail)], Front = 1 }], Until(60));
        Run();

        Assert.Equal([Notes], _controller.Fronts);
        Assert.True(_system[Notes].Shown);
        Assert.False(_system[Mail].Shown);
    }

    [Fact]
    public void OnceTheMinuteIsUpAKeptGroupIsMadeOfTheWindowsThere()
    {
        _system.ForegroundWindow = Other;
        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(42), Tab(Mail)], Front = 1 }], Until(60));

        for (var second = 0; second < 59; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            Run();
        }

        Assert.Empty(_controller.Groups);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Run();

        // Without the window of the tab in front, the front-most window is in front.
        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.Equal([Notes], _controller.Fronts);
        Assert.False(_controller.IsRegrouping);
    }

    [Fact]
    public void AMinimizedTabInFrontMakesAMinimizedGroupWhoseTabsComeBackRestored()
    {
        _system.ForegroundWindow = Other;
        _system[Mail].Minimized = true;
        _system[Notes].Minimized = true;

        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(Mail)], Front = 1 }], Until(60));
        Run();

        Assert.Equal([Mail], _controller.Fronts);
        Assert.True(_system[Mail].Minimized);
        Assert.False(_system[Notes].Shown);
        Assert.False(_strips[0].Shown);

        _system[Mail].Minimized = false;
        _controller.OnMinimizeEnd(Mail);
        _strips[0].Choose(0);
        Run();

        Assert.True(_system[Notes].Shown);
        Assert.False(_system[Notes].Minimized);
        Assert.Contains($"show {Notes} Restored", _system.Calls);
    }

    [Fact]
    public void WindowsInAGroupOrThatCantBeTabsArentTaken()
    {
        Group(Notes, Mail);
        _system.ForegroundWindow = Other;
        _system[Report].Refusal = TabRefusal.NotResizable;

        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(Report)] }, new KeptGroup { Tabs = [Tab(Report), Tab(Other)] }], Until(1));
        _clock.Advance(TimeSpan.FromSeconds(2));
        Run();

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.False(_controller.IsRegrouping);
    }

    [Fact]
    public void WithKeepGroupsOffNothingIsMadeAgain()
    {
        _controller.Apply(new WindowTabsSettings { Enabled = true, KeepGroups = false });

        _controller.StartRegrouping([new KeptGroup { Tabs = [Tab(Notes), Tab(Mail)] }], Until(60));
        Run();

        Assert.Empty(_controller.Groups);
        Assert.False(_controller.IsRegrouping);
    }

    [Fact]
    public void AnAppHidingItsTabInFrontTakesItOutAndTheNextShows()
    {
        GroupThree();

        _system.AppHides(Report);
        Run();

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.Equal([Mail], _controller.Fronts);
        Assert.True(_system[Mail].Shown);
        Assert.False(_system[Report].Shown);
    }

    [Fact]
    public void WhenTheTabInFrontClosesTheNextShowsAndAGroupOfOneEnds()
    {
        Group(Notes, Mail);

        _system.Close(Mail);
        Run();

        Assert.Empty(_controller.Groups);
        Assert.True(_system[Notes].Shown);
        Assert.False(_system.Marks.ContainsKey(Notes));
        Assert.True(_strips[0].Disposed);
        Assert.Empty(_system.Watched);
        Assert.Empty(_system.Followed);
    }

    [Fact]
    public void AHiddenTabThatClosesLeavesItsGroup()
    {
        GroupThree();

        _system.Close(Notes);
        Run();

        Assert.Equal([[Mail, Report]], _controller.Groups);
        Assert.Equal([Report], _controller.Fronts);
    }

    [Fact]
    public void AWindowWindowsWontLetMarkLeavesInsteadOfHiding()
    {
        _system[Notes].MarkRefused = true;

        Group(Notes, Mail);

        Assert.Empty(_controller.Groups);
        Assert.True(_system[Notes].Shown);
    }

    [Fact]
    public void OwnedWindowsHideWithTheirTabAndComeBackWithIt()
    {
        _system.Add(Palette, new Rectangle(1400, 200, 200, 200), owner: Notes);
        _system.Activate(Notes);
        Group(Notes, Mail);

        Assert.False(_system[Palette].Shown);
        Assert.True(_system.Marks[Palette]);

        _strips[0].Choose(0);
        Run();

        Assert.True(_system[Palette].Shown);
        Assert.False(_system.Marks.ContainsKey(Palette));
    }

    [Fact]
    public void MoveOutOfGroupShowsTheTabBesideItsGroup()
    {
        GroupThree();

        _strips[0].Menu(0, TabMenuItem.MoveOutOfGroup);
        Run();

        Assert.Equal([[Mail, Report]], _controller.Groups);
        Assert.True(_system[Notes].Shown);
        Assert.Equal(NotesFrame with { X = 332, Y = 232 }, _system[Notes].Visible);
        Assert.Equal(Notes, _system.ForegroundWindow);
        Assert.False(_system.Marks.ContainsKey(Notes));
    }

    [Fact]
    public void CloseWindowShowsTheTabThenAsksItToClose()
    {
        Group(Notes, Mail);

        _strips[0].Menu(0, TabMenuItem.CloseWindow);
        Run();

        Assert.Equal([Notes], _controller.Fronts);
        Assert.Contains($"close {Notes}", _system.Calls);
    }

    [Fact]
    public void TheMenusSettingsItemOpensThePage()
    {
        Group(Notes, Mail);

        _strips[0].Menu(0, TabMenuItem.Settings);

        Assert.Equal(1, _settingsOpened);
    }

    [Fact]
    public void DroppingATabOnItsStripMovesItAndFarAwayTakesItOut()
    {
        GroupThree();
        var place = _strips[0].View!.Place;

        _strips[0].Drop(0, new Point(place.Bounds.Right - 5, place.Bounds.Top + 5));
        Run();
        Assert.Equal([[Mail, Report, Notes]], _controller.Groups);

        _strips[0].Drop(0, new Point(1200, 900));
        Run();
        Assert.Equal([[Report, Notes]], _controller.Groups);
        Assert.True(_system[Mail].Shown);
    }

    [Fact]
    public void WhenTheTabInFrontLeavesTheStripComesBackWithTheNextTabOnceItShows()
    {
        GroupThree();
        _system[Mail].Hung = true;

        _strips[0].Drop(2, new Point(1200, 900));
        Run();
        _system.Respond(Mail);
        Run();

        Assert.Equal([[Notes, Mail]], _controller.Groups);
        Assert.True(_system[Mail].Shown);
        Assert.True(_strips[0].Shown);
        Assert.Equal([Notes, Mail], _strips[0].View!.Tabs);
    }

    [Fact]
    public void DroppingATabOnAnotherGroupsStripMovesItThere()
    {
        Group(Notes, Mail);
        _system.Add(Other + 1, new Rectangle(300, 900, 600, 100));
        _system.Activate(Other);
        Group(Other, Other + 1);
        var target = _strips[1].View!.Place;

        _strips[0].Drop(0, new Point(target.Bounds.Left + 5, target.Bounds.Top + 5));
        Run();

        Assert.Equal([[Notes, Other, Other + 1]], _controller.Groups);
        Assert.Equal([Notes], _controller.Fronts);
        Assert.True(_strips[0].Disposed);
    }

    [Fact]
    public void MinimizedFullScreenOrOnAnotherDesktopTheStripHides()
    {
        Group(Notes, Mail);
        var strip = _strips[0];

        _controller.OnMinimizeStart(Mail);
        Assert.False(strip.Shown);
        _controller.OnMinimizeEnd(Mail);
        Assert.True(strip.Shown);

        _system[Mail].FullScreen = true;
        _controller.OnLocationChange(Mail);
        Assert.False(strip.Shown);
        _system[Mail].FullScreen = false;

        _system[Mail].Cloaked = true;
        _controller.OnCloakChanged(Mail);
        Assert.False(strip.Shown);
        _system[Mail].Cloaked = false;
        _controller.OnCloakChanged(Mail);
        Assert.True(strip.Shown);
    }

    [Fact]
    public void AHiddenTabShownWhileTheGroupIsOnAnotherDesktopLeavesIt()
    {
        GroupThree();
        _system[Report].Cloaked = true;

        _system.AppShows(Notes);
        Run();

        Assert.Equal([[Mail, Report]], _controller.Groups);
        Assert.True(_system[Notes].Shown);
    }

    [Fact]
    public void AStripOverItsWindowsTopShowsAfterThePointerRestsThere()
    {
        Group(Notes, Mail);
        _system[Mail].Visible = new Rectangle(300, 0, 1000, 600);
        _controller.OnLocationChange(Mail);
        var strip = _strips[0];
        Assert.False(strip.Shown);

        _system.PointerPosition = new Point(800, 0);
        Advance(TimeSpan.FromMilliseconds(250));
        Assert.False(strip.Shown);
        Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(strip.Shown);

        _system.PointerPosition = new Point(800, 500);
        Advance(TimeSpan.FromSeconds(1));
        Assert.False(strip.Shown);
    }

    [Fact]
    public void ReleaseAllBringsHiddenTabsBackMinimizedAndEndsTheGroups()
    {
        _system.Add(Palette, new Rectangle(1400, 200, 200, 200), owner: Notes);
        _system.Activate(Notes);
        GroupThree();

        var released = _controller.ReleaseAll();

        Assert.Equal(2, released);
        Assert.True(_system[Notes].Shown && _system[Notes].Minimized);
        Assert.True(_system[Mail].Shown && _system[Mail].Minimized);
        Assert.True(_system[Palette].Shown);
        Assert.True(_system.Calls.IndexOf($"show without activating {Palette}") < _system.Calls.IndexOf($"show minimized {Notes}"));
        Assert.Empty(_system.Marks);
        Assert.Empty(_controller.Groups);
        Assert.False(_shared.Contains(Report));
    }

    [Fact]
    public void ReleaseNowBringsHiddenTabsBackAndLeavesTheMarks()
    {
        GroupThree();

        _controller.ReleaseNow();

        Assert.True(_system[Notes].Minimized && _system[Notes].Shown);
        Assert.True(_system.Marks.ContainsKey(Notes));
    }

    [Fact]
    public void RecoverBringsBackMarkedHiddenWindowsAndRemovesEveryMark()
    {
        _system.Add(Palette, new Rectangle(1400, 200, 200, 200), owner: Notes);
        _system[Notes].Shown = false;
        _system[Palette].Shown = false;
        _system.Mark(Notes, owned: false);
        _system.Mark(Palette, owned: true);
        _system.Mark(Mail, owned: false);

        var recovered = TabsController.Recover(_system);

        Assert.Equal(1, recovered);
        Assert.True(_system[Notes].Minimized);
        Assert.True(_system[Palette].Shown);
        Assert.False(_system[Mail].Minimized);
        Assert.Empty(_system.Marks);
    }

    private static Point TitleOf(FakeTabWindow window) => new(window.Visible.Left + 100, window.Visible.Top + 10);

    private Point TitleOf(nint window) => TitleOf(_system[window]);

    private void GroupThree()
    {
        Group(Notes, Mail);
        var place = _strips[0].View!.Place;
        DropOn(Report, new Point(place.Bounds.Right - 5, place.Bounds.Top + 5), control: false);
        Assert.Equal([[Notes, Mail, Report]], _controller.Groups);
    }

    private void Group(nint target, nint dropped) => DropOn(dropped, TitleOf(target), control: true);

    private void DropOn(nint dropped, Point point, bool control)
    {
        _controller.OnMoveSizeStart(dropped);
        Drag(dropped, point, control);
        _controller.OnMoveSizeEnd(dropped);
        Run();
    }

    // The window follows the pointer, keeping its size, as Windows moves it during a drag.
    private void Drag(nint window, Point point, bool control)
    {
        _system.PointerPosition = point;
        _system.IsControlHeld = control;
        var visible = _system[window].Visible;
        _system[window].Visible = visible with { X = point.X - 50, Y = point.Y - 10 };
        _controller.OnLocationChange(window);
    }

    private void RunThreadOnly()
    {
        while (_thread.TryDequeue(out var action))
        {
            action();
        }
    }

    private void Run()
    {
        for (var round = 0; round < 50 && (_thread.Count > 0 || HasFrames()); round++)
        {
            RunThreadOnly();
            _system.RunFrames();
            RunThreadOnly();
        }
    }

    private bool HasFrames()
    {
        // The fake queues frame waits; running them again is harmless when there are none.
        _system.RunFrames();
        return _thread.Count > 0;
    }

    private static KeptTab Tab(nint window) => new() { Program = FakeTabsSystem.AppProgram(window) };

    private long Until(int seconds) => _clock.GetTimestamp() + TimeSpan.FromSeconds(seconds).Ticks;

    private void Advance(TimeSpan time)
    {
        var step = TimeSpan.FromMilliseconds(10);
        for (var elapsed = TimeSpan.Zero; elapsed < time; elapsed += step)
        {
            _clock.Advance(step);
            Run();
        }
    }
}
