using System;
using System.Drawing;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own frame with unique class names, shown far off the screen.
public sealed class WindowFrameTests
{
    private static readonly Rectangle OffScreen = new(-30000, -30000, 40, 20);

    [Fact]
    public void ShowAndHideTrackWhetherTheFrameIsShown()
    {
        using var frame = new WindowFrame(UniqueClassName());

        Assert.False(frame.IsShown);
        frame.Show(OffScreen, 4, 0xE81123);
        Assert.True(frame.IsShown);
        frame.Show(OffScreen with { Width = 60 }, 2, 0x8A8A8A);
        Assert.True(frame.IsShown);
        frame.Hide();
        Assert.False(frame.IsShown);
    }

    [Fact]
    public void AFrameNeedsAThickness()
    {
        using var frame = new WindowFrame(UniqueClassName());

        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Show(OffScreen, 0, 0xE81123));
    }

    [Fact]
    public void AFrameAroundNothingShowsOnlyItsTopAndBottom()
    {
        using var frame = new WindowFrame(UniqueClassName());

        frame.Show(OffScreen with { Height = 0 }, 4, 0xE81123);

        Assert.True(frame.IsShown);
    }

    [Fact]
    public void DisposingTwiceIsFine()
    {
        var frame = new WindowFrame(UniqueClassName());
        frame.Dispose();
        frame.Dispose();
    }
}
