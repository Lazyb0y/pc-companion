using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Finds the strip's place on the taskbar: at the end of the free space between the last app
/// button and the Widgets entry, when it's on the notification area's side, or else the
/// notification area. On a mirrored taskbar, as in right-to-left languages, the notification
/// area is on the left and so is the strip. See "How the strip works" in
/// docs/features/desktop/system-meters.md.
/// </summary>
public static class StripPlacement
{
    /// <summary>The class name of the taskbar's app buttons.</summary>
    public const string AppButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    public const string StartButton = "StartButton";
    public const string SearchButton = "SearchButton";
    public const string TaskViewButton = "TaskViewButton";
    public const string WidgetsButton = "WidgetsButton";

    /// <summary>
    /// The strip's bounds, <paramref name="width"/> wide and as high as the taskbar, with
    /// <paramref name="gap"/> pixels to what's next to it, or <see langword="null"/> when there's no
    /// room or the taskbar isn't along the bottom or top of the display.
    /// </summary>
    public static Rectangle? Find(Rectangle taskbar, Rectangle? notificationArea, IReadOnlyList<TaskbarElement> elements, int width, int gap)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        if (taskbar.Width <= taskbar.Height)
        {
            return null;
        }

        var middle = taskbar.Left + (taskbar.Width / 2);
        var tray = notificationArea ?? new Rectangle(taskbar.Right, taskbar.Top, 0, taskbar.Height);
        var trayOnRight = tray.Left + (tray.Width / 2) >= middle;

        // The free space runs from the app side's last element to the tray side's first one.
        int appEdge = trayOnRight ? taskbar.Left : taskbar.Right;
        int trayEdge = trayOnRight ? tray.Left : tray.Right;
        foreach (var element in elements)
        {
            var bounds = element.Bounds;
            if (!taskbar.IntersectsWith(bounds))
            {
                continue;
            }

            var onTraySide = (bounds.Left + (bounds.Width / 2) >= middle) == trayOnRight;
            if (element.AutomationId == WidgetsButton && onTraySide)
            {
                trayEdge = trayOnRight ? Math.Min(trayEdge, bounds.Left) : Math.Max(trayEdge, bounds.Right);
            }
            else if (element.ClassName == AppButtonClass || element.AutomationId is StartButton or SearchButton or TaskViewButton or WidgetsButton)
            {
                appEdge = trayOnRight ? Math.Max(appEdge, bounds.Right) : Math.Min(appEdge, bounds.Left);
            }
        }

        var x = trayOnRight ? trayEdge - gap - width : trayEdge + gap;
        var room = trayOnRight ? x >= appEdge + gap : x + width <= appEdge - gap;
        return room ? new Rectangle(x, taskbar.Top, width, taskbar.Height) : null;
    }
}
