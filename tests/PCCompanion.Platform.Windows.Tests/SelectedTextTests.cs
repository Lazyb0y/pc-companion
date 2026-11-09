using System;
using System.Drawing;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test checks only a hidden window of its own; the window in front and its text aren't read.
public sealed class SelectedTextTests
{
    [Fact]
    public void NoWindowIsntRead()
    {
        Assert.Equal(SelectedTextApp.NoWindow, SelectedText.CheckWindow(0));
    }

    [Fact]
    public void AWindowOfAProcessAtTheSameIntegrityLevelIsRead()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Equal(SelectedTextApp.Readable, SelectedText.CheckWindow(window.Handle));
    }

    // Windows' window in place of an app that stopped responding has this class, in another process.
    [Fact]
    public void TheWindowInPlaceOfAnAppThatStoppedRespondingIsntRead()
    {
        using var window = new MessageWindow("Ghost");

        Assert.Equal(SelectedTextApp.NotResponding, SelectedText.CheckWindow(window.Handle));
    }

    [Fact]
    public void TheLastLineIsTheLastRectangle()
    {
        Assert.Equal(new Rectangle(30, 60, 50, 20), SelectedText.LastLine([10, 20, 100, 18, 30, 60, 50, 20]));
    }

    [Fact]
    public void TheLastLineCoversItsFractionsOfPixels()
    {
        Assert.Equal(Rectangle.FromLTRB(10, 20, 110, 38), SelectedText.LastLine([10.5, 20.25, 99.5, 17.5]));
    }

    [Fact]
    public void ALineWithNoWidthIsOnePixelWide()
    {
        Assert.Equal(new Rectangle(30, 60, 1, 20), SelectedText.LastLine([30, 60, 0, 20]));
    }

    [Theory]
    [InlineData(30, 60, 50, 0)]
    [InlineData(30, 60, -5, 20)]
    [InlineData(double.NaN, 60, 50, 20)]
    [InlineData(30, double.PositiveInfinity, 50, 20)]
    [InlineData(-1e9, 60, 50, 20)]
    [InlineData(30, 60, 1e9, 20)]
    public void ARectangleThatIsntALineIsntUsed(double left, double top, double width, double height)
    {
        Assert.Null(SelectedText.LastLine([left, top, width, height]));
    }

    [Fact]
    public void ALineOnNoDisplayIsntUsed()
    {
        Assert.Null(SelectedText.LastLine([-200_000, -200_000, 50, 20]));
    }

    [Fact]
    public void NoRectanglesGiveNoLine()
    {
        Assert.Null(SelectedText.LastLine([]));
        Assert.Null(SelectedText.LastLine([30, 60, 50]));
    }

    [Fact]
    public void ADisposedReaderReadsNothing()
    {
        var reader = new SelectedTextReader();
        reader.Dispose();
        reader.Dispose();

        // It throws as it's called, before any task.
        Assert.Throws<ObjectDisposedException>(() => { _ = reader.ReadAsync(TimeSpan.Zero); });
    }
}
