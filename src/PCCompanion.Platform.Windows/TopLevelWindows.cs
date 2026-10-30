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
    /// Returns false when Windows refused. It doesn't wait for the window's program: Windows passes
    /// the restore on to it, so one that stopped responding restores the window when it responds.
    /// </summary>
    public static bool Activate(nint window)
    {
        var handle = new HWND(window);
        if (PInvoke.IsIconic(handle))
        {
            PInvoke.ShowWindowAsync(handle, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        return PInvoke.SetForegroundWindow(PInvoke.GetLastActivePopup(handle));
    }

    /// <summary>
    /// Minimizes another program's <paramref name="window"/>, as its Minimize button does, and
    /// Windows activates the next window. Returns false when Windows refused, for example for an
    /// app running as administrator while this process doesn't. It doesn't wait: Windows passes it
    /// on to the window's program, which carries it out when it handles its messages.
    /// </summary>
    public static bool Minimize(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_MINIMIZE);

    /// <summary>
    /// Hides another program's <paramref name="window"/>, taking it off the taskbar and out of
    /// Alt + Tab. Returns false when Windows refused. It doesn't wait, as <see cref="Minimize"/>
    /// doesn't.
    /// </summary>
    public static bool Hide(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>
    /// Shows another program's <paramref name="window"/> minimized on the taskbar without
    /// activating it: a hidden window comes back minimized, and a shown one minimizes while the
    /// window in front stays in front. Returns false when Windows refused. It doesn't wait.
    /// </summary>
    public static bool ShowMinimized(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE);

    /// <summary>
    /// Restores another program's minimized <paramref name="window"/> as it was before it was
    /// minimized, maximized again if it was, without activating it. Windows leaves its place in
    /// the order of windows and its virtual desktop, which activating it would switch to. Returns
    /// false when Windows refused, for example for an app running as administrator while this
    /// process doesn't. It doesn't wait.
    /// </summary>
    public static bool RestoreWithoutActivating(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

    /// <summary>
    /// Asks another program's <paramref name="window"/> to minimize, as its window menu does, by
    /// posting it <c>WM_SYSCOMMAND</c> with <c>SC_MINIMIZE</c>. Windows passes the request on even
    /// to an app running as administrator while this process doesn't, and the app may ignore it.
    /// Returns false when the request couldn't be posted.
    /// </summary>
    public static bool AskToMinimize(nint window) => PostSystemCommand(window, PInvoke.SC_MINIMIZE);

    /// <summary>
    /// Asks another program's minimized <paramref name="window"/> to come back, as
    /// <see cref="AskToMinimize"/> asks it to go. The window takes the front as it comes back.
    /// </summary>
    public static bool AskToRestore(nint window) => PostSystemCommand(window, PInvoke.SC_RESTORE);

    /// <summary>
    /// Puts another program's <paramref name="window"/> right behind <paramref name="inFront"/>,
    /// without moving, resizing or activating it. Returns false when Windows refused. It doesn't
    /// wait: Windows passes the request on to the window's thread, so check with
    /// <see cref="NextVisibleBehind"/> that it moved; Windows can report success and leave a
    /// window where it was, as <see cref="SendToBack"/> notes.
    /// </summary>
    public static bool PutBehind(nint window, nint inFront) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            new HWND(inFront),
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// The first visible window behind <paramref name="window"/>, skipping hidden ones such as each
    /// app's input method windows, or 0 when there's none among the 500 windows behind it.
    /// </summary>
    public static nint NextVisibleBehind(nint window)
    {
        var current = new HWND(window);
        for (var step = 0; step < MaximumWindowsInFront; step++)
        {
            current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_HWNDNEXT);
            if (current.IsNull || PInvoke.IsWindowVisible(current))
            {
                return current;
            }
        }

        return 0;
    }

    /// <summary>Whether <paramref name="window"/> has a Minimize button (<c>WS_MINIMIZEBOX</c>), as Windows + M requires.</summary>
    public static bool HasMinimizeButton(nint window) =>
        (((WINDOW_STYLE)(uint)PInvoke.GetWindowLongPtr(new HWND(window), WINDOW_LONG_PTR_INDEX.GWL_STYLE)) & WINDOW_STYLE.WS_MINIMIZEBOX) != 0;

    /// <summary>
    /// Whether <paramref name="window"/>'s program stopped responding: it hasn't handled its
    /// messages for 5 seconds (<c>IsHungAppWindow</c>).
    /// </summary>
    public static bool IsNotResponding(nint window) => PInvoke.IsHungAppWindow(new HWND(window));

    /// <summary>
    /// Shows another program's <paramref name="window"/>, hidden or minimized, as it was before it
    /// was minimized, maximized again if it was, and activates it. Returns false when Windows
    /// refused. It doesn't wait; bring the window's dialog to the front with <see cref="Activate"/>
    /// once the window shows.
    /// </summary>
    public static bool Restore(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_RESTORE);

    /// <summary>Whether <paramref name="window"/> is shown: hidden windows aren't, minimized ones are.</summary>
    public static bool IsVisible(nint window) => PInvoke.IsWindowVisible(new HWND(window));

    /// <summary>Whether <paramref name="window"/> is minimized, shown or hidden.</summary>
    public static bool IsMinimized(nint window) => PInvoke.IsIconic(new HWND(window));

    /// <summary>Whether <paramref name="window"/> still exists.</summary>
    public static bool Exists(nint window) => PInvoke.IsWindow(new HWND(window));

    /// <summary>
    /// Whether <paramref name="window"/> takes input. A window with a dialog open is disabled until
    /// the dialog closes.
    /// </summary>
    public static bool IsEnabled(nint window) => PInvoke.IsWindowEnabled(new HWND(window));

    /// <summary>
    /// The title of another program's <paramref name="window"/>, or an empty string. Windows reads
    /// its own copy of another program's title without sending the window a message, so a program
    /// that stopped responding can't hold it. For this process's windows, it sends one.
    /// </summary>
    public static string Title(nint window)
    {
        var handle = new HWND(window);
        var length = PInvoke.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> text = length < 512 ? stackalloc char[length + 1] : new char[length + 1];
        var copied = PInvoke.GetWindowText(handle, text);
        return copied > 0 ? new string(text[..copied]) : string.Empty;
    }

    /// <summary>The name of <paramref name="window"/>'s window class, or an empty string when it has closed.</summary>
    public static string ClassName(nint window)
    {
        // Class names have at most 256 characters.
        Span<char> name = stackalloc char[257];
        var copied = PInvoke.GetClassName(new HWND(window), name);
        return copied > 0 ? new string(name[..copied]) : string.Empty;
    }

    /// <summary>
    /// Puts <paramref name="window"/> behind all other windows, as Alt + Esc does, without moving,
    /// resizing or activating it. An always-on-top window loses always-on-top. Returns false when
    /// Windows refused. For another thread's window, it doesn't wait: Windows passes the request on
    /// to the window's thread, so a program that stopped responding moves it when it responds.
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
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

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

    private static bool PostSystemCommand(nint window, uint command) => PInvoke.PostMessage(new HWND(window), PInvoke.WM_SYSCOMMAND, command, 0);

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

    /// <summary>
    /// The ID of the process that <paramref name="window"/> belongs to, or 0 when the window has
    /// closed.
    /// </summary>
    public static unsafe uint ProcessIdOf(nint window)
    {
        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(new HWND(window), &processId);
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
