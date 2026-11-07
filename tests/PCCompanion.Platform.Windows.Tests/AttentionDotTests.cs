using System;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Draws into memory only; nothing is shown.
public sealed class AttentionDotTests
{
    private const uint White = 0xFF_FF_FF_FF;
    private const uint Accent = 0x00_78_D4;

    [Fact]
    public void TheDotIsDrawnInTheBottomRightCorner()
    {
        var pixels = Icon(16, White);

        AttentionDot.Draw(pixels, 16, Accent);

        // The dot is 6 pixels wide, around the point (13, 13).
        Assert.Equal(0xFF_00_78_D4u, At(pixels, 16, 12, 12));
        Assert.Equal(0xFF_00_78_D4u, At(pixels, 16, 13, 13));
        Assert.Equal(0xFF_00_78_D4u, At(pixels, 16, 14, 14));
        Assert.Equal(White, At(pixels, 16, 0, 0));
        Assert.Equal(White, At(pixels, 16, 15, 0));
        Assert.Equal(White, At(pixels, 16, 0, 15));
        Assert.Equal(White, At(pixels, 16, 8, 8));
    }

    [Fact]
    public void AGapClearsTheIconAroundTheDot()
    {
        var pixels = Icon(32, White);

        AttentionDot.Draw(pixels, 32, Accent);

        // The dot is 12 pixels wide, around the point (26, 26), and the gap 2.
        Assert.Equal(0u, At(pixels, 32, 19, 26));
        Assert.Equal(0u, At(pixels, 32, 26, 19));
        Assert.Equal(White, At(pixels, 32, 17, 26));
        Assert.Equal(White, At(pixels, 32, 26, 17));
    }

    [Fact]
    public void TheDotHasSmoothEdges()
    {
        var pixels = Icon(16, 0);

        AttentionDot.Draw(pixels, 16, Accent);

        Assert.Contains(pixels, pixel => pixel >> 24 is > 0 and < 255);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void TheDotIsThreeEighthsOfTheIconWide(int size)
    {
        var pixels = Icon(size, 0);

        AttentionDot.Draw(pixels, size, Accent);

        // The pixels at least half covered, on the row through the dot's center.
        var width = size * 3.0 / 8;
        var row = size - (int)Math.Ceiling(width / 2);
        var covered = Enumerable.Range(0, size).Count(x => At(pixels, size, x, row) >> 24 >= 128);
        Assert.InRange(covered, (int)Math.Floor(width) - 1, (int)Math.Ceiling(width) + 1);
    }

    [Fact]
    public void ThePixelsStayPremultiplied()
    {
        var pixels = Icon(20, 0x80_40_20_10);

        AttentionDot.Draw(pixels, 20, 0xFF_FF_FF);

        Assert.All(pixels, pixel =>
        {
            var alpha = pixel >> 24;
            Assert.True(((pixel >> 16) & 0xFF) <= alpha && ((pixel >> 8) & 0xFF) <= alpha && (pixel & 0xFF) <= alpha, $"0x{pixel:X8}");
        });
    }

    [Fact]
    public void PixelsMustFillTheIcon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AttentionDot.Draw(new uint[10], 16, Accent));
    }

    [Fact]
    public void TheColorsAreRead()
    {
        Assert.InRange(AttentionDot.TaskbarColor(), 0u, 0xFF_FF_FFu);
        Assert.InRange(WindowsTheme.AccentColor(), 0u, 0xFF_FF_FFu);
        Assert.NotEqual(WindowsTheme.AccentColor(), WindowsTheme.LightAccentColor());
    }

    private static uint[] Icon(int size, uint pixel) => Enumerable.Repeat(pixel, size * size).ToArray();

    private static uint At(uint[] pixels, int size, int x, int y) => pixels[(y * size) + x];
}
