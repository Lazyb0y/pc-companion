using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// A period by its schedule: one of Sunrise and sunset's (<see cref="SunPeriod"/>), a time's place
/// in Times of day, or Light (0) and Dark (1) of Windows mode.
/// </summary>
public readonly record struct PeriodName(ChangeWith Kind, int Index)
{
    public static PeriodName Of(SunPeriod period) => new(ChangeWith.Sun, (int)period);

    public static PeriodName Light => new(ChangeWith.WindowsMode, 0);

    public static PeriodName Dark => new(ChangeWith.WindowsMode, 1);

    /// <summary>Its name in logs and in <c>dynamic-wallpaper.json</c>, such as <c>Sun.Day</c>, <c>Times.2</c> or <c>WindowsMode.Dark</c>.</summary>
    public override string ToString() => Kind switch
    {
        ChangeWith.Sun => $"Sun.{(SunPeriod)Index}",
        ChangeWith.WindowsMode => Index == 0 ? "WindowsMode.Light" : "WindowsMode.Dark",
        _ => $"Times.{Index}",
    };
}

/// <summary>What shows now, and when that changes.</summary>
/// <param name="Period">The period now.</param>
/// <param name="Pictures">Its pictures; <see langword="null"/> when there's nothing to show, as with no usable time.</param>
/// <param name="NextLook">When to look again: when the next period starts, or later when none starts soon; <see langword="null"/> to look only when something changes, as for Windows mode.</param>
public sealed record WallpaperPlan(PeriodName Period, WallpaperPeriod? Pictures, DateTimeOffset? NextLook);

/// <summary>
/// Works out the period now and when the next starts, from the settings, the clock and the place.
/// It reads nothing from Windows. See "Change with" and "When pictures change" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class WallpaperPlanner
{
    /// <summary>When Day starts in Sunrise and sunset without a place.</summary>
    public static readonly TimeOnly FixedSunrise = new(6, 0);

    /// <summary>When Night starts in Sunrise and sunset without a place.</summary>
    public static readonly TimeOnly FixedSunset = new(18, 0);

    /// <summary>The place Sunrise and sunset uses: the coordinates, or the time zone's main city; <see langword="null"/> for none.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="zoneCity">The main city of Windows' time zone, for From your time zone.</param>
    public static Coordinates? PlaceOf(DynamicWallpaperSettings settings, Func<Coordinates?> zoneCity)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(zoneCity);

        return settings.Location switch
        {
            LocationSource.Coordinates => DynamicWallpaperCheck.AreCoordinates(settings.Latitude, settings.Longitude)
                ? DynamicWallpaperCheck.Round(new Coordinates(settings.Latitude!.Value, settings.Longitude!.Value))
                : null,
            _ => zoneCity(),
        };
    }

    /// <summary>The period at a moment, its pictures, and when to look again.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="now">The moment.</param>
    /// <param name="zone">The time zone of the PC's clock.</param>
    /// <param name="place">Sunrise and sunset's place; <see langword="null"/> for none, which changes at 6:00 and 18:00.</param>
    /// <param name="windowsModeDark">Whether the Windows mode is dark, for Windows mode.</param>
    public static WallpaperPlan Plan(DynamicWallpaperSettings settings, DateTimeOffset now, TimeZoneInfo zone, Coordinates? place, bool windowsModeDark)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(zone);

        return settings.ChangeWith switch
        {
            ChangeWith.Times => PlanTimes(settings, now, zone),
            ChangeWith.WindowsMode => windowsModeDark
                ? new WallpaperPlan(PeriodName.Dark, settings.WindowsMode.Dark, null)
                : new WallpaperPlan(PeriodName.Light, settings.WindowsMode.Light, null),
            _ => PlanSun(settings, now, zone, place),
        };
    }

    /// <summary>Whether a period's own picture or folder can be used, as Sunrise and Sunset need to count.</summary>
    public static bool IsUsable(WallpaperPeriod? period) =>
        period is not null && DynamicWallpaperCheck.Check(period.Own) == SourceProblem.None;

    private static WallpaperPlan PlanSun(DynamicWallpaperSettings settings, DateTimeOffset now, TimeZoneInfo zone, Coordinates? place)
    {
        if (place is not { } here)
        {
            var fixedTimes = new TimesSchedule([FixedSunrise, FixedSunset], zone);
            var day = fixedTimes.PeriodAt(now) == 0;
            var period = day ? SunPeriod.Day : SunPeriod.Night;
            return new WallpaperPlan(PeriodName.Of(period), settings.Sun.Of(period), fixedTimes.NextChange(now)?.At ?? now + Schedule.LookAhead);
        }

        var schedule = new SunSchedule(here.Latitude, here.Longitude, IsUsable(settings.Sun.Sunrise), IsUsable(settings.Sun.Sunset));
        var current = schedule.PeriodAt(now);
        return new WallpaperPlan(PeriodName.Of(current), settings.Sun.Of(current), schedule.NextChange(now)?.At ?? now + Schedule.LookAhead);
    }

    /// <summary>
    /// The periods of a day on the clock, for Today on the feature's page, as <see cref="Plan"/>
    /// works them out; none for Windows mode or without a usable time.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="date">The day.</param>
    /// <param name="zone">The time zone of the PC's clock.</param>
    /// <param name="place">Sunrise and sunset's place; <see langword="null"/> for none, which changes at 6:00 and 18:00.</param>
    public static IReadOnlyList<PeriodSpan<PeriodName>> Day(DynamicWallpaperSettings settings, DateOnly date, TimeZoneInfo zone, Coordinates? place)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(zone);

        switch (settings.ChangeWith)
        {
            case ChangeWith.WindowsMode:
                return [];
            case ChangeWith.Times:
                var usable = UsableTimes(settings);
                return usable.Count == 0
                    ? []
                    : [.. new TimesSchedule([.. usable.Select(time => time.Time)], zone).Day(date)
                        .Select(span => new PeriodSpan<PeriodName>(new PeriodName(ChangeWith.Times, usable[span.Period].Index), span.Start, span.End))];
            default:
                if (place is not { } here)
                {
                    return [.. new TimesSchedule([FixedSunrise, FixedSunset], zone).Day(date)
                        .Select(span => new PeriodSpan<PeriodName>(PeriodName.Of(span.Period == 0 ? SunPeriod.Day : SunPeriod.Night), span.Start, span.End))];
                }

                return [.. new SunSchedule(here.Latitude, here.Longitude, IsUsable(settings.Sun.Sunrise), IsUsable(settings.Sun.Sunset)).Day(date, zone)
                    .Select(span => new PeriodSpan<PeriodName>(PeriodName.Of(span.Period), span.Start, span.End))];
        }
    }

    /// <summary>Whether the sun rises and sets during a day on the clock, for Today's note near the poles.</summary>
    public static SunDay SunDayOf(Coordinates place, DateOnly date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var (rises, sets) = new SunSchedule(place.Latitude, place.Longitude, true, true).RisesAndSets(date, zone);
        if (rises == sets)
        {
            if (rises)
            {
                return SunDay.RisesAndSets;
            }

            // Neither: up all day, or down all day.
            var (start, _) = Schedule.LocalDay(date, zone);
            return Sun.At(start, place.Latitude, place.Longitude).Elevation >= SunSchedule.Horizon ? SunDay.DoesntSet : SunDay.DoesntRise;
        }

        return rises ? SunDay.DoesntSet : SunDay.DoesntRise;
    }

    private static WallpaperPlan PlanTimes(DynamicWallpaperSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var usable = UsableTimes(settings);
        if (usable.Count == 0)
        {
            return new WallpaperPlan(new PeriodName(ChangeWith.Times, -1), null, null);
        }

        var schedule = new TimesSchedule([.. usable.Select(time => time.Time)], zone);
        var index = usable[schedule.PeriodAt(now)].Index;
        return new WallpaperPlan(new PeriodName(ChangeWith.Times, index), settings.Times[index], schedule.NextChange(now)?.At ?? now + Schedule.LookAhead);
    }

    // The times that can be used, each with its place in the settings' times.
    private static List<(int Index, TimeOnly Time)> UsableTimes(DynamicWallpaperSettings settings)
    {
        var problems = DynamicWallpaperCheck.CheckTimes(settings.Times);
        var usable = new List<(int Index, TimeOnly Time)>();
        for (var i = 0; i < settings.Times.Count; i++)
        {
            if (problems[i] == TimeProblem.None && DynamicWallpaperCheck.TryParseTime(settings.Times[i].At, out var time))
            {
                usable.Add((i, time));
            }
        }

        return usable;
    }
}

/// <summary>Whether the sun rises and sets during a day.</summary>
public enum SunDay
{
    RisesAndSets,

    /// <summary>The sun stays below the horizon, or sets and doesn't rise again that day.</summary>
    DoesntRise,

    /// <summary>The sun stays above the horizon, or rises and doesn't set again that day.</summary>
    DoesntSet,
}
