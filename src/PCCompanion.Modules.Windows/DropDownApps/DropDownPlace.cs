using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// The invisible borders Windows draws around a window for resizing, outside the part that
/// shows: 7 pixels left, right and below at 100% scale for most apps.
/// </summary>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The insets of a window whose rectangle is <paramref name="bounds"/> and whose visible part is <paramref name="visible"/>.</summary>
    public static FrameInsets Between(Rectangle bounds, Rectangle visible) =>
        new(visible.Left - bounds.Left, visible.Top - bounds.Top, bounds.Right - visible.Right, bounds.Bottom - visible.Bottom);

    /// <summary>The visible part of a window whose rectangle is <paramref name="bounds"/>.</summary>
    public Rectangle Inside(Rectangle bounds) =>
        Rectangle.FromLTRB(bounds.Left + Left, bounds.Top + Top, bounds.Right - Right, bounds.Bottom - Bottom);
}

/// <summary>
/// Where a drop-down window goes, as "Where" in docs/features/windows/drop-down-apps.md says: its
/// visible part at the top of the work area, centered across it, its invisible borders outside.
/// </summary>
public static class DropDownPlace
{
    /// <summary>
    /// The size of the visible part for a width and height in percent of <paramref name="workArea"/>,
    /// or in shares of it, as for a size the user dragged.
    /// </summary>
    public static Size VisibleSize(Rectangle workArea, double widthShare, double heightShare) =>
        new((int)Math.Round(workArea.Width * widthShare), (int)Math.Round(workArea.Height * heightShare));

    /// <summary>The share of <paramref name="workArea"/> that a visible part of <paramref name="size"/> takes, across and down.</summary>
    public static (double Width, double Height) ShareOf(Rectangle workArea, Size size) =>
        (workArea.Width > 0 ? (double)size.Width / workArea.Width : 1, workArea.Height > 0 ? (double)size.Height / workArea.Height : 1);

    /// <summary>
    /// The window's rectangle, borders included, that puts a visible part of <paramref name="visibleSize"/>
    /// at the top of <paramref name="workArea"/>, centered across it.
    /// </summary>
    public static Rectangle Shown(Rectangle workArea, Size visibleSize, FrameInsets insets)
    {
        var left = workArea.Left + ((workArea.Width - visibleSize.Width) / 2);
        return new Rectangle(
            left - insets.Left,
            workArea.Top - insets.Top,
            visibleSize.Width + insets.Left + insets.Right,
            visibleSize.Height + insets.Top + insets.Bottom);
    }

    /// <summary>
    /// Where a window shown at <paramref name="shown"/> starts sliding in and ends sliding out: right
    /// above <paramref name="workArea"/>, the bottom of its visible part at the work area's top.
    /// </summary>
    public static Point Above(Rectangle shown, Rectangle workArea, FrameInsets insets) =>
        new(shown.Left, workArea.Top - shown.Height + insets.Bottom);

    /// <summary>
    /// Whether another display touches the top edge of <paramref name="display"/> across the
    /// window's width, so that a slide from above would cross it.
    /// </summary>
    /// <param name="display">The whole display the window shows on.</param>
    /// <param name="shown">The window's rectangle once shown.</param>
    /// <param name="displays">Every display.</param>
    public static bool HasDisplayAbove(Rectangle display, Rectangle shown, IEnumerable<Rectangle> displays) =>
        displays.Any(other => other != display && other.Bottom == display.Top && other.Left < shown.Right && other.Right > shown.Left);
}
