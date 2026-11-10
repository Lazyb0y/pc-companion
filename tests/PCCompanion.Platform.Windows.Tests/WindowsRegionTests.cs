using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class WindowsRegionTests
{
    [Fact]
    public void TheCountryOrRegionIsACode()
    {
        var code = WindowsRegion.CountryOrRegion();

        Assert.NotNull(code);
        Assert.Matches("^([A-Z]{2}|[0-9]{3})$", code);
    }
}
