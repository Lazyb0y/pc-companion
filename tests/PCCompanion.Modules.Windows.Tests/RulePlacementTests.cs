using System.Drawing;
using PCCompanion.Modules.Windows.WindowRules;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class RulePlacementTests
{
    // A 1920 x 1080 display with a 48-pixel taskbar at the bottom, and a second one to its right.
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1032);
    private static readonly Rectangle Second = new(1920, 0, 2560, 1392);
    private static readonly Rectangle Frame = new(100, 200, 800, 600);

    [Theory]
    [InlineData("LeftHalf", 0, 0, 960, 1032)]
    [InlineData("RightHalf", 960, 0, 960, 1032)]
    [InlineData("TopHalf", 0, 0, 1920, 516)]
    [InlineData("BottomHalf", 0, 516, 1920, 516)]
    [InlineData("TopLeft", 0, 0, 960, 516)]
    [InlineData("TopRight", 960, 0, 960, 516)]
    [InlineData("BottomLeft", 0, 516, 960, 516)]
    [InlineData("BottomRight", 960, 516, 960, 516)]
    [InlineData("WorkArea", 0, 0, 1920, 1032)]
    [InlineData("Centered", 560, 216, 800, 600)]
    public void PresetsArePartsOfTheWorkArea(string which, int x, int y, int width, int height)
    {
        Assert.Equal(new Rectangle(x, y, width, height), RulePlacement.Frame(Frame, WorkArea, WorkArea, new RulePlace { Which = which }, movesDisplay: false));
    }

    [Fact]
    public void ACustomPlaceIsInPercentOfTheWorkArea()
    {
        var place = new RulePlace { Which = "Custom", Left = 25, Top = 10, Width = 50, Height = 80 };

        Assert.Equal(new Rectangle(480, 103, 960, 826), RulePlacement.Frame(Frame, WorkArea, WorkArea, place, movesDisplay: false));
        Assert.Equal(new Rectangle(2560, 139, 1280, 1114), RulePlacement.Frame(Frame, WorkArea, Second, place, movesDisplay: true));
    }

    [Fact]
    public void PresetsOnAnotherDisplayUseItsWorkArea()
    {
        Assert.Equal(new Rectangle(3200, 0, 1280, 1392), RulePlacement.Frame(Frame, WorkArea, Second, new RulePlace { Which = "RightHalf" }, movesDisplay: true));
    }

    [Fact]
    public void ADisplayWithoutAPlaceKeepsTheSizeAtTheSamePlaceRelativeToTheWorkArea()
    {
        Assert.Equal(new Rectangle(2053, 270, 800, 600), RulePlacement.Frame(Frame, WorkArea, Second, place: null, movesDisplay: true));
    }

    [Fact]
    public void NoDisplayAndNoPlaceIsNoChange()
    {
        Assert.Null(RulePlacement.Frame(Frame, WorkArea, WorkArea, place: null, movesDisplay: false));
        Assert.Null(RulePlacement.Frame(Frame, WorkArea, WorkArea, new RulePlace { Which = "Spiral" }, movesDisplay: false));
    }

    [Fact]
    public void ARememberedPlaceWithoutOneIsNoChangeAndOneThatSticksOutIsMovedIn()
    {
        var remembered = new RulePlace { Which = "Remembered" };

        Assert.Null(RulePlacement.Frame(Frame, WorkArea, WorkArea, remembered, movesDisplay: false));
        Assert.Equal(new Rectangle(1120, 432, 800, 600), RulePlacement.Frame(Frame, WorkArea, WorkArea, remembered, movesDisplay: false, new Rectangle(1500, 900, 800, 600)));
    }

    [Fact]
    public void AFrameIsKeptInsideTheWorkAreaAndNoLargerThanIt()
    {
        Assert.Equal(new Rectangle(0, 0, 1920, 1032), RulePlacement.Within(new Rectangle(-100, -50, 2500, 1200), WorkArea));
        Assert.Equal(new Rectangle(1120, 432, 800, 600), RulePlacement.Within(new Rectangle(1600, 900, 800, 600), WorkArea));
        Assert.Equal(new Rectangle(1920, 0, 800, 600), RulePlacement.Within(new Rectangle(100, 0, 800, 600), Second));
    }

    [Fact]
    public void TheWindowRectangleAddsTheBordersTheWindowHasNow()
    {
        var bounds = new Rectangle(93, 200, 814, 607);
        var visible = new Rectangle(100, 200, 800, 600);

        Assert.Equal(new Rectangle(953, 0, 974, 1039), RulePlacement.WithBorders(new Rectangle(960, 0, 960, 1032), bounds, visible));
        Assert.Equal(new Rectangle(950, 0, 980, 1042), RulePlacement.WithUsualBorders(new Rectangle(960, 0, 960, 1032), 144));
    }

    [Fact]
    public void AWindowsCustomPlaceIsInTenthsOfAPercentInsideTheWorkArea()
    {
        var place = OpenRuleWindows.PlaceOf(new Rectangle(480, 103, 961, 826), WorkArea);

        Assert.Equal((25.0, 10.0, 50.1, 80.0), (place.Left, place.Top, place.Width, place.Height));
        Assert.Equal(PlaceChoice.Custom, place.KnownWhich);
        Assert.Equal((0.0, 0.0, 100.0, 100.0), (OpenRuleWindows.PlaceOf(new Rectangle(-50, -10, 2500, 1300), WorkArea) is var all ? (all.Left, all.Top, all.Width, all.Height) : default));
    }

    [Fact]
    public void TheDistanceIsTheLargestDifferenceOfAnEdge()
    {
        Assert.Equal(0, RulePlacement.Distance(Frame, Frame));
        Assert.Equal(30, RulePlacement.Distance(Frame, new Rectangle(110, 200, 820, 600)));
    }
}
