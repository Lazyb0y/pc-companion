using System.Drawing;
using PCCompanion.Modules.Windows.DropDownApps;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownPlaceTests
{
    // A 1920 x 1080 display with the taskbar at the bottom, and the borders the spike measured at 100%.
    private static readonly Rectangle Display = new(0, 0, 1920, 1080);
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1032);
    private static readonly FrameInsets Borders = new(7, 0, 7, 7);

    [Fact]
    public void InsetsAreTheBordersBetweenTheRectangleAndItsVisiblePart()
    {
        var bounds = new Rectangle(93, 200, 814, 607);
        var visible = new Rectangle(100, 200, 800, 600);

        var insets = FrameInsets.Between(bounds, visible);

        Assert.Equal(Borders, insets);
        Assert.Equal(visible, insets.Inside(bounds));
    }

    [Fact]
    public void TheVisiblePartFillsTheWorkAreasWidthAndTopHalf()
    {
        var size = DropDownPlace.VisibleSize(WorkArea, 1.0, 0.5);
        var shown = DropDownPlace.Shown(WorkArea, size, Borders);

        Assert.Equal(new Size(1920, 516), size);
        Assert.Equal(new Rectangle(0, 0, 1920, 516), Borders.Inside(shown));
        Assert.Equal(new Rectangle(-7, 0, 1934, 523), shown);
    }

    [Fact]
    public void ANarrowerWindowIsCenteredAcrossTheWorkArea()
    {
        var workArea = new Rectangle(1920, 40, 2560, 1400);

        var shown = DropDownPlace.Shown(workArea, DropDownPlace.VisibleSize(workArea, 0.6, 0.35), Borders);

        Assert.Equal(new Rectangle(1920 + 512, 40, 1536, 490), Borders.Inside(shown));
    }

    [Fact]
    public void AWindowLargerThanAskedIsCenteredFromItsOwnSize()
    {
        // Windows keeps an app's minimum size of 1000 x 700, as the spike showed.
        var shown = DropDownPlace.Shown(WorkArea, new Size(1000, 700), Borders);

        Assert.Equal(new Rectangle(460, 0, 1000, 700), Borders.Inside(shown));
    }

    [Fact]
    public void ADraggedSizeIsKeptAsAShareOfTheWorkArea()
    {
        var share = DropDownPlace.ShareOf(WorkArea, new Size(960, 258));

        Assert.Equal((0.5, 0.25), share);
        Assert.Equal(new Size(1280, 350), DropDownPlace.VisibleSize(new Rectangle(0, 0, 2560, 1400), share.Width, share.Height));
    }

    [Fact]
    public void TheSlideStartsWithTheVisiblePartRightAboveTheWorkArea()
    {
        var shown = DropDownPlace.Shown(WorkArea, new Size(1920, 516), Borders);

        var above = DropDownPlace.Above(shown, WorkArea, Borders);

        Assert.Equal(new Point(-7, -516), above);
        Assert.Equal(WorkArea.Top, Borders.Inside(new Rectangle(above, shown.Size)).Bottom);
    }

    [Fact]
    public void ADisplayTouchingTheTopEdgeAcrossTheWindowIsAbove()
    {
        var shown = new Rectangle(-7, 0, 1934, 523);

        Assert.True(DropDownPlace.HasDisplayAbove(Display, shown, [Display, new Rectangle(0, -1080, 1920, 1080)]));
        Assert.True(DropDownPlace.HasDisplayAbove(Display, shown, [Display, new Rectangle(1000, -900, 1600, 900)]));
    }

    [Fact]
    public void DisplaysBesideOrApartAreNotAbove()
    {
        var shown = new Rectangle(-7, 0, 1934, 523);

        Assert.False(DropDownPlace.HasDisplayAbove(Display, shown, [Display]));
        Assert.False(DropDownPlace.HasDisplayAbove(Display, shown, [Display, new Rectangle(1920, 0, 1920, 1080)]));
        Assert.False(DropDownPlace.HasDisplayAbove(Display, shown, [Display, new Rectangle(2000, -1080, 1920, 1080)]));
        Assert.False(DropDownPlace.HasDisplayAbove(Display, shown, [Display, new Rectangle(0, -1200, 1920, 1080)]));
    }
}
