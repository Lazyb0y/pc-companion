using System;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class TimeZonePlacesTests
{
    private static readonly TimeZoneInfo WesternEurope = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

    [Fact]
    public void AWindowsTimeZoneHasItsMainCity()
    {
        AssertPlace(23.72, 90.42, TimeZonePlaces.Of(TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time"), "BD"));
    }

    [Fact]
    public void TheCountryOrRegionChoosesTheCity()
    {
        AssertPlace(52.37, 4.90, TimeZonePlaces.Of(WesternEurope, "NL"));
        AssertPlace(52.50, 13.37, TimeZonePlaces.Of(WesternEurope, "DE"));
        AssertPlace(52.50, 13.37, TimeZonePlaces.Of(WesternEurope, "de"));
    }

    [Fact]
    public void WithoutACountryOrRegionTheTimeZoneHasItsUsualCity()
    {
        var usual = TimeZonePlaces.Of(WesternEurope, null);

        Assert.NotNull(usual);
        Assert.Equal(usual, TimeZonePlaces.Of(WesternEurope, "419"));
        Assert.Equal(usual, TimeZonePlaces.Of(WesternEurope, "ZZ"));
    }

    [Fact]
    public void OlderNamesStandForTheirZones()
    {
        // Windows' time zones convert to some older names, such as Asia/Calcutta.
        AssertPlace(22.53, 88.37, TimeZonePlaces.OfZone("Asia/Calcutta"));
        Assert.Equal(TimeZonePlaces.OfZone("Asia/Kolkata"), TimeZonePlaces.OfZone("Asia/Calcutta"));
        AssertPlace(22.53, 88.37, TimeZonePlaces.Of(TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"), "IN"));
    }

    [Fact]
    public void ATimeZoneWithoutAPlaceHasNone()
    {
        Assert.Null(TimeZonePlaces.Of(TimeZoneInfo.Utc, "BD"));
        Assert.Null(TimeZonePlaces.OfZone("Etc/UTC"));
        Assert.Null(TimeZonePlaces.OfZone("Nowhere/Town"));
    }

    [Theory]
    [InlineData("+2343+09025", 23.716667, 90.416667)]
    [InlineData("-3352+15113", -33.866667, 151.216667)]
    [InlineData("+515248-1763929", 51.88, -176.658056)]
    public void CoordinatesAreReadInDegreesMinutesAndSeconds(string text, double latitude, double longitude)
    {
        AssertPlace(latitude, longitude, TimeZonePlaces.Parse(text), precision: 0.0001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+2343")]
    [InlineData("2343+09025")]
    [InlineData("+2360+09025")]
    [InlineData("+9900+09025")]
    [InlineData("+2343+19000")]
    [InlineData("+23a3+09025")]
    [InlineData("+234+09025")]
    public void AnythingElseIsntCoordinates(string text) => Assert.Null(TimeZonePlaces.Parse(text));

    private static void AssertPlace(double latitude, double longitude, Coordinates? place, double precision = 0.02)
    {
        Assert.NotNull(place);
        Assert.Equal(latitude, place.Value.Latitude, precision);
        Assert.Equal(longitude, place.Value.Longitude, precision);
    }
}
