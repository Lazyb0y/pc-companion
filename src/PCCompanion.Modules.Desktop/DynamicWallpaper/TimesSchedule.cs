using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// Which of Times of day's times shows at a moment, and when the next starts, on the clock of a
/// time zone. See "Times of day" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed class TimesSchedule
{
    private readonly IReadOnlyList<TimeOnly> _times;
    private readonly TimeZoneInfo _zone;

    /// <param name="times">The times, each different, in any order; a period is a time's place in this list.</param>
    /// <param name="zone">The time zone of the PC's clock.</param>
    public TimesSchedule(IReadOnlyList<TimeOnly> times, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfZero(times.Count);

        _times = times;
        _zone = zone;
    }

    /// <summary>The time whose picture shows at a moment: the one that came last.</summary>
    public int PeriodAt(DateTimeOffset time) =>
        Occurrences(time).Where(occurrence => occurrence.At <= time).MaxBy(occurrence => occurrence.At).Period;

    /// <summary>When the next time after a moment comes, and which one it is.</summary>
    public ScheduleChange<int>? NextChange(DateTimeOffset after) =>
        Occurrences(after).Where(occurrence => occurrence.At > after).MinBy(occurrence => occurrence.At) is { At: var at, Period: var period } && at - after <= Schedule.LookAhead
            ? new ScheduleChange<int>(at, period)
            : null;

    /// <summary>The periods of a day on the clock, for Today on the feature's page.</summary>
    public IReadOnlyList<PeriodSpan<int>> Day(DateOnly date)
    {
        var (start, end) = Schedule.LocalDay(date, _zone);
        return Schedule.Spans(start, end, PeriodAt, NextChange);
    }

    // Every time on the days around a moment, with room for any time zone's offset.
    private IEnumerable<ScheduleChange<int>> Occurrences(DateTimeOffset around)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(around, _zone).DateTime);
        for (var day = -2; day <= 3; day++)
        {
            for (var i = 0; i < _times.Count; i++)
            {
                yield return new ScheduleChange<int>(Schedule.Occurrence(today.AddDays(day), _times[i], _zone), i);
            }
        }
    }
}
