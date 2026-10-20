using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The desktop's top-level windows, and bringing another program's window to the front.
/// </summary>
public static class TopLevelWindows
{
    private const int MaximumWindowsInFront = 500;

    /// <summary>
    /// The visible top-level windows, front to back. Windows on other virtual desktops, and the
    /// frames of suspended Store apps, are visible but cloaked.
    /// </summary>
    public static IReadOnlyList<TopLevelWindow> List()
    {
        var desktop = PInvoke.GetShellWindow();
        var windows = new List<TopLevelWindow>();

        PInvoke.EnumWindows((window, _) =>
        {
            if (PInvoke.IsWindowVisible(window))
            {
                windows.Add(Read(window, desktop));
            }

            return true;
        }, 0);

        return windows;
    }

    /// <summary>
    /// The window at the end of <paramref name="window"/>'s chain of owners, such as a dialog's main
    /// window, or the window itself when nothing owns it.
    /// </summary>
    public static nint RootOwner(nint window) => PInvoke.GetAncestor(new HWND(window), GET_ANCESTOR_FLAGS.GA_ROOTOWNER);

    /// <summary>
    /// Brings another program's <paramref name="window"/> to the front, with the dialog it has
    /// open, if any, restoring it first when it's minimized. Windows allows it only in some cases,
    /// for example right after the user pressed one of this process's <see cref="HotKey"/>s.
    /// Returns false when Windows refused.
    /// </summary>
    public static bool Activate(nint window)
    {
        var handle = new HWND(window);
        if (PInvoke.IsIconic(handle))
        {
            PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        return PInvoke.SetForegroundWindow(PInvoke.GetLastActivePopup(handle));
    }

    /// <summary>
    /// Puts <paramref name="window"/> behind all other windows, as Alt + Esc does, without moving,
    /// resizing or activating it. An always-on-top window loses always-on-top. Returns false when
    /// Windows refused.
    /// </summary>
    /// <remarks>
    /// Putting another program's window right behind a given window instead, with
    /// <c>SetWindowPos</c>, reports success but leaves File Explorer windows where they were.
    /// </remarks>
    public static bool SendToBack(nint window) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            HWND.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// Whether a window is always on top. Windows takes that from the taskbar while a full-screen
    /// app is in front.
    /// </summary>
    public static bool IsTopmost(nint window) => (ExtendedStyle(new HWND(window)) & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0;

    /// <summary>
    /// Whether <paramref name="other"/> is in front of <paramref name="window"/>. It looks at up to
    /// 500 windows in front of <paramref name="window"/>, so a desktop with many windows stays cheap.
    /// </summary>
    public static bool IsInFrontOf(nint other, nint window)
    {
        var current = new HWND(window);
        for (var step = 0; step < MaximumWindowsInFront; step++)
        {
            current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_HWNDPREV);
            if (current.IsNull)
            {
                return false;
            }

            if (current == other)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a window is one that Windows may give a taskbar button: a top-level window that
    /// isn't a tool window or owned by another window, unless it asks for a button.
    /// </summary>
    public static bool MayHaveTaskbarButton(nint window)
    {
        var handle = new HWND(window);
        if (PInvoke.GetAncestor(handle, GET_ANCESTOR_FLAGS.GA_ROOT) != handle)
        {
            return false;
        }

        var style = ExtendedStyle(handle);
        return (style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0
            || ((style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) == 0 && PInvoke.GetWindow(handle, GET_WINDOW_CMD.GW_OWNER).IsNull);
    }

    private static WINDOW_EX_STYLE ExtendedStyle(HWND window) =>
        (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    private static unsafe TopLevelWindow Read(HWND window, HWND desktop)
    {
        // A window that closed meanwhile keeps process ID 0.
        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(window, &processId);
        var style = ExtendedStyle(window);
        uint cloaked;
        var isCloaked = PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded && cloaked != 0;

        return new TopLevelWindow(
            window,
            processId,
            IsMinimized: PInvoke.IsIconic(window),
            IsCloaked: isCloaked,
            IsToolWindow: (style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0,
            IsNoActivate: (style & WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0,
            IsTopmost: (style & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0,
            HasOwner: !PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER).IsNull,
            IsDesktop: window == desktop);
    }
}

/// <summary>A visible top-level window, as it was when <see cref="TopLevelWindows.List"/> read it.</summary>
/// <param name="IsCloaked">Kept out of sight by Windows, as on another virtual desktop.</param>
/// <param name="IsAppWindow">Shown on the taskbar even when it's owned or a tool window.</param>
/// <param name="IsNoActivate">Not activated when clicked, like an on-screen keyboard.</param>
/// <param name="IsTopmost">Always on top.</param>
/// <param name="IsDesktop">The desktop window of the Windows shell.</param>
public sealed record TopLevelWindow(
    nint Handle,
    uint ProcessId,
    bool IsMinimized,
    bool IsCloaked,
    bool IsToolWindow,
    bool IsAppWindow,
    bool IsNoActivate,
    bool IsTopmost,
    bool HasOwner,
    bool IsDesktop);
