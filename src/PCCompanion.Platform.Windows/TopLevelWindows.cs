using System;
using System.Collections.Generic;
using System.Drawing;
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
    /// Reads the visible top-level windows front to back, with their place and the styles that tell
    /// an app's window from overlays, until <paramref name="decide"/> returns a value, and returns
    /// that value; <see langword="null"/> when no window decided. It reads no titles or class names
    /// and sends the windows no messages, so a window that stopped responding can't hold it.
    /// </summary>
    public static bool? FindFrontToBack(Func<WindowPlace, bool?> decide)
    {
        ArgumentNullException.ThrowIfNull(decide);

        var shell = PInvoke.GetShellWindow();
        var shellProcessId = shell.IsNull ? 0 : ProcessIdOf(shell);
        bool? decision = null;

        PInvoke.EnumWindows((window, _) =>
        {
            if (PInvoke.IsWindowVisible(window))
            {
                decision = decide(ReadPlace(window, shellProcessId));
            }

            return decision is null;
        }, 0);

        return decision;
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

    private static TopLevelWindow Read(HWND window, HWND desktop)
    {
        var style = ExtendedStyle(window);

        return new TopLevelWindow(
            window,
            ProcessIdOf(window),
            IsMinimized: PInvoke.IsIconic(window),
            IsCloaked: IsCloaked(window),
            IsToolWindow: (style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0,
            IsNoActivate: (style & WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0,
            IsTopmost: (style & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0,
            HasOwner: !PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER).IsNull,
            IsDesktop: window == desktop);
    }

    private static WindowPlace ReadPlace(HWND window, uint shellProcessId)
    {
        var style = (WINDOW_STYLE)(uint)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var extendedStyle = ExtendedStyle(window);
        var bounds = PInvoke.GetWindowRect(window, out var area) ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom) : Rectangle.Empty;

        return new WindowPlace(
            window,
            bounds,
            IsCloaked: IsCloaked(window),
            IsMinimized: PInvoke.IsIconic(window),
            IsClickThrough: (extendedStyle & WINDOW_EX_STYLE.WS_EX_TRANSPARENT) != 0,
            IsNoActivate: (extendedStyle & WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0,
            IsToolWindow: (extendedStyle & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            IsTopmost: (extendedStyle & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0,
            HasFrame: (style & WINDOW_STYLE.WS_CAPTION) == WINDOW_STYLE.WS_CAPTION || (style & WINDOW_STYLE.WS_THICKFRAME) != 0,
            IsShell: shellProcessId != 0 && ProcessIdOf(window) == shellProcessId);
    }

    // A window that closed meanwhile keeps process ID 0.
    private static unsafe uint ProcessIdOf(HWND window)
    {
        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(window, &processId);
        return processId;
    }

    private static unsafe bool IsCloaked(HWND window)
    {
        uint cloaked;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded && cloaked != 0;
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

/// <summary>
/// A visible top-level window's place and the styles that tell an app's window from overlays, as
/// <see cref="TopLevelWindows.FindFrontToBack"/> read it.
/// </summary>
/// <param name="Bounds">The window's rectangle, in physical pixels when the process is per-monitor DPI aware.</param>
/// <param name="IsCloaked">Kept out of sight by Windows, as on another virtual desktop.</param>
/// <param name="IsClickThrough">Lets clicks through to the windows behind it (<c>WS_EX_TRANSPARENT</c>).</param>
/// <param name="IsNoActivate">Not activated when clicked, like an on-screen keyboard.</param>
/// <param name="IsTopmost">Always on top.</param>
/// <param name="HasFrame">Has a title bar or a frame for resizing (<c>WS_CAPTION</c>, <c>WS_THICKFRAME</c>).</param>
/// <param name="IsShell">Belongs to the Windows shell's process, which shows the desktop, the taskbar and Task View.</param>
public sealed record WindowPlace(
    nint Handle,
    Rectangle Bounds,
    bool IsCloaked,
    bool IsMinimized,
    bool IsClickThrough,
    bool IsNoActivate,
    bool IsToolWindow,
    bool IsTopmost,
    bool HasFrame,
    bool IsShell);
