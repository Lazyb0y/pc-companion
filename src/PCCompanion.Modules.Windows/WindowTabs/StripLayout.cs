using System;
using System.Collections.Generic;
using System.Drawing;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Where a group's tab strip goes and where each tab is in it, as "Where it is" in
/// docs/features/windows/window-tabs.md says. Sizes are in effective pixels, scaled to the
/// display's DPI.
/// </summary>
public static class StripLayout
{
    /// <summary>The strip's height.</summary>
    public const int Height = 32;

    /// <summary>The widest a tab gets.</summary>
    public const int MaximumTabWidth = 200;

    /// <summary>The narrowest a tab gets, which shows only its icon.</summary>
    public const int MinimumTabWidth = 40;

    /// <summary>How far below the top of a window's visible frame a window dropped with Ctrl groups with it.</summary>
    public const int DropBand = 48;

    /// <summary>
    /// The strip for <paramref name="count"/> tabs of a window whose visible frame is
    /// <paramref name="frame"/>, on a display with <paramref name="workArea"/> and
    /// <paramref name="dpi"/>: above the window, from its left edge or its right edge in
    /// right-to-left languages, or over its top when there's no room above.
    /// </summary>
    public static StripPlace Place(Rectangle frame, Rectangle workArea, int dpi, int count, bool rightToLeft)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var height = Scale(Height, dpi);
        var tabWidth = count == 0 ? Scale(MinimumTabWidth, dpi) : Math.Clamp(frame.Width / count, Scale(MinimumTabWidth, dpi), Scale(MaximumTabWidth, dpi));
        var width = Math.Max(tabWidth * count, tabWidth);
        var atTopEdge = frame.Top - height < workArea.Top;
        var bounds = new Rectangle(rightToLeft ? frame.Right - width : frame.Left, atTopEdge ? frame.Top : frame.Top - height, width, height);

        var tabs = new Rectangle[count];
        for (var index = 0; index < count; index++)
        {
            tabs[index] = new Rectangle(rightToLeft ? width - ((index + 1) * tabWidth) : index * tabWidth, 0, tabWidth, height);
        }

        return new StripPlace(bounds, atTopEdge, tabs, rightToLeft);
    }

    /// <summary>A length in effective pixels at <paramref name="dpi"/>, rounded.</summary>
    public static int Scale(int effective, int dpi) => ((effective * dpi) + 48) / 96;
}

/// <summary>
/// A strip's place on the screen in physical pixels, and its tabs in the strip's own pixels, the
/// first tab first.
/// </summary>
/// <param name="AtTopEdge">There's no room above the window, so the strip is over its top.</param>
public sealed record StripPlace(Rectangle Bounds, bool AtTopEdge, IReadOnlyList<Rectangle> Tabs, bool RightToLeft)
{
    /// <summary>The tab at <paramref name="point"/>, in the strip's own pixels, or -1.</summary>
    public int TabAt(Point point)
    {
        for (var index = 0; index < Tabs.Count; index++)
        {
            if (Tabs[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where a tab dropped at <paramref name="point"/>, in the strip's own pixels, goes among the
    /// tabs: from 0, before the first, to the number of tabs, after the last.
    /// </summary>
    public int InsertionAt(Point point)
    {
        var before = 0;
        foreach (var tab in Tabs)
        {
            var middle = tab.Left + (tab.Width / 2);
            if (RightToLeft ? middle > point.X : middle < point.X)
            {
                before++;
            }
        }

        return before;
    }

    /// <summary>Whether a point on the screen is on the strip.</summary>
    public bool Contains(Point screen) => Bounds.Contains(screen);

    /// <summary>A point on the screen in the strip's own pixels.</summary>
    public Point ToStrip(Point screen) => new(screen.X - Bounds.X, screen.Y - Bounds.Y);
}
