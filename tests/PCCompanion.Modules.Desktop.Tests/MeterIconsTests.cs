using System;
using System.Linq;
using PCCompanion.Modules.Desktop.SystemMeters;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// Draws into memory only.
public sealed class MeterIconsTests
{
    private const uint White = 0xFFFFFF;
    private const uint Black = 0x000000;

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void NumbersFitAndAreCentered(int size)
    {
        foreach (var percent in new[] { 0, 7, 42, 88, 100 })
        {
            var ink = Ink(MeterIcons.Number(percent, size, White), size);

            Assert.False(ink.IsEmpty);
            Assert.InRange(ink.Left + ink.Width, 1, size);
            Assert.InRange(ink.Top + ink.Height, 1, size);
            Assert.InRange((2 * ink.Left) + ink.Width - size, -1, 1);
            Assert.InRange((2 * ink.Top) + ink.Height - size, -1, 1);
        }
    }

    [Fact]
    public void OneAndTwoDigitsShareASizeAndAHundredIsSmaller()
    {
        var seven = Ink(MeterIcons.Number(7, 16, White), 16);
        var fortyTwo = Ink(MeterIcons.Number(42, 16, White), 16);
        var hundred = Ink(MeterIcons.Number(100, 16, White), 16);

        Assert.Equal(fortyTwo.Height, seven.Height);
        Assert.True(hundred.Height < fortyTwo.Height);
        Assert.InRange(fortyTwo.Width, 13, 16); // two digits fill the width
    }

    [Fact]
    public void IconsAreOneColorOnTransparency()
    {
        var white = MeterIcons.Number(88, 16, White);
        var black = MeterIcons.Number(88, 16, Black);

        Assert.Equal(0u, white[0]); // a corner
        Assert.All(white, pixel => Assert.True(pixel == 0 || ((pixel >> 24) == ((pixel >> 16) & 0xFF) && (pixel & 0xFF) == ((pixel >> 16) & 0xFF))));
        Assert.All(black, pixel => Assert.Equal(0u, pixel & 0xFFFFFF));
        Assert.Contains(black, pixel => pixel >> 24 == 255);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000, 0)]
    [InlineData(1_000_000, 0.5)]
    [InlineData(1_000_000_000, 1)]
    [InlineData(10_000_000_000, 1)]
    public void SlotsFillOnALogarithmicScale(double bitsPerSecond, double fraction)
    {
        Assert.Equal(fraction, MeterIcons.SlotFraction(bitsPerSecond), 6);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void SlotsAreOutlinedAndFillFromTheBottom(int size)
    {
        var empty = MeterIcons.NetworkSlots(0, 0, size, White);
        var full = MeterIcons.NetworkSlots(1e9, 1e9, size, White);
        var trickle = MeterIcons.NetworkSlots(2_000, 0, size, White);

        // The outline shows even with no traffic, and full slots have more pixels.
        Assert.True(Count(empty) > 0);
        Assert.True(Count(full) > Count(empty));

        // Any traffic above 1 Kbps fills at least one row of the receive slot, the left one.
        Assert.True(Count(trickle) > Count(empty));
        Assert.Equal(Count(trickle) - Count(empty), CountLeftHalf(trickle, size) - CountLeftHalf(empty, size));
    }

    private static int Count(uint[] pixels) => pixels.Count(pixel => pixel >> 24 > 0);

    private static int CountLeftHalf(uint[] pixels, int size) =>
        Enumerable.Range(0, pixels.Length).Count(i => i % size < size / 2 && pixels[i] >> 24 > 0);

    private static InkBox Ink(uint[] pixels, int size)
    {
        int left = size, top = size, right = -1, bottom = -1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (pixels[(y * size) + x] >> 24 > 24)
                {
                    (left, right) = (Math.Min(left, x), Math.Max(right, x));
                    (top, bottom) = (Math.Min(top, y), Math.Max(bottom, y));
                }
            }
        }

        return right < 0 ? default : new InkBox(left, top, right - left + 1, bottom - top + 1);
    }
}
