using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Draws into memory only; nothing is shown.
public sealed class DrawingTests
{
    [Fact]
    public void TextIsDrawnAsCoverageInItsBox()
    {
        var mask = TextMask.Render("42", "Segoe UI", 600, 16);

        Assert.Equal(mask.Width * mask.Height, mask.Coverage.Length);
        Assert.InRange(mask.Height, 16, 32);
        Assert.Contains(mask.Coverage, value => value == 255);
        Assert.Contains(mask.Coverage, value => value is > 0 and < 255);
    }

    [Fact]
    public void LongerTextIsWider()
    {
        Assert.True(TextMask.Render("100%", "Segoe UI", 600, 12).Width > TextMask.Render("7%", "Segoe UI", 600, 12).Width);
    }

    [Fact]
    public void RightToLeftTextKeepsItsWidthAndPutsALeftToRightPartInItsPlace()
    {
        const string Text = "تم تشغيل Caps Lock";

        var leftToRight = TextMask.Render(Text, "Segoe UI", 400, 14);
        var rightToLeft = TextMask.Render(Text, "Segoe UI", 400, 14, rightToLeft: true);

        Assert.Equal(leftToRight.Width, rightToLeft.Width);
        Assert.Contains(rightToLeft.Coverage, value => value == 255);
        Assert.False(leftToRight.Coverage.SequenceEqual(rightToLeft.Coverage));
    }

    [Fact]
    public void LeftToRightTextLooksTheSameInEitherDirection()
    {
        Assert.Equal(TextMask.Render("Caps Lock", "Segoe UI", 400, 14).Coverage, TextMask.Render("Caps Lock", "Segoe UI", 400, 14, rightToLeft: true).Coverage);
    }

    [Fact]
    public void EmptyTextDrawsNothing()
    {
        var mask = TextMask.Render(string.Empty, "Segoe UI", 400, 12);

        Assert.All(mask.Coverage, value => Assert.Equal(0, value));
    }

    [Fact]
    public void AnIconIsMadeFromPixels()
    {
        var pixels = Enumerable.Repeat(0xFF_FF_FF_FFu, 16 * 16).ToArray();

        using var icon = IconImage.FromPixels(pixels, 16);

        Assert.Equal(16, icon.Size);
    }

    [Fact]
    public void PixelsMustFillTheIcon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IconImage.FromPixels(new uint[10], 16));
    }
}

// Counts the process's GDI and USER objects, so it runs on its own, after the parallel tests.
[CollectionDefinition(nameof(HandleCounting), DisableParallelization = true)]
public sealed class HandleCounting;

[Collection(nameof(HandleCounting))]
public sealed class DrawingLeakTests
{
    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    [Fact]
    public void DrawingLeaksNoHandles()
    {
        var pixels = new uint[32 * 32];
        Draw();
        var gdi = GetGuiResources(CurrentProcess, GdiObjects);
        var user = GetGuiResources(CurrentProcess, UserObjects);

        for (var i = 0; i < 1000; i++)
        {
            Draw();
        }

        Assert.Equal(gdi, GetGuiResources(CurrentProcess, GdiObjects));
        Assert.Equal(user, GetGuiResources(CurrentProcess, UserObjects));

        void Draw()
        {
            _ = TextMask.Render("88%", "Segoe UI", 600, 12);
            using var icon = IconImage.FromPixels(pixels, 32);
        }
    }

    private static nint CurrentProcess => -1;

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);
}
