using System;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class SunTests
{
    private const double LondonLatitude = 51.5074;
    private const double LondonLongitude = -0.1278;

    [Fact]
    public void AtMidsummerNoonTheSunIsAsHighAsTheLatitudeAllows()
    {
        // 90° - latitude + the sun's declination at the solstice, 23.44°.
        var sun = Sun.At(new DateTimeOffset(2026, 6, 21, 12, 2, 0, TimeSpan.Zero), LondonLatitude, LondonLongitude);

        Assert.Equal(90 - LondonLatitude + 23.44, sun.Elevation, 0.1);
    }

    [Fact]
    public void AtMidwinterNoonTheSunIsAsLowAsTheLatitudeAllows()
    {
        var sun = Sun.At(new DateTimeOffset(2026, 12, 21, 11, 59, 0, TimeSpan.Zero), LondonLatitude, LondonLongitude);

        Assert.Equal(90 - LondonLatitude - 23.44, sun.Elevation, 0.1);
    }

    [Fact]
    public void SolarNoonFollowsTheEquationOfTime()
    {
        // Early in November, the sun crosses the Greenwich meridian about 16 minutes before 12:00 UTC.
        var sun = Sun.At(new DateTimeOffset(2026, 11, 3, 11, 43, 35, TimeSpan.Zero), 0, 0);

        Assert.InRange(sun.HourAngle, -0.25, 0.25);
    }

    [Fact]
    public void TheSunRisesBeforeSolarNoonAndSetsAfter()
    {
        Assert.True(Sun.At(new DateTimeOffset(2026, 6, 21, 6, 0, 0, TimeSpan.Zero), LondonLatitude, LondonLongitude).IsRising);
        Assert.False(Sun.At(new DateTimeOffset(2026, 6, 21, 18, 0, 0, TimeSpan.Zero), LondonLatitude, LondonLongitude).IsRising);
    }

    [Fact]
    public void TheSunIsBelowTheHorizonAtNight()
    {
        Assert.True(Sun.At(new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), LondonLatitude, LondonLongitude).Elevation < 0);
    }
}
