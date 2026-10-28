using System.Collections.Generic;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>A display, in physical pixels when the process is per-monitor DPI aware.</summary>
/// <param name="Bounds">The whole display.</param>
/// <param name="WorkArea">The display without the taskbar and other docked bars.</param>
/// <param name="Dpi">The display's DPI: 96 at 100% scale.</param>
public sealed record DisplayDetails(Rectangle Bounds, Rectangle WorkArea, uint Dpi)
{
    /// <summary>How many physical pixels an effective pixel is on this display: 1 at 100% scale.</summary>
    public double Scale => Dpi / 96.0;
}

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
    /// Returns each display's area, work area and DPI, in physical pixels when the process is
    /// per-monitor DPI aware. A display whose details can't be read is left out.
    /// </summary>
    public static unsafe IReadOnlyList<DisplayDetails> GetDetails()
    {
        var displays = new List<DisplayDetails>();

        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (PInvoke.GetMonitorInfo(monitor, &info))
            {
                var dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var x, out _).Succeeded ? x : 96;
                displays.Add(new DisplayDetails(ToRectangle(info.rcMonitor), ToRectangle(info.rcWork), dpi));
            }

            return true;
        }, 0);

        return displays;
    }

    /// <summary>
    /// The area of the display that <paramref name="window"/> is on, mostly on or nearest to, in
    /// physical pixels when the process is per-monitor DPI aware; <see langword="null"/> when
    /// there's no window.
    /// </summary>
    public static unsafe Rectangle? AreaOf(nint window)
    {
        if (window == 0)
        {
            return null;
        }

        var monitor = PInvoke.MonitorFromWindow(new HWND(window), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, &info))
        {
            return null;
        }

        return ToRectangle(info.rcMonitor);
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

    private static Rectangle ToRectangle(RECT area) => Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom);
}
