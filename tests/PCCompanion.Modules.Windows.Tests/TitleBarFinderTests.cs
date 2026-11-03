using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TitleBarFinderTests
{
    private const nint Window = 10;
    private const nint Child = 11;
    private static readonly Point OnTitleBar = new(400, 120);
    private static readonly Point OnTab = new(150, 120);

    private readonly FakeTitleBarWindows _windows = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly ExcludedWindows _excluded;
    private readonly TitleBarFinder _finder;

    public TitleBarFinderTests()
    {
        // A window at (100, 100) whose title bar answers as Edge's does: tabs on the left, the empty strip on the right.
        _windows.Add(Window, new Rectangle(100, 100, 800, 600));
        _windows.Answers = (_, point) => point.Y < 140 && point.X >= 300 ? HitTestAnswer.Caption : HitTestAnswer.Elsewhere;
        _excluded = new ExcludedWindows(_windows);
        _finder = new TitleBarFinder(_windows, _excluded, _clock);
    }

    [Fact]
    public void AWindowThatAnswersCaptionWhereThePointerRestsGivesASpot()
    {
        MoveTo(OnTitleBar);

        Assert.Equal(new TitleBarSpot(Window, OnTitleBar), _finder.Spot);
        Assert.Equal([(Window, OnTitleBar)], _windows.Asked);
    }

    [Fact]
    public void ATabGivesNoSpot()
    {
        MoveTo(OnTitleBar);
        MoveTo(OnTab);

        Assert.Null(_finder.Spot);
    }

    [Theory]
    [InlineData(96u, 163, true)]
    [InlineData(96u, 164, false)]
    [InlineData(144u, 195, true)]
    [InlineData(144u, 196, false)]
    public void OnlyTheTopBandAtTheWindowsDpiIsAsked(uint dpi, int y, bool asked)
    {
        var windows = new FakeTitleBarWindows { Answers = (_, _) => HitTestAnswer.Caption };
        windows.Add(Window, new Rectangle(100, 100, 800, 600), dpi: dpi);
        var finder = new TitleBarFinder(windows, new ExcludedWindows(windows), _clock);

        windows.PointerPosition = new Point(400, y);
        finder.PointerMoved();

        Assert.Equal(asked, windows.Asked.Count == 1);
        Assert.Equal(asked, finder.Spot is not null);
    }

    [Fact]
    public void AChildThatAnswersTransparentPassesTheQuestionToItsParent()
    {
        _windows.Add(Child, new Rectangle(100, 100, 800, 40), parent: Window);
        _windows.Answers = (window, _) => window == Child ? HitTestAnswer.Transparent : HitTestAnswer.Caption;

        MoveTo(OnTitleBar);

        Assert.Equal([(Child, OnTitleBar), (Window, OnTitleBar)], _windows.Asked);
        Assert.Equal(new TitleBarSpot(Window, OnTitleBar), _finder.Spot);
    }

    [Fact]
    public void AChildThatAnswersCaptionIsTheTitleBarOfItsTopLevelWindow()
    {
        _windows.Add(Child, new Rectangle(300, 100, 400, 40), parent: Window);
        _windows.Answers = (window, _) => window == Child ? HitTestAnswer.Caption : HitTestAnswer.Elsewhere;

        MoveTo(OnTitleBar);

        Assert.Equal([(Child, OnTitleBar)], _windows.Asked);
        Assert.Equal(new TitleBarSpot(Window, OnTitleBar), _finder.Spot);
    }

    [Fact]
    public void AnAnswerAboutWhereThePointerWasIsAskedAgainWhereItIs()
    {
        _windows.AnswersLater = true;
        MoveTo(new Point(401, 120));

        // The pointer moves on before the answer comes, with no more pointer events.
        _windows.PointerPosition = OnTitleBar;
        _windows.Answer();
        Assert.Null(_finder.Spot);

        _windows.Answer();
        Assert.Equal(new TitleBarSpot(Window, OnTitleBar), _finder.Spot);
        Assert.Equal(2, _windows.Asked.Count);
    }

    [Fact]
    public void OnlyOneQuestionWaitsAndOneUnansweredIsDroppedAfter150Milliseconds()
    {
        _windows.AnswersLater = true;
        MoveTo(OnTitleBar);
        MoveTo(new Point(410, 120));
        Assert.Single(_windows.Asked);

        _clock.Advance(TimeSpan.FromMilliseconds(150));
        MoveTo(new Point(420, 120));
        Assert.Equal(2, _windows.Asked.Count);

        // The dropped question's answer changes nothing; the new one's does.
        _windows.Answer();
        Assert.Null(_finder.Spot);
        _windows.Answer();
        Assert.Equal(new TitleBarSpot(Window, new Point(420, 120)), _finder.Spot);
    }

    [Fact]
    public void AWindowThatMayNotGetActionsIsNotAsked()
    {
        _windows.NotCandidates.Add(Window);

        MoveTo(OnTitleBar);

        Assert.Empty(_windows.Asked);
        Assert.Null(_finder.Spot);
    }

    [Fact]
    public void AKnownSpotIsNotAskedAgainUntilThePointerMoves()
    {
        MoveTo(OnTitleBar);
        MoveTo(OnTitleBar);
        Assert.Single(_windows.Asked);

        MoveTo(new Point(410, 120));
        Assert.Equal(2, _windows.Asked.Count);
    }

    [Fact]
    public void ARefusedQuestionGivesNoSpot()
    {
        _windows.Refuses = true;

        MoveTo(OnTitleBar);

        Assert.Null(_finder.Spot);
    }

    [Fact]
    public void SpotChangedIsRaisedOnlyWhenTheSpotChanges()
    {
        var changes = 0;
        _finder.SpotChanged += (_, _) => changes++;

        MoveTo(OnTitleBar);
        MoveTo(OnTitleBar);
        MoveTo(OnTab);
        MoveTo(new Point(150, 500));

        Assert.Equal(2, changes);
    }

    [Fact]
    public void CheckGivesTheWindowOnlyForACaption()
    {
        var found = new List<nint>();

        _finder.Check(OnTitleBar, found.Add);
        _finder.Check(OnTab, found.Add);
        _finder.Check(new Point(400, 500), found.Add);

        Assert.Equal([Window], found);
    }

    [Fact]
    public void CheckIgnoresAnAnswerThatComesTooLate()
    {
        _windows.AnswersLater = true;
        var found = new List<nint>();

        _finder.Check(OnTitleBar, found.Add);
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        _windows.Answer();

        Assert.Empty(found);
    }

    [Fact]
    public void AListedAppsWindowIsntAskedAndGivesNoSpot()
    {
        _excluded.SetApps([new ListedApp { Name = "App", Program = @"C:\Apps\10.exe" }]);

        MoveTo(OnTitleBar);

        Assert.Null(_finder.Spot);
        Assert.Empty(_windows.Asked);
        Assert.Equal(TitleBarCheck.AppListed, _finder.Check(OnTitleBar, _ => Assert.Fail("A listed app's window was checked.")));
    }

    [Fact]
    public void CheckSaysWhyAWindowWasntAsked()
    {
        Assert.Equal(TitleBarCheck.Elsewhere, _finder.Check(new Point(400, 500), _ => { }));

        _windows.NotCandidates.Add(Window);

        Assert.Equal(TitleBarCheck.WindowLeftOut, _finder.Check(OnTitleBar, _ => { }));
        Assert.Empty(_windows.Asked);
    }

    [Fact]
    public void StopForgetsTheSpotAndDropsAnswersThatComeLater()
    {
        MoveTo(OnTitleBar);
        _windows.AnswersLater = true;
        var found = new List<nint>();
        _finder.Check(OnTitleBar, found.Add);
        MoveTo(new Point(410, 120));

        _finder.Stop();
        _windows.Answer();
        _windows.Answer();

        Assert.Null(_finder.Spot);
        Assert.Empty(found);
    }

    private void MoveTo(Point point)
    {
        _windows.PointerPosition = point;
        _finder.PointerMoved();
    }
}
