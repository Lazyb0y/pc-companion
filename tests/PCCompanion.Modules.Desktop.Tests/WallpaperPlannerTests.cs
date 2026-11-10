using System;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class WallpaperPlannerTests
{
    private static readonly Coordinates London = new(51.5, -0.1);
    private static readonly DynamicWallpaperSettings Settings = DynamicWallpaperSettings.First(@"C:\Day.jpg", @"C:\Night.jpg", WallpaperFit.Fill) with { Enabled = true };

    [Fact]
    public void AtNoonItsDayUntilSunset()
    {
        var noon = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

        var plan = WallpaperPlanner.Plan(Settings, noon, TimeZoneInfo.Utc, London, windowsModeDark: false);

        Assert.Equal(PeriodName.Of(SunPeriod.Day), plan.Period);
        Assert.Equal(@"C:\Day.jpg", plan.Pictures!.Picture);
        Assert.InRange(plan.NextLook!.Value, noon.AddHours(8), noon.AddHours(8.6));
    }

    [Fact]
    public void WithoutAPlaceDayIsFromSixToEighteen()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time");

        var morning = WallpaperPlanner.Plan(Settings, new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.FromHours(6)), zone, null, false);
        var evening = WallpaperPlanner.Plan(Settings, new DateTimeOffset(2026, 6, 21, 20, 0, 0, TimeSpan.FromHours(6)), zone, null, false);

        Assert.Equal(PeriodName.Of(SunPeriod.Day), morning.Period);
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 18, 0, 0, TimeSpan.FromHours(6)), morning.NextLook);
        Assert.Equal(PeriodName.Of(SunPeriod.Night), evening.Period);
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.FromHours(6)), evening.NextLook);
    }

    [Fact]
    public void ASunrisePictureThatCantBeUsedIsLeftOut()
    {
        var dawn = new DateTimeOffset(2026, 3, 20, 5, 50, 0, TimeSpan.Zero);
        var withSunrise = Settings with { Sun = Settings.Sun with { Sunrise = new() { Picture = @"C:\Dawn.jpg" } } };
        var unusable = Settings with { Sun = Settings.Sun with { Sunrise = new() { Picture = @"\\server\Dawn.jpg" } } };

        Assert.Equal(PeriodName.Of(SunPeriod.Sunrise), WallpaperPlanner.Plan(withSunrise, dawn, TimeZoneInfo.Utc, London, false).Period);
        Assert.Equal(PeriodName.Of(SunPeriod.Night), WallpaperPlanner.Plan(unusable, dawn, TimeZoneInfo.Utc, London, false).Period);
    }

    [Fact]
    public void FarNorthItLooksAgainInTwoDays()
    {
        var now = new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero);

        var plan = WallpaperPlanner.Plan(Settings, now, TimeZoneInfo.Utc, new Coordinates(78.2, 15.6), false);

        Assert.Equal(PeriodName.Of(SunPeriod.Night), plan.Period);
        Assert.Equal(now + Schedule.LookAhead, plan.NextLook);
    }

    [Fact]
    public void TimesThatCantBeUsedAreLeftOut()
    {
        var times = Settings with
        {
            ChangeWith = ChangeWith.Times,
            Times = [new() { At = "07:00", Picture = @"C:\A.jpg" }, new() { At = "bad", Picture = @"C:\B.jpg" }, new() { At = "07:00", Picture = @"C:\C.jpg" }, new() { At = "19:00", Picture = @"C:\D.jpg" }],
        };

        var plan = WallpaperPlanner.Plan(times, new DateTimeOffset(2026, 6, 21, 20, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, null, false);

        Assert.Equal(new PeriodName(ChangeWith.Times, 3), plan.Period);
        Assert.Equal(@"C:\D.jpg", plan.Pictures!.Picture);
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 7, 0, 0, TimeSpan.Zero), plan.NextLook);
    }

    [Fact]
    public void WithoutATimeThereIsNothingToShow()
    {
        var plan = WallpaperPlanner.Plan(Settings with { ChangeWith = ChangeWith.Times, Times = [] }, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, null, false);

        Assert.Null(plan.Pictures);
        Assert.Null(plan.NextLook);
    }

    [Fact]
    public void WindowsModeFollowsTheMode()
    {
        var mode = Settings with { ChangeWith = ChangeWith.WindowsMode };

        var dark = WallpaperPlanner.Plan(mode, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, null, windowsModeDark: true);
        var light = WallpaperPlanner.Plan(mode, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, null, windowsModeDark: false);

        Assert.Equal((PeriodName.Dark, @"C:\Night.jpg", (DateTimeOffset?)null), (dark.Period, dark.Pictures!.Picture, dark.NextLook));
        Assert.Equal((PeriodName.Light, @"C:\Day.jpg"), (light.Period, light.Pictures!.Picture));
    }

    [Fact]
    public void ThePlaceIsTheCoordinatesOrTheTimeZonesCity()
    {
        var coordinates = Settings with { Location = LocationSource.Coordinates, Latitude = 23.76, Longitude = 90.42 };

        Assert.Equal(new Coordinates(23.8, 90.4), WallpaperPlanner.PlaceOf(coordinates, () => London));
        Assert.Null(WallpaperPlanner.PlaceOf(coordinates with { Latitude = 99 }, () => London));
        Assert.Equal(London, WallpaperPlanner.PlaceOf(Settings, () => London));
        Assert.Null(WallpaperPlanner.PlaceOf(Settings, () => null));
    }

    [Fact]
    public void PeriodsHaveNames()
    {
        Assert.Equal("Sun.Sunrise", PeriodName.Of(SunPeriod.Sunrise).ToString());
        Assert.Equal("Times.2", new PeriodName(ChangeWith.Times, 2).ToString());
        Assert.Equal("WindowsMode.Dark", PeriodName.Dark.ToString());
    }
}
