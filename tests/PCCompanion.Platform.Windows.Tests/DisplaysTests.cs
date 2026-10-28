using System;
using System.Drawing;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class DisplaysTests
{
    [Fact]
    public void GetAreasIncludesTheMainDisplay()
    {
        // The main display's top-left corner is the origin of the desktop.
        Assert.Contains(Displays.GetAreas(), area => area.Contains(0, 0) && !area.IsEmpty);
    }

    [Fact]
    public void DisplaysDoNotOverlap()
    {
        var areas = Displays.GetAreas();

        Assert.DoesNotContain(areas, area => areas.Any(other => other != area && other.IntersectsWith(area)));
    }

    [Fact]
    public void AWindowIsOnOneOfTheDisplays()
    {
        // Never shown, so nothing appears.
        using var window = new MessageWindow("PCCompanion.Tests." + Guid.NewGuid().ToString("N"));

        Assert.Contains(Displays.AreaOf(window.Handle) ?? Rectangle.Empty, Displays.GetAreas());
    }

    [Fact]
    public void NoWindowIsOnNoDisplay()
    {
        Assert.Null(Displays.AreaOf(0));
    }

    [Fact]
    public void ThePointerClipCoversEveryDisplayWhenNoAppConfinesIt()
    {
        var clip = InputState.PointerClip;

        Assert.NotNull(clip);
        Assert.All(Displays.GetAreas(), area => Assert.True(clip.Value.Contains(area), $"clip {clip}, display {area}"));
    }

    [Fact]
    public void TheMainDisplayHasADpi()
    {
        Assert.InRange(Displays.MainDisplayDpi(), 96u, 960u);
    }

    [Fact]
    public void EachDisplaysDetailsHaveItsAreaAWorkAreaInItAndADpi()
    {
        var details = Displays.GetDetails();

        Assert.Equal(Displays.GetAreas(), details.Select(display => display.Bounds));
        Assert.All(details, display =>
        {
            Assert.True(display.Bounds.Contains(display.WorkArea), $"{display.WorkArea} in {display.Bounds}");
            Assert.False(display.WorkArea.IsEmpty);
            Assert.InRange(display.Dpi, 96u, 960u);
        });
        Assert.Equal(Displays.MainDisplayDpi(), details.Single(display => display.Bounds.Location == Point.Empty).Dpi);
    }

    [Theory]
    [InlineData(96u, 1.0)]
    [InlineData(144u, 1.5)]
    [InlineData(192u, 2.0)]
    public void AScaleIsTheDpiOver96(uint dpi, double scale)
    {
        Assert.Equal(scale, new DisplayDetails(Rectangle.Empty, Rectangle.Empty, dpi).Scale);
    }

    [Theory]
    [InlineData(96u, 16)]
    [InlineData(120u, 20)]
    [InlineData(144u, 24)]
    [InlineData(192u, 32)]
    public void SmallIconsGrowWithTheDpi(uint dpi, int size)
    {
        Assert.Equal(size, Displays.SmallIconSize(dpi));
    }
}
