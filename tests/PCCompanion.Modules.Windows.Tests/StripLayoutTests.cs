using System.Drawing;
using PCCompanion.Modules.Windows.WindowTabs;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class StripLayoutTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void TheStripStandsAboveTheWindowFromItsLeftEdge()
    {
        var place = StripLayout.Place(new Rectangle(300, 200, 1000, 600), WorkArea, 96, 3, rightToLeft: false);

        Assert.Equal(new Rectangle(300, 168, 600, 32), place.Bounds);
        Assert.False(place.AtTopEdge);
        Assert.Equal([new Rectangle(0, 0, 200, 32), new Rectangle(200, 0, 200, 32), new Rectangle(400, 0, 200, 32)], place.Tabs);
    }

    [Fact]
    public void WithoutRoomAboveTheStripIsOverTheTopOfTheWindow()
    {
        var maximized = StripLayout.Place(new Rectangle(0, 0, 1920, 1040), WorkArea, 96, 2, rightToLeft: false);
        var nearTheTop = StripLayout.Place(new Rectangle(100, 20, 800, 600), WorkArea, 96, 2, rightToLeft: false);

        Assert.Equal(new Rectangle(0, 0, 400, 32), maximized.Bounds);
        Assert.True(maximized.AtTopEdge);
        Assert.Equal(new Rectangle(100, 20, 400, 32), nearTheTop.Bounds);
        Assert.True(nearTheTop.AtTopEdge);
    }

    [Fact]
    public void TheWorkAreaOfALowerDisplayCountsForTheRoomAbove()
    {
        var place = StripLayout.Place(new Rectangle(100, 1100, 800, 600), new Rectangle(0, 1080, 1920, 1040), 96, 2, rightToLeft: false);

        Assert.True(place.AtTopEdge);
        Assert.Equal(1100, place.Bounds.Top);
    }

    [Fact]
    public void TabsNarrowToFitTheWindowDownToTheirIcons()
    {
        var narrower = StripLayout.Place(new Rectangle(0, 100, 500, 400), WorkArea, 96, 4, rightToLeft: false);
        var past = StripLayout.Place(new Rectangle(0, 100, 300, 400), WorkArea, 96, 10, rightToLeft: false);

        Assert.Equal(125, narrower.Tabs[0].Width);
        Assert.Equal(500, narrower.Bounds.Width);
        Assert.Equal(40, past.Tabs[0].Width);
        Assert.Equal(400, past.Bounds.Width);
    }

    [Fact]
    public void SizesFollowTheDisplaysScale()
    {
        var place = StripLayout.Place(new Rectangle(300, 300, 1500, 600), WorkArea, 144, 2, rightToLeft: false);

        Assert.Equal(new Rectangle(300, 252, 600, 48), place.Bounds);
        Assert.Equal(300, place.Tabs[1].Left);
    }

    [Fact]
    public void RightToLeftTheStripStandsAtTheRightEdgeAndTheFirstTabIsOnTheRight()
    {
        var place = StripLayout.Place(new Rectangle(300, 200, 1000, 600), WorkArea, 96, 3, rightToLeft: true);

        Assert.Equal(new Rectangle(700, 168, 600, 32), place.Bounds);
        Assert.Equal([new Rectangle(400, 0, 200, 32), new Rectangle(200, 0, 200, 32), new Rectangle(0, 0, 200, 32)], place.Tabs);
    }

    [Fact]
    public void TabAtFindsTheTabUnderAPoint()
    {
        var place = StripLayout.Place(new Rectangle(300, 200, 1000, 600), WorkArea, 96, 3, rightToLeft: false);

        Assert.Equal(0, place.TabAt(new Point(10, 10)));
        Assert.Equal(2, place.TabAt(new Point(599, 31)));
        Assert.Equal(-1, place.TabAt(new Point(600, 10)));
        Assert.Equal(1, place.TabAt(place.ToStrip(new Point(550, 180))));
        Assert.True(place.Contains(new Point(300, 168)));
        Assert.False(place.Contains(new Point(300, 200)));
    }

    [Fact]
    public void InsertionAtGoesBetweenTheTabsWhoseMiddlesArePassed()
    {
        var ltr = StripLayout.Place(new Rectangle(300, 200, 1000, 600), WorkArea, 96, 3, rightToLeft: false);
        var rtl = StripLayout.Place(new Rectangle(300, 200, 1000, 600), WorkArea, 96, 3, rightToLeft: true);

        Assert.Equal(0, ltr.InsertionAt(new Point(50, 10)));
        Assert.Equal(1, ltr.InsertionAt(new Point(150, 10)));
        Assert.Equal(3, ltr.InsertionAt(new Point(590, 10)));
        Assert.Equal(0, rtl.InsertionAt(new Point(590, 10)));
        Assert.Equal(1, rtl.InsertionAt(new Point(450, 10)));
        Assert.Equal(3, rtl.InsertionAt(new Point(10, 10)));
    }
}
