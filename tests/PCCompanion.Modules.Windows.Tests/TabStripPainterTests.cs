using System;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TabStripPainterTests
{
    private const uint Accent = 0x0078D4;
    private const uint Opaque = 0xFF000000;
    private const uint Green = 0xFF00FF00;

    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);
    private static readonly TabColors Light = TabColors.Light(Accent);
    private static readonly TabStyle Style = new(96, 1, "Segoe UI", RoundCorners: false, RightToLeft: false);

    [Fact]
    public void TheCanvasHasTheStripsSize()
    {
        var canvas = Paint(View(3), Looks(3));

        Assert.Equal(600, canvas.Width);
        Assert.Equal(32, canvas.Height);
    }

    [Fact]
    public void TheTabInFrontStandsOutWithALineInTheAccentColor()
    {
        var canvas = Paint(View(3), Looks(3));

        Assert.Equal(Opaque | Light.Front, At(canvas, 5, 15));
        Assert.Equal(Opaque | Accent, At(canvas, 100, 30));
        Assert.Equal(Opaque | Light.Background, At(canvas, 250, 15));
        Assert.Equal(Opaque | Light.Background, At(canvas, 250, 30));
    }

    [Fact]
    public void TheStripShowsBetweenTabsAndAboveThem()
    {
        var canvas = Paint(View(3), Looks(3));

        Assert.Equal(Opaque | Light.Background, At(canvas, 1, 15));
        Assert.Equal(Opaque | Light.Background, At(canvas, 100, 1));
    }

    [Fact]
    public void AChosenTabStandsOutBeforeItComesToTheFront()
    {
        var canvas = Paint(View(3) with { Chosen = 2 }, Looks(3));

        Assert.Equal(Opaque | Light.Background, At(canvas, 5, 15));
        Assert.Equal(Opaque | Light.Front, At(canvas, 405, 15));
    }

    [Fact]
    public void TheTabUnderThePointerIsHighlighted()
    {
        var canvas = TabStripPainter.Paint(View(3), Looks(3), hover: 1, Light, Style);

        Assert.Equal(Opaque | Light.Hover, At(canvas, 250, 15));
        Assert.Equal(Opaque | Light.Background, At(canvas, 450, 15));
    }

    [Fact]
    public void HighContrastDrawsABorderAndTheHighlightForTheTabInFront()
    {
        var colors = TabColors.HighContrast(0x000000, 0xFFFFFF, 0x1AEBFF);

        var canvas = TabStripPainter.Paint(View(3), Looks(3), hover: -1, colors, Style);

        Assert.Equal(0xFFFFFFFF, At(canvas, 0, 15));
        Assert.Equal(0xFFFFFFFF, At(canvas, 300, 0));
        Assert.Equal(0xFF1AEBFF, At(canvas, 5, 15));
        Assert.Equal(0xFF000000, At(canvas, 250, 15));
    }

    [Fact]
    public void TheIconIsAtTheStartOfTheTab()
    {
        var canvas = Paint(View(3), Looks(3, icon: true));

        Assert.Equal(Green, At(canvas, 10, 9));
        Assert.Equal(Green, At(canvas, 25, 24));
        Assert.Equal(Opaque | Light.Front, At(canvas, 9, 9));
        Assert.Equal(Opaque | Light.Front, At(canvas, 26, 9));
    }

    [Fact]
    public void InRightToLeftLanguagesTheIconIsAtTheRightOfTheTab()
    {
        var view = new StripView(StripLayout.Place(new Rectangle(100, 200, 600, 400), WorkArea, 96, 3, rightToLeft: true), [1, 2, 3], 0, -1, -1);

        var canvas = TabStripPainter.Paint(view, Looks(3, icon: true), -1, Light, Style with { RightToLeft = true });

        Assert.Equal(Green, At(canvas, 574, 9));
        Assert.Equal(Green, At(canvas, 589, 9));
        Assert.Equal(Opaque | Light.Front, At(canvas, 590, 9));
    }

    [Fact]
    public void ATabTooNarrowForItsTitleShowsOnlyItsIconCentered()
    {
        var view = new StripView(StripLayout.Place(new Rectangle(100, 200, 120, 400), WorkArea, 96, 3, rightToLeft: false), [1, 2, 3], 0, -1, -1);

        var canvas = TabStripPainter.Paint(view, Looks(3, icon: true, title: "Documents"), -1, Light, Style);

        Assert.Equal(Green, At(canvas, 12, 9));
        Assert.Equal(Green, At(canvas, 27, 9));
        Assert.Equal(Opaque | Light.Front, At(canvas, 11, 9));
        Assert.True(Region(canvas, 28, 3, 38, 28).All(pixel => pixel == (Opaque | Light.Front)));
    }

    [Fact]
    public void TheTitleFollowsTheIcon()
    {
        var canvas = Paint(View(3), Looks(3, title: "Documents"));

        Assert.Contains(Region(canvas, 32, 9, 190, 25), pixel => pixel != (Opaque | Light.Front));
    }

    [Fact]
    public void ALongTitleIsCutToFitItsTab()
    {
        var canvas = Paint(View(3), Looks(3, title: string.Concat(Enumerable.Repeat("A long title ", 20))));

        Assert.Contains(Region(canvas, 32, 9, 190, 25), pixel => pixel != (Opaque | Light.Front));
        Assert.True(Region(canvas, 190, 3, 198, 28).All(pixel => pixel == (Opaque | Light.Front)));
    }

    [Fact]
    public void AnEmptyTitleDrawsNoText()
    {
        var canvas = Paint(View(3), Looks(3));

        Assert.True(Region(canvas, 32, 9, 190, 25).All(pixel => pixel == (Opaque | Light.Front)));
    }

    [Fact]
    public void WhereADraggedTabWouldGoIsMarked()
    {
        var canvas = Paint(View(3) with { DropMark = 1 }, Looks(3));

        Assert.Equal(Opaque | Accent, At(canvas, 199, 15));
        Assert.Equal(Opaque | Accent, At(canvas, 200, 15));
        Assert.Equal(Opaque | Light.Background, At(canvas, 250, 15));
    }

    [Fact]
    public void AfterTheLastTabTheMarkStaysOnTheStrip()
    {
        var canvas = Paint(View(3) with { DropMark = 3 }, Looks(3));

        Assert.Equal(Opaque | Accent, At(canvas, 598, 15));
        Assert.Equal(Opaque | Accent, At(canvas, 599, 15));
    }

    [Fact]
    public void TheIconsSizeFollowsTheDisplaysScaleButNotTheTextSize()
    {
        Assert.Equal(16, TabStripPainter.IconPixels(Style));
        Assert.Equal(24, TabStripPainter.IconPixels(Style with { Dpi = 144 }));
        Assert.Equal(16, TabStripPainter.IconPixels(Style with { TextScale = 2 }));
    }

    private static StripView View(int count) =>
        new(StripLayout.Place(new Rectangle(100, 200, 600, 400), WorkArea, 96, count, rightToLeft: false), [.. Enumerable.Range(1, count).Select(index => (nint)index)], 0, -1, -1);

    private static TabLook[] Looks(int count, bool icon = false, string title = "") =>
        [.. Enumerable.Range(0, count).Select(_ => new TabLook(title, icon ? Enumerable.Repeat(Green, 16 * 16).ToArray() : null))];

    private static StripCanvas Paint(StripView view, TabLook[] looks) => TabStripPainter.Paint(view, looks, -1, Light, Style);

    private static uint At(StripCanvas canvas, int x, int y) => canvas.Pixels[(y * canvas.Width) + x];

    // The pixels from (left, top) up to, but not including, (right, bottom).
    private static uint[] Region(StripCanvas canvas, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        return [.. Enumerable.Range(top, bottom - top).SelectMany(y => Enumerable.Range(left, right - left).Select(x => At(canvas, x, y)))];
    }
}
