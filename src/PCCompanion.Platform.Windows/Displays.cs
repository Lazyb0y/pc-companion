using System.Collections.Generic;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

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

    /// <summary>
    /// The DPI of the main display, where the taskbar's notification area is: 96 at 100% scale.
    /// It's the display's own DPI when the process is per-monitor DPI aware.
    /// </summary>
    public static uint MainDisplayDpi()
    {
        var monitor = PInvoke.MonitorFromPoint(Point.Empty, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded ? dpi : 96;
    }

    /// <summary>The size of small icons, such as notification icons, at a DPI: 16 pixels at 96.</summary>
    public static int SmallIconSize(uint dpi) => PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
}
