using System;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class SunScheduleTests
{
    private static readonly SunSchedule London = new(51.5074, -0.1278, HasSunrise: false, HasSunset: false);
    private static readonly SunSchedule Sydney = new(-33.8688, 151.2093, HasSunrise: false, HasSunset: false);
    private static readonly SunSchedule Tromso = new(69.6492, 18.9553, HasSunrise: true, HasSunset: true);
    private static readonly SunSchedule Longyearbyen = new(78.2232, 15.6267, HasSunrise: true, HasSunset: true);
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

    [Theory]
    [InlineData(6, 21, 3, 43, 20, 21)]
    [InlineData(12, 21, 8, 4, 15, 53)]
    public void DayAndNightChangeAtSunriseAndSunsetInLondon(int month, int day, int riseHour, int riseMinute, int setHour, int setMinute)
    {
        var midnight = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero);

        var sunrise = London.NextChange(midnight)!.Value;
        var sunset = London.NextChange(sunrise.At)!.Value;

        Assert.Equal(SunPeriod.Day, sunrise.Period);
        AssertNear(midnight.AddHours(riseHour).AddMinutes(riseMinute), sunrise.At);
        Assert.Equal(SunPeriod.Night, sunset.Period);
        AssertNear(midnight.AddHours(setHour).AddMinutes(setMinute), sunset.At);
    }

    [Fact]
    public void InTheSouthernSummerTheDayIsLong()
    {
        // Sydney at the December solstice: sunrise 5:41 and sunset 20:05 local time (UTC+11).
        var sunrise = Sydney.NextChange(new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero))!.Value;
        var sunset = Sydney.NextChange(sunrise.At)!.Value;

        AssertNear(new DateTimeOffset(2026, 12, 20, 18, 41, 0, TimeSpan.Zero), sunrise.At, TimeSpan.FromMinutes(3));
        AssertNear(new DateTimeOffset(2026, 12, 21, 9, 5, 0, TimeSpan.Zero), sunset.At, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void WithSunriseAndSunsetPicturesTheDayHasFourPeriodsAroundSunriseAndSunset()
    {
        var all = London with { HasSunrise = true, HasSunset = true };

        var spans = all.Day(new DateOnly(2026, 3, 20), TimeZoneInfo.Utc);

        Assert.Equal([SunPeriod.Night, SunPeriod.Sunrise, SunPeriod.Day, SunPeriod.Sunset, SunPeriod.Night], spans.Select(span => span.Period));
        var sunrise = London.NextChange(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero))!.Value.At;
        Assert.InRange(sunrise, spans[1].Start, spans[1].End);
        Assert.True(spans[1].End - spans[1].Start < TimeSpan.FromHours(1.5));
    }

    [Fact]
    public void ASunriseWithoutAPictureLeavesNightUntilTheSunRises()
    {
        var withSunrise = London with { HasSunrise = true };
        var dawn = new DateTimeOffset(2026, 3, 20, 5, 50, 0, TimeSpan.Zero);

        Assert.Equal(SunPeriod.Sunrise, withSunrise.PeriodAt(dawn));
        Assert.Equal(SunPeriod.Night, London.PeriodAt(dawn));
    }

    [Fact]
    public void InAPolarNightTheSunComesNearTheHorizonAroundNoon()
    {
        var spans = Tromso.Day(new DateOnly(2026, 12, 21), Oslo);

        Assert.Equal([SunPeriod.Night, SunPeriod.Sunrise, SunPeriod.Sunset, SunPeriod.Night], spans.Select(span => span.Period));
        Assert.Equal((false, false), Tromso.RisesAndSets(new DateOnly(2026, 12, 21), Oslo));
        Assert.All(
            (Tromso with { HasSunrise = false, HasSunset = false }).Day(new DateOnly(2026, 12, 21), Oslo),
            span => Assert.Equal(SunPeriod.Night, span.Period));
    }

    [Fact]
    public void UnderTheMidnightSunTheSunComesNearTheHorizonAroundMidnight()
    {
        var spans = Tromso.Day(new DateOnly(2026, 6, 21), Oslo);

        Assert.Equal([SunPeriod.Sunset, SunPeriod.Sunrise, SunPeriod.Day, SunPeriod.Sunset], spans.Select(span => span.Period));
        Assert.Equal((false, false), Tromso.RisesAndSets(new DateOnly(2026, 6, 21), Oslo));
    }

    [Fact]
    public void FurtherNorthItIsNightOrDayAllDay()
    {
        Assert.Equal([SunPeriod.Night], Longyearbyen.Day(new DateOnly(2026, 12, 21), Oslo).Select(span => span.Period));
        Assert.Equal([SunPeriod.Day], Longyearbyen.Day(new DateOnly(2026, 6, 21), Oslo).Select(span => span.Period));
        Assert.Null(Longyearbyen.NextChange(new DateTimeOffset(2026, 12, 21, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheSunRisesAndSetsOnAnOrdinaryDay()
    {
        Assert.Equal((true, true), London.RisesAndSets(new DateOnly(2026, 3, 20), TimeZoneInfo.Utc));
    }

    [Fact]
    public void TheNextChangeIsFoundToTheSecond()
    {
        var change = London.NextChange(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero))!.Value;

        Assert.Equal(SunPeriod.Night, London.PeriodAt(change.At.AddSeconds(-1)));
        Assert.Equal(SunPeriod.Day, London.PeriodAt(change.At));
    }

    private static void AssertNear(DateTimeOffset expected, DateTimeOffset actual) => AssertNear(expected, actual, Tolerance);

    private static void AssertNear(DateTimeOffset expected, DateTimeOffset actual, TimeSpan tolerance) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
