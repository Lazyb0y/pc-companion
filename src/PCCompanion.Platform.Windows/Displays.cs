using System.Collections.Generic;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The displays of the desktop.
/// </summary>
public static class Displays
{
    /// <summary>
    /// Returns the area of each display, in physical pixels when the process is per-monitor DPI aware.
    /// </summary>
    public static unsafe IReadOnlyList<Rectangle> GetAreas()
    {
        var areas = new List<Rectangle>();

        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (_, _, area, _) =>
        {
            areas.Add(Rectangle.FromLTRB(area->left, area->top, area->right, area->bottom));
            return true;
        }, 0);

        return areas;
    }

    /// <summary>
    /// Whether a message, which every top-level window receives, says that a display was added,
    /// removed or changed.
    /// </summary>
    public static bool IsChangeMessage(uint message) => message == PInvoke.WM_DISPLAYCHANGE;
}
