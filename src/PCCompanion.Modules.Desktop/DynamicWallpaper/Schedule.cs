using System;
using System.Collections.Generic;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>When the next period starts, and which one it is.</summary>
public readonly record struct ScheduleChange<TPeriod>(DateTimeOffset At, TPeriod Period);

/// <summary>A period from when it starts until the next one starts.</summary>
public readonly record struct PeriodSpan<TPeriod>(TPeriod Period, DateTimeOffset Start, DateTimeOffset End);

/// <summary>What the schedules have in common.</summary>
public static class Schedule
{
    /// <summary>
    /// How far ahead the next change is looked for; with no change in that time, as in a polar
    /// night, it's looked for again then.
    /// </summary>
    public static readonly TimeSpan LookAhead = TimeSpan.FromDays(2);

    /// <summary>When a day starts and ends on the clock of a time zone, as <see cref="Occurrence"/> says.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) LocalDay(DateOnly date, TimeZoneInfo zone) =>
        (Occurrence(date, TimeOnly.MinValue, zone), Occurrence(date.AddDays(1), TimeOnly.MinValue, zone));

    /// <summary>
    /// The moment a time on the clock comes on a day, in a time zone. A time the clock skips, as
    /// when it goes forward for daylight saving time, comes when the clock jumps; a time the clock
    /// shows twice, as when it goes back, comes the first time.
    /// </summary>
    public static DateTimeOffset Occurrence(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = date.ToDateTime(time);
        if (zone.IsInvalidTime(local))
        {
            // The first time after the gap: the clock shows it right after the jump.
            var end = local.AddHours(4);
            do
            {
                local = local.AddMinutes(1);
            }
            while (zone.IsInvalidTime(local) && local < end);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var first = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
            return new DateTimeOffset(local, first);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>The periods from one moment to another, each from its start, or the first moment, to its end.</summary>
    /// <param name="start">The first moment.</param>
    /// <param name="end">The last moment, not included.</param>
    /// <param name="periodAt">The period at a moment.</param>
    /// <param name="nextChange">The next change after a moment, if any comes within <see cref="LookAhead"/>.</param>
    public static IReadOnlyList<PeriodSpan<TPeriod>> Spans<TPeriod>(
        DateTimeOffset start,
        DateTimeOffset end,
        Func<DateTimeOffset, TPeriod> periodAt,
        Func<DateTimeOffset, ScheduleChange<TPeriod>?> nextChange)
    {
        ArgumentNullException.ThrowIfNull(periodAt);
        ArgumentNullException.ThrowIfNull(nextChange);

        var spans = new List<PeriodSpan<TPeriod>>();
        var from = start;
        var period = periodAt(start);
        while (from < end)
        {
            var next = nextChange(from);
            if (next is not { } change || change.At >= end)
            {
                spans.Add(new PeriodSpan<TPeriod>(period, from, end));
                break;
            }

            spans.Add(new PeriodSpan<TPeriod>(period, from, change.At));
            (from, period) = (change.At, change.Period);
        }

        return spans;
    }
}
