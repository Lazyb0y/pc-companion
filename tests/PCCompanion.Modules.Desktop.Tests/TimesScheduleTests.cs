using System;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class TimesScheduleTests
{
    // Central European Time: UTC+1, and UTC+2 from 29 March to 25 October 2026.
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    private static readonly TimeOnly Seven = new(7, 0);
    private static readonly TimeOnly Nineteen = new(19, 0);

    [Fact]
    public void ThePictureOfTheTimeThatCameLastShows()
    {
        var schedule = new TimesSchedule([Seven, Nineteen], Berlin);

        Assert.Equal(0, schedule.PeriodAt(Utc(2026, 7, 1, 10, 0)));
        Assert.Equal(1, schedule.PeriodAt(Utc(2026, 7, 1, 18, 0)));
        Assert.Equal(1, schedule.PeriodAt(Utc(2026, 7, 1, 1, 0)));
        Assert.Equal(0, schedule.PeriodAt(Utc(2026, 7, 1, 5, 0)));
    }

    [Fact]
    public void TheNextChangeIsTheNextTime()
    {
        var schedule = new TimesSchedule([Nineteen, Seven], Berlin);

        Assert.Equal(new ScheduleChange<int>(Utc(2026, 7, 1, 17, 0), 0), schedule.NextChange(Utc(2026, 7, 1, 10, 0)));
        Assert.Equal(new ScheduleChange<int>(Utc(2026, 7, 2, 5, 0), 1), schedule.NextChange(Utc(2026, 7, 1, 17, 0)));
    }

    [Fact]
    public void ATimeTheClockSkipsComesWhenItJumps()
    {
        // On 29 March 2026 the clock goes from 2:00 to 3:00, at 1:00 UTC.
        var schedule = new TimesSchedule([new TimeOnly(2, 30), new TimeOnly(12, 0)], Berlin);

        Assert.Equal(new ScheduleChange<int>(Utc(2026, 3, 29, 1, 0), 0), schedule.NextChange(Utc(2026, 3, 28, 23, 0)));
        Assert.Equal(0, schedule.PeriodAt(Utc(2026, 3, 29, 1, 0)));
        Assert.Equal(1, schedule.PeriodAt(Utc(2026, 3, 29, 0, 59)));
    }

    [Fact]
    public void ATimeTheClockShowsTwiceComesTheFirstTime()
    {
        // On 25 October 2026 the clock goes from 3:00 back to 2:00, at 1:00 UTC.
        var schedule = new TimesSchedule([new TimeOnly(2, 30), new TimeOnly(12, 0)], Berlin);

        var first = schedule.NextChange(Utc(2026, 10, 24, 22, 0))!.Value;

        Assert.Equal(new ScheduleChange<int>(Utc(2026, 10, 25, 0, 30), 0), first);
        Assert.Equal(new ScheduleChange<int>(Utc(2026, 10, 25, 11, 0), 1), schedule.NextChange(first.At));
    }

    [Fact]
    public void TheTimesFollowTheClockOfTheTimeZone()
    {
        var schedule = new TimesSchedule([Seven, Nineteen], Tokyo);

        Assert.Equal(new ScheduleChange<int>(Utc(2026, 7, 1, 22, 0), 0), schedule.NextChange(Utc(2026, 7, 1, 12, 0)));
    }

    [Fact]
    public void ADayIsSplitAtItsTimes()
    {
        var schedule = new TimesSchedule([Seven, Nineteen], Berlin);

        var spans = schedule.Day(new DateOnly(2026, 7, 1));

        Assert.Equal(
            [
                new PeriodSpan<int>(1, Utc(2026, 6, 30, 22, 0), Utc(2026, 7, 1, 5, 0)),
                new PeriodSpan<int>(0, Utc(2026, 7, 1, 5, 0), Utc(2026, 7, 1, 17, 0)),
                new PeriodSpan<int>(1, Utc(2026, 7, 1, 17, 0), Utc(2026, 7, 1, 22, 0)),
            ],
            spans);
    }

    [Fact]
    public void ADayWhenTheClockGoesBackHas25Hours()
    {
        var (start, end) = Schedule.LocalDay(new DateOnly(2026, 10, 25), Berlin);

        Assert.Equal(TimeSpan.FromHours(25), end - start);
        Assert.Equal(25, new TimesSchedule([Seven], Berlin).Day(new DateOnly(2026, 10, 25)).Sum(span => (span.End - span.Start).TotalHours));
    }

    [Fact]
    public void ScheduleNeedsATime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimesSchedule([], Berlin));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
