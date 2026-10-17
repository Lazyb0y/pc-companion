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
}
