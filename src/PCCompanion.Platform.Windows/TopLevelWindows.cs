using System;
using System.Collections.Generic;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
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

    /// <summary>
    /// Whether Windows keeps <paramref name="window"/> out of sight while it's shown (cloaked), as
    /// for windows on other virtual desktops and Windows' own helper windows.
    /// </summary>
    public static bool IsCloaked(nint window) => IsCloaked(new HWND(window));

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
    /// What <paramref name="window"/> is like now, as <see cref="List"/> reads each window. It
    /// reads no title and sends the window no message.
    /// </summary>
    public static TopLevelWindow Describe(nint window) => Read(new HWND(window), PInvoke.GetShellWindow());

    /// <summary>
    /// The visible top-level windows that <paramref name="owner"/> owns, directly or through other
    /// owned windows, such as its palettes and dialogs, front to back.
    /// </summary>
    public static IReadOnlyList<nint> VisibleOwnedWindows(nint owner)
    {
        var owned = new List<nint>();
        PInvoke.EnumWindows((window, _) =>
        {
            if (window != new HWND(owner) && PInvoke.IsWindowVisible(window) && IsOwnedBy(window, new HWND(owner)))
            {
                owned.Add(window);
            }

            return true;
        }, 0);

        return owned;
    }

    /// <summary>
    /// <paramref name="window"/>'s rectangle, with the invisible borders Windows draws around it for
    /// resizing, in physical pixels when the process is per-monitor DPI aware;
    /// <see langword="null"/> when the window has closed.
    /// </summary>
    public static Rectangle? Bounds(nint window) =>
        PInvoke.GetWindowRect(new HWND(window), out var area) ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom) : null;

    /// <summary>
    /// The part of <paramref name="window"/> that shows: its rectangle without the invisible
    /// borders (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>), in physical pixels, or its whole rectangle when
    /// Windows doesn't report them; <see langword="null"/> when the window has closed. It sends
    /// the window no message.
    /// </summary>
    public static unsafe Rectangle? VisibleBounds(nint window)
    {
        RECT area;
        return PInvoke.DwmGetWindowAttribute(new HWND(window), DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &area, (uint)sizeof(RECT)).Succeeded
            ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom)
            : Bounds(window);
    }

    /// <summary>
    /// Moves and sizes another program's <paramref name="window"/>, shown or hidden, to
    /// <paramref name="bounds"/>, a rectangle with its invisible borders, without changing its
    /// place in the order of windows or activating it. Returns false when Windows refused. It
    /// doesn't wait: Windows passes the request on to the window's thread, so a program that
    /// stopped responding moves it when it responds.
    /// </summary>
    public static bool Place(nint window, Rectangle bounds) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            HWND.Null,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// Moves another program's <paramref name="window"/> so its top-left corner, invisible borders
    /// included, is at <paramref name="location"/>, keeping its size, as <see cref="Place"/> does.
    /// </summary>
    public static bool MoveTo(nint window, Point location) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            HWND.Null,
            location.X,
            location.Y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// Shows another program's hidden <paramref name="window"/> in its current state, without
    /// activating it (<c>SW_SHOWNA</c>). Returns false when Windows refused. It doesn't wait.
    /// </summary>
    public static bool ShowWithoutActivating(nint window) => PInvoke.ShowWindowAsync(new HWND(window), SHOW_WINDOW_CMD.SW_SHOWNA);

    /// <summary>
    /// Puts another program's <paramref name="window"/> always on top, or takes it off, without
    /// moving or activating it. Returns false when Windows refused. It doesn't wait.
    /// </summary>
    public static bool SetTopmost(nint window, bool topmost) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>The DPI <paramref name="window"/> is drawn at: 96 at 100% scale.</summary>
    public static uint Dpi(nint window) => PInvoke.GetDpiForWindow(new HWND(window));

    /// <summary>Whether <paramref name="window"/> is maximized, shown or hidden.</summary>
    public static bool IsMaximized(nint window) => PInvoke.IsZoomed(new HWND(window));

    /// <summary>
    /// How <paramref name="window"/> shows now and when it's restored, or <see langword="null"/>
    /// when it has closed. It sends the window no message.
    /// </summary>
    public static unsafe WindowPlacement? Placement(nint window)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!PInvoke.GetWindowPlacement(new HWND(window), ref placement))
        {
            return null;
        }

        var state = PInvoke.IsIconic(new HWND(window)) ? WindowState.Minimized
            : PInvoke.IsZoomed(new HWND(window)) ? WindowState.Maximized
            : WindowState.Normal;
        var normal = Rectangle.FromLTRB(placement.rcNormalPosition.left, placement.rcNormalPosition.top, placement.rcNormalPosition.right, placement.rcNormalPosition.bottom);
        return new WindowPlacement(
            ToScreen(window, normal),
            state,
            RestoresMaximized: (placement.flags & WINDOWPLACEMENT_FLAGS.WPF_RESTORETOMAXIMIZED) != 0);
    }

    /// <summary>
    /// Sets another program's <paramref name="window"/>'s normal rectangle, the one it has when
    /// neither minimized nor maximized, and shows it there, or minimized, without activating it.
    /// A maximized window is restored, so it shows at once at the new place. Returns false when
    /// Windows refused. It waits for the window's program to handle it, so don't call it on a
    /// thread that must not wait.
    /// </summary>
    /// <param name="normalBounds">The normal rectangle, with its invisible borders, in screen coordinates.</param>
    /// <param name="minimized">Shows the window minimized instead.</param>
    /// <param name="restoresMaximized">A minimized window comes back maximized.</param>
    public static unsafe bool SetPlacement(nint window, Rectangle normalBounds, bool minimized, bool restoresMaximized)
    {
        var workspace = ToWorkspace(window, normalBounds);
        var placement = new WINDOWPLACEMENT
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            flags = restoresMaximized ? WINDOWPLACEMENT_FLAGS.WPF_RESTORETOMAXIMIZED : 0,
            showCmd = minimized ? SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE : SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE,
            rcNormalPosition = new RECT(workspace.Left, workspace.Top, workspace.Right, workspace.Bottom),
        };
        return PInvoke.SetWindowPlacement(new HWND(window), in placement);
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

    // Windows keeps a window's normal rectangle in workspace coordinates, which start at the top-left
    // corner of the work area of the display the rectangle is on, unless it's a tool window's.
    private static Rectangle ToScreen(nint window, Rectangle workspace)
    {
        if (IsToolWindow(window))
        {
            return workspace;
        }

        var offset = WorkAreaOffset(workspace);
        return workspace with { X = workspace.X + offset.X, Y = workspace.Y + offset.Y };
    }

    private static Rectangle ToWorkspace(nint window, Rectangle screen)
    {
        if (IsToolWindow(window))
        {
            return screen;
        }

        var offset = WorkAreaOffset(screen);
        return screen with { X = screen.X - offset.X, Y = screen.Y - offset.Y };
    }

    private static unsafe Point WorkAreaOffset(Rectangle area)
    {
        var monitor = PInvoke.MonitorFromRect(new RECT(area.Left, area.Top, area.Right, area.Bottom), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        return !monitor.IsNull && PInvoke.GetMonitorInfo(monitor, &info)
            ? new Point(info.rcWork.left - info.rcMonitor.left, info.rcWork.top - info.rcMonitor.top)
            : Point.Empty;
    }

    private static bool IsToolWindow(nint window) => (ExtendedStyle(new HWND(window)) & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0;

    // Follows the owners themselves: GA_ROOTOWNER follows them only through popup windows.
    private static bool IsOwnedBy(HWND window, HWND owner)
    {
        var current = window;
        for (var step = 0; step < MaximumWindowsInFront; step++)
        {
            current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_OWNER);
            if (current.IsNull)
            {
                return false;
            }

            if (current == owner)
            {
                return true;
            }
        }

        return false;
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

/// <summary>How a window shows, as <see cref="TopLevelWindows.Placement"/> read it.</summary>
/// <param name="NormalBounds">
/// Its rectangle when it's neither minimized nor maximized, with its invisible borders, in screen coordinates.
/// </param>
/// <param name="State">Whether it's minimized or maximized now, shown or hidden.</param>
/// <param name="RestoresMaximized">Being minimized, it comes back maximized.</param>
public sealed record WindowPlacement(Rectangle NormalBounds, WindowState State, bool RestoresMaximized);

/// <summary>Whether a window is minimized or maximized.</summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
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
