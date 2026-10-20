using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The taskbar on the main display: where it is, whether it hides itself, and whether it's in
/// front, as it is unless a full-screen app is.
/// </summary>
public static class Taskbar
{
    /// <summary>Reads the taskbar, or returns <see langword="null"/> when it isn't running.</summary>
    public static unsafe TaskbarState? Read()
    {
        var taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (taskbar.IsNull || !PInvoke.GetWindowRect(taskbar, out var bounds))
        {
            return null;
        }

        // The notification area is a child window; Windows keeps its position up to date, unlike the task list's.
        var notificationArea = PInvoke.FindWindowEx(taskbar, HWND.Null, "TrayNotifyWnd", null);
        Rectangle? notificationBounds = !notificationArea.IsNull && PInvoke.GetWindowRect(notificationArea, out var area)
            ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom)
            : null;

        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(taskbar, &processId);
        var dpi = PInvoke.GetDpiForWindow(taskbar);

        return new TaskbarState(
            taskbar,
            processId,
            Rectangle.FromLTRB(bounds.left, bounds.top, bounds.right, bounds.bottom),
            notificationBounds,
            dpi != 0 ? dpi : 96,
            TopLevelWindows.IsTopmost(taskbar),
            IsAutoHide());
    }

    /// <summary>Whether the user set the taskbar to hide itself.</summary>
    public static unsafe bool IsAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
        return (PInvoke.SHAppBarMessage(PInvoke.ABM_GETSTATE, ref data) & PInvoke.ABS_AUTOHIDE) != 0;
    }
}

/// <summary>The taskbar as <see cref="Taskbar.Read"/> found it, in physical pixels.</summary>
/// <param name="Handle">The taskbar's window.</param>
/// <param name="ProcessId">The process that runs the taskbar, Explorer.</param>
/// <param name="NotificationArea">Where the notification area is, or <see langword="null"/> when the taskbar has none.</param>
/// <param name="Dpi">The taskbar's DPI: 96 at 100% scale.</param>
/// <param name="IsTopmost">Always on top, as it is unless a full-screen app is in front.</param>
/// <param name="IsAutoHide">Set to hide itself.</param>
public sealed record TaskbarState(
    nint Handle,
    uint ProcessId,
    Rectangle Bounds,
    Rectangle? NotificationArea,
    uint Dpi,
    bool IsTopmost,
    bool IsAutoHide);
