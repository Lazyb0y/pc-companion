using System;
using System.Collections.Generic;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>The periods of Sunrise and sunset.</summary>
public enum SunPeriod
{
    /// <summary>The sun more than 6° below the horizon.</summary>
    Night,

    /// <summary>The sun rising from 6° below to 6° above the horizon.</summary>
    Sunrise,

    /// <summary>The sun more than 6° above the horizon.</summary>
    Day,

    /// <summary>The sun setting from 6° above to 6° below the horizon.</summary>
    Sunset,
}

/// <summary>
/// Which of Sunrise and sunset's periods it is, by the sun's height, and when the next starts. See
/// "Sunrise and sunset" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
/// <param name="Latitude">The place's degrees north; negative for south.</param>
/// <param name="Longitude">The place's degrees east; negative for west.</param>
/// <param name="HasSunrise">Whether Sunrise has a picture; without one, Night lasts until the sun rises.</param>
/// <param name="HasSunset">Whether Sunset has a picture; without one, Day lasts until the sun sets.</param>
public sealed record SunSchedule(double Latitude, double Longitude, bool HasSunrise, bool HasSunset)
{
    /// <summary>How far below and above the horizon Sunrise and Sunset go, in degrees.</summary>
    public const double Twilight = 6;

    /// <summary>
    /// The sun's height when it rises and sets, in degrees: the top of its disk meets the horizon,
    /// with the bending of its light by the air.
    /// </summary>
    public const double Horizon = -0.833;

    // Short enough that no period worth showing fits between two looks.
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Precision = TimeSpan.FromSeconds(1);

    /// <summary>The period at a moment.</summary>
    public SunPeriod PeriodAt(DateTimeOffset time) => PeriodOf(Sun.At(time, Latitude, Longitude));

    /// <summary>The period when the sun is at a position.</summary>
    public SunPeriod PeriodOf(SunPosition sun)
    {
        if (sun.Elevation > Twilight)
        {
            return SunPeriod.Day;
        }

        if (sun.Elevation < -Twilight)
        {
            return SunPeriod.Night;
        }

        var shown = sun.IsRising ? HasSunrise : HasSunset;
        return shown
            ? sun.IsRising ? SunPeriod.Sunrise : SunPeriod.Sunset
            : sun.Elevation >= Horizon ? SunPeriod.Day : SunPeriod.Night;
    }

    /// <summary>
    /// When the next period after a moment starts, to the second; <see langword="null"/> when none
    /// starts within <see cref="Schedule.LookAhead"/>.
    /// </summary>
    public ScheduleChange<SunPeriod>? NextChange(DateTimeOffset after)
    {
        var current = PeriodAt(after);
        var end = after + Schedule.LookAhead;
        for (var (before, at) = (after, after + Step); before < end; (before, at) = (at, at + Step))
        {
            if (PeriodAt(at) == current)
            {
                continue;
            }

            // The change is between the two looks.
            var (low, high) = (before, at);
            while (high - low > Precision)
            {
                var middle = low + ((high - low) / 2);
                if (PeriodAt(middle) == current)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return new ScheduleChange<SunPeriod>(high, PeriodAt(high));
        }

        return null;
    }

    /// <summary>The periods of a day on the clock of a time zone, for Today on the feature's page.</summary>
    public IReadOnlyList<PeriodSpan<SunPeriod>> Day(DateOnly date, TimeZoneInfo zone)
    {
        var (start, end) = Schedule.LocalDay(date, zone);
        return Schedule.Spans(start, end, PeriodAt, NextChange);
    }

    /// <summary>Whether the sun rises, sets, both or neither during a day on the clock of a time zone.</summary>
    public (bool Rises, bool Sets) RisesAndSets(DateOnly date, TimeZoneInfo zone)
    {
        var (start, end) = Schedule.LocalDay(date, zone);
        var (rises, sets) = (false, false);
        var above = Sun.At(start, Latitude, Longitude).Elevation >= Horizon;
        for (var at = start + Step; at <= end; at += Step)
        {
            var now = Sun.At(at, Latitude, Longitude).Elevation >= Horizon;
            rises |= now && !above;
            sets |= above && !now;
            above = now;
        }

        return (rises, sets);
    }
}
