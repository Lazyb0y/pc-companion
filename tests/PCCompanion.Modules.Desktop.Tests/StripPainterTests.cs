using System;
using System.Globalization;
using System.Linq;
using PCCompanion.Modules.Desktop.SystemMeters;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// Draws into memory only, with the fonts this PC has, so widths are compared rather than fixed.
public sealed class StripPainterTests
{
    private const uint White = 0xFFFFFF;
    private const int Height = 48;
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly StripStyle Default = new(Cpu: true, Memory: true, Network: true, StripLayout.ValuesAndNames, StripIcons.Color, RightToLeft: false);
    private static readonly StripTexts Values = new("42%", "61%", "1.2 Mbps", "80.0 Kbps");

    [Fact]
    public void EachMeterHasItsColumnInOrder()
    {
        var metrics = StripPainter.Measure(Default, 1, English);

        Assert.Equal([StripColumnKind.Cpu, StripColumnKind.Memory, StripColumnKind.Network], metrics.Columns.Select(column => column.Kind));
        Assert.True(metrics.Columns[0].X < metrics.Columns[1].X && metrics.Columns[1].X < metrics.Columns[2].X);
        Assert.Equal(metrics.Width, metrics.Columns[^1].X + metrics.Columns[^1].Width + 10);
    }

    [Fact]
    public void InRightToLeftLanguagesTheColumnsRunRightToLeftCpuFirst()
    {
        var metrics = StripPainter.Measure(Default with { RightToLeft = true }, 1, English);

        Assert.Equal([StripColumnKind.Network, StripColumnKind.Memory, StripColumnKind.Cpu], metrics.Columns.Select(column => column.Kind));
        Assert.Equal(StripPainter.Measure(Default, 1, English).Width, metrics.Width);
    }

    [Fact]
    public void LayoutsAndIconStylesChangeTheWidth()
    {
        int Width(StripStyle style) => StripPainter.Measure(style, 1, English).Width;

        var valuesAndNames = Width(Default);
        Assert.True(Width(Default with { Icons = StripIcons.None }) < valuesAndNames);
        Assert.Equal(Width(Default with { Icons = StripIcons.OneColor }), valuesAndNames);
        Assert.True(Width(Default with { Layout = StripLayout.LargeValues }) > valuesAndNames);
        Assert.True(Width(Default with { Layout = StripLayout.Compact }) < valuesAndNames);
        Assert.Equal([StripColumnKind.CpuAndMemory, StripColumnKind.Network], StripPainter.Measure(Default with { Layout = StripLayout.Compact }, 1, English).Columns.Select(column => column.Kind));
    }

    [Fact]
    public void Windows10DrawsInItsOwnFonts()
    {
        Assert.Equal(StripFonts.Windows10, StripFonts.For(19045));
        Assert.Equal(StripFonts.Windows11, StripFonts.For(22000));
        Assert.Equal(StripFonts.Windows10, StripPainter.Measure(Default, 1, English, StripFonts.Windows10).Fonts);
        Assert.Equal(StripFonts.Windows11, StripPainter.Measure(Default, 1, English).Fonts);
    }

    [Fact]
    public void AMeterTurnedOffLeavesOutItsColumn()
    {
        var withoutMemory = StripPainter.Measure(Default with { Memory = false }, 1, English);

        Assert.Equal([StripColumnKind.Cpu, StripColumnKind.Network], withoutMemory.Columns.Select(column => column.Kind));
        Assert.True(withoutMemory.Width < StripPainter.Measure(Default, 1, English).Width);
        Assert.Equal(0, StripPainter.Measure(Default with { Cpu = false, Memory = false, Network = false }, 1, English).Width);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheStripGrowsWithTheScale(double scale)
    {
        var atOne = StripPainter.Measure(Default, 1, English).Width;

        var scaled = StripPainter.Measure(Default, scale, English).Width;

        Assert.InRange(scaled, atOne * scale * 0.9, atOne * scale * 1.1);
    }

    [Fact]
    public void ValuesHaveFixedPlacesSizedForTheirWidestValue()
    {
        var metrics = StripPainter.Measure(Default, 1, English);

        var narrow = StripPainter.Paint(metrics, Height, new StripTexts("5%", "5%", "0 Kbps", "0 Kbps"), new StripColors(White, false), highlighted: false);
        var wide = StripPainter.Paint(metrics, Height, new StripTexts("100%", "100%", "999 Mbps", "99.9 Kbps"), new StripColors(White, false), highlighted: false);

        Assert.Equal(narrow.Length, wide.Length);
        Assert.Equal(metrics.Width * Height, wide.Length);
    }

    [Fact]
    public void TheBackgroundIsAlmostTransparentAndTheHighlightShows()
    {
        var metrics = StripPainter.Measure(Default, 1, English);

        var plain = StripPainter.Paint(metrics, Height, Values, new StripColors(White, false), highlighted: false);
        var highlighted = StripPainter.Paint(metrics, Height, Values, new StripColors(White, false), highlighted: true);

        // A corner, outside the highlight, and a spot inside it between the columns.
        Assert.Equal(1u, plain[0] >> 24);
        Assert.Equal(1u, highlighted[0] >> 24);
        var between = (5 * metrics.Width) + metrics.Columns[1].X - 3;
        Assert.True(highlighted[between] >> 24 > plain[between] >> 24);
    }

    [Fact]
    public void ColorIconsAreInColorExceptInHighContrast()
    {
        var metrics = StripPainter.Measure(Default, 1, English);

        Assert.Contains(StripPainter.Paint(metrics, Height, Values, new StripColors(White, false), false), IsColored);
        Assert.DoesNotContain(StripPainter.Paint(metrics, Height, Values, new StripColors(White, true), false), IsColored);
        Assert.DoesNotContain(StripPainter.Paint(StripPainter.Measure(Default with { Icons = StripIcons.OneColor }, 1, English), Height, Values, new StripColors(White, false), false), IsColored);
    }

    [Fact]
    public void TextIsInTheTextColor()
    {
        var metrics = StripPainter.Measure(Default with { Icons = StripIcons.None }, 1, English);

        var pixels = StripPainter.Paint(metrics, Height, Values, new StripColors(0x000000, false), false);

        Assert.All(pixels, pixel => Assert.Equal(0u, pixel & 0xFFFFFF));
        Assert.Contains(pixels, pixel => pixel >> 24 == 255);
    }

    [Theory]
    [InlineData(StripLayout.LargeValues)]
    [InlineData(StripLayout.ValuesAndNames)]
    public void ACpuColumnIsCenteredOnTheTaskbarByWhatsDrawn(StripLayout layout)
    {
        var metrics = StripPainter.Measure(new StripStyle(Cpu: true, Memory: false, Network: false, layout, StripIcons.None, false), 1, English);

        var pixels = StripPainter.Paint(metrics, Height, Values, new StripColors(White, false), false);

        var rows = Enumerable.Range(0, Height).Where(y => Enumerable.Range(0, metrics.Width).Any(x => pixels[(y * metrics.Width) + x] >> 24 > 24)).ToList();
        Assert.InRange(rows[0] + rows[^1] + 1 - Height, -2, 2);
    }

    // A pixel whose color isn't a shade of gray.
    private static bool IsColored(uint pixel)
    {
        var (red, green, blue) = ((pixel >> 16) & 0xFF, (pixel >> 8) & 0xFF, pixel & 0xFF);
        return Math.Abs((int)red - (int)green) > 8 || Math.Abs((int)green - (int)blue) > 8;
    }
}
