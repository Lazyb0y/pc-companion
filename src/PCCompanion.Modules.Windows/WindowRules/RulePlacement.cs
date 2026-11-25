using System;
using System.Drawing;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Works out where a rule puts a window's visible frame, as "Display" and "Place and size" in
/// docs/features/windows/window-rules.md say. Places are in physical pixels, as the visible frame
/// (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>) gives them.
/// </summary>
public static class RulePlacement
{
    /// <summary>
    /// The visible frame for a window now at <paramref name="frame"/> in <paramref name="from"/>,
    /// the work area of its display, that a rule puts on the display with the work area
    /// <paramref name="to"/>, at <paramref name="place"/>; <see langword="null"/> when the rule
    /// changes neither its display nor its place. A <see cref="PlaceChoice.Remembered"/> place
    /// without a remembered one is no place, as are unknown places.
    /// </summary>
    /// <param name="remembered">The remembered frame, already on <paramref name="to"/>, for <see cref="PlaceChoice.Remembered"/>.</param>
    public static Rectangle? Frame(Rectangle frame, Rectangle from, Rectangle to, RulePlace? place, bool movesDisplay, Rectangle? remembered = null)
    {
        Rectangle? placed = place?.KnownWhich switch
        {
            PlaceChoice.Centered => Center(frame.Size, to),
            PlaceChoice.LeftHalf => Part(to, 0, 0, 0.5, 1),
            PlaceChoice.RightHalf => Part(to, 0.5, 0, 0.5, 1),
            PlaceChoice.TopHalf => Part(to, 0, 0, 1, 0.5),
            PlaceChoice.BottomHalf => Part(to, 0, 0.5, 1, 0.5),
            PlaceChoice.TopLeft => Part(to, 0, 0, 0.5, 0.5),
            PlaceChoice.TopRight => Part(to, 0.5, 0, 0.5, 0.5),
            PlaceChoice.BottomLeft => Part(to, 0, 0.5, 0.5, 0.5),
            PlaceChoice.BottomRight => Part(to, 0.5, 0.5, 0.5, 0.5),
            PlaceChoice.WorkArea => to,
            PlaceChoice.Custom => Part(to, place!.Left / 100, place.Top / 100, place.Width / 100, place.Height / 100),
            PlaceChoice.Remembered => remembered,
            _ => null,
        };

        if (placed is { } chosen)
        {
            return Within(chosen, to);
        }

        return movesDisplay ? Within(Moved(frame, from, to), to) : null;
    }

    /// <summary>
    /// Keeps <paramref name="frame"/> inside <paramref name="workArea"/>: no larger, and moved in
    /// when it sticks out, so no rule puts a window off screen or under the taskbar.
    /// </summary>
    public static Rectangle Within(Rectangle frame, Rectangle workArea)
    {
        var width = Math.Min(Math.Max(frame.Width, 1), workArea.Width);
        var height = Math.Min(Math.Max(frame.Height, 1), workArea.Height);
        var x = Math.Clamp(frame.X, workArea.Left, workArea.Right - width);
        var y = Math.Clamp(frame.Y, workArea.Top, workArea.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// The window rectangle for a visible frame, with the invisible resize borders the window has
    /// now added: its window rectangle <paramref name="bounds"/> around its frame <paramref name="visible"/>.
    /// </summary>
    public static Rectangle WithBorders(Rectangle frame, Rectangle bounds, Rectangle visible) =>
        Rectangle.FromLTRB(
            frame.Left - (visible.Left - bounds.Left),
            frame.Top - (visible.Top - bounds.Top),
            frame.Right + (bounds.Right - visible.Right),
            frame.Bottom + (bounds.Bottom - visible.Bottom));

    /// <summary>
    /// The window rectangle for a visible frame when the borders can't be read, as for a maximized
    /// or minimized window: Windows 11's invisible borders, 7 pixels at 100% scale on the left, right
    /// and bottom, scaled for <paramref name="dpi"/>.
    /// </summary>
    public static Rectangle WithUsualBorders(Rectangle frame, uint dpi)
    {
        var border = (int)Math.Round(7 * dpi / 96.0);
        return Rectangle.FromLTRB(frame.Left - border, frame.Top, frame.Right + border, frame.Bottom + border);
    }

    /// <summary>The largest difference between two frames' edges, in pixels.</summary>
    public static int Distance(Rectangle a, Rectangle b) =>
        Math.Max(Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)), Math.Max(Math.Abs(a.Right - b.Right), Math.Abs(a.Bottom - b.Bottom)));

    private static Rectangle Center(Size size, Rectangle workArea)
    {
        var width = Math.Min(size.Width, workArea.Width);
        var height = Math.Min(size.Height, workArea.Height);
        return new Rectangle(workArea.Left + ((workArea.Width - width) / 2), workArea.Top + ((workArea.Height - height) / 2), width, height);
    }

    // A part of the work area, by fractions of its width and height, with edges rounded so halves
    // and quarters meet without a gap.
    private static Rectangle Part(Rectangle workArea, double left, double top, double width, double height)
    {
        var x1 = workArea.Left + (int)Math.Round(workArea.Width * left);
        var y1 = workArea.Top + (int)Math.Round(workArea.Height * top);
        var x2 = workArea.Left + (int)Math.Round(workArea.Width * Math.Min(1, left + width));
        var y2 = workArea.Top + (int)Math.Round(workArea.Height * Math.Min(1, top + height));
        return Rectangle.FromLTRB(x1, y1, Math.Max(x2, x1 + 1), Math.Max(y2, y1 + 1));
    }

    // The same size, at the same place relative to the work area.
    private static Rectangle Moved(Rectangle frame, Rectangle from, Rectangle to)
    {
        if (from.Width <= 0 || from.Height <= 0)
        {
            return Center(frame.Size, to);
        }

        var x = to.Left + (int)Math.Round((frame.Left - from.Left) * (double)to.Width / from.Width);
        var y = to.Top + (int)Math.Round((frame.Top - from.Top) * (double)to.Height / from.Height);
        return new Rectangle(x, y, frame.Width, frame.Height);
    }
}
