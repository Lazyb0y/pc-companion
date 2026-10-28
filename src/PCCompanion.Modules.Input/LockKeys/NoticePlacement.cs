using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>
/// Where the notice and the badge show. See "Where the notice shows" in docs/features/input/lock-keys.md.
/// </summary>
public static class NoticePlacement
{
    /// <summary>The gap between the notice and the edges of the work area, in effective pixels.</summary>
    public const int MarginEffectivePixels = 24;

    /// <summary>
    /// The display to show on: the one the window in front is on, where the user types; with no
    /// window in front, the one with the pointer; otherwise the main display, whose top-left
    /// corner is the desktop's origin. <see langword="null"/> when there are no displays.
    /// </summary>
    /// <param name="frontWindowDisplay">The bounds of the window in front's display, if any.</param>
    /// <param name="pointerPosition">The pointer position, if it can be read.</param>
    public static DisplayDetails? ChooseDisplay(IReadOnlyList<DisplayDetails> displays, Rectangle? frontWindowDisplay, Point? pointerPosition)
    {
        ArgumentNullException.ThrowIfNull(displays);

        return (frontWindowDisplay is { } front ? displays.FirstOrDefault(display => display.Bounds == front) : null)
            ?? (pointerPosition is { } position ? displays.FirstOrDefault(display => display.Bounds.Contains(position)) : null)
            ?? displays.FirstOrDefault(display => display.Bounds.Location == Point.Empty)
            ?? (displays.Count > 0 ? displays[0] : null);
    }

    /// <summary>
    /// The bounds of something of <paramref name="size"/> at <paramref name="position"/> in the
    /// display's work area, <see cref="MarginEffectivePixels"/> from its edges, and
    /// <paramref name="away"/> pixels further from the edge it's at, so that a notice can show next
    /// to the badge without covering it.
    /// </summary>
    public static Rectangle Place(DisplayDetails display, Size size, NoticePosition position, int away = 0)
    {
        ArgumentNullException.ThrowIfNull(display);

        var work = display.WorkArea;
        var margin = (int)Math.Round(MarginEffectivePixels * display.Scale);
        var center = work.Left + ((work.Width - size.Width) / 2);
        var location = position switch
        {
            NoticePosition.TopCenter => new Point(center, work.Top + margin + away),
            NoticePosition.TopLeft => new Point(work.Left + margin, work.Top + margin + away),
            _ => new Point(center, work.Bottom - margin - away - size.Height),
        };

        return new Rectangle(location, size);
    }
}
