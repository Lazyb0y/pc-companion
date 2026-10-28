using System.Drawing;
using PCCompanion.Modules.Input.LockKeys;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class NoticePlacementTests
{
    // A 1920 x 1080 main display with the taskbar at the bottom, and a 2560 x 1440 display at 150%
    // on its left, with its taskbar at the top.
    private static readonly NoticeDisplay Main = new(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1032), 96);
    private static readonly NoticeDisplay Left = new(new Rectangle(-2560, 0, 2560, 1440), new Rectangle(-2560, 72, 2560, 1368), 144);
    private static readonly NoticeDisplay[] Displays = [Left, Main];
    private static readonly Size Notice = new(138, 40);

    [Fact]
    public void TheNoticeShowsOnTheDisplayOfTheWindowInFront()
    {
        Assert.Same(Left, NoticePlacement.ChooseDisplay(Displays, Left.Bounds, new Point(100, 100)));
    }

    [Fact]
    public void WithNoWindowInFrontItShowsOnTheDisplayWithThePointer()
    {
        Assert.Same(Left, NoticePlacement.ChooseDisplay(Displays, null, new Point(-10, 500)));
    }

    [Fact]
    public void AWindowOnADisplayThatsGoneCountsAsNone()
    {
        Assert.Same(Main, NoticePlacement.ChooseDisplay(Displays, new Rectangle(5000, 0, 800, 600), new Point(100, 100)));
    }

    [Fact]
    public void WithoutAPointerItShowsOnTheMainDisplay()
    {
        Assert.Same(Main, NoticePlacement.ChooseDisplay(Displays, null, null));
        Assert.Same(Main, NoticePlacement.ChooseDisplay(Displays, null, new Point(9000, 9000)));
    }

    [Fact]
    public void WithNoMainDisplayItShowsOnTheFirstAndWithNoneNowhere()
    {
        Assert.Same(Left, NoticePlacement.ChooseDisplay([Left], null, null));
        Assert.Null(NoticePlacement.ChooseDisplay([], null, null));
    }

    [Fact]
    public void AtTheBottomCenterTheNoticeIsAboveTheTaskbar()
    {
        Assert.Equal(new Rectangle(891, 1032 - 24 - 40, 138, 40), NoticePlacement.Place(Main, Notice, NoticePosition.BottomCenter));
    }

    [Fact]
    public void AtTheTopItsBelowATaskbarThere()
    {
        var size = new Size(207, 60);

        Assert.Equal(new Rectangle(-2560 + ((2560 - 207) / 2), 72 + 36, 207, 60), NoticePlacement.Place(Left, size, NoticePosition.TopCenter));
        Assert.Equal(new Rectangle(-2560 + 36, 72 + 36, 207, 60), NoticePlacement.Place(Left, size, NoticePosition.TopLeft));
    }

    [Fact]
    public void ATaskbarOnTheLeftMovesTheTopLeftPosition()
    {
        var display = Main with { WorkArea = new Rectangle(48, 0, 1872, 1080) };

        Assert.Equal(new Point(48 + 24, 24), NoticePlacement.Place(display, Notice, NoticePosition.TopLeft).Location);
    }

    [Fact]
    public void AwayMovesItFromTheEdgeItsAt()
    {
        Assert.Equal(1032 - 24 - 40 - 50, NoticePlacement.Place(Main, Notice, NoticePosition.BottomCenter, away: 50).Y);
        Assert.Equal(24 + 50, NoticePlacement.Place(Main, Notice, NoticePosition.TopCenter, away: 50).Y);
        Assert.Equal(24 + 50, NoticePlacement.Place(Main, Notice, NoticePosition.TopLeft, away: 50).Y);
    }
}
