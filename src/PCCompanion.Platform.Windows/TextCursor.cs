using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>The app in front and its text cursor, as Windows reports them.</summary>
/// <param name="Window">
/// The window with the keyboard focus, or else the window in front; 0 when no window is in front.
/// </param>
/// <param name="TextCursor">
/// Windows' text cursor, in physical pixels on the screen, or <see langword="null"/> when the app
/// doesn't use it or the user can't see it.
/// </param>
public readonly record struct ForegroundTextCursor(nint Window, Rectangle? TextCursor);

/// <summary>
/// The text cursor (caret) of the app in front, where the user types, in physical pixels on the
/// screen, as a per-monitor DPI aware process sees them. Apps that use Windows' text cursor report
/// it to Windows; apps that draw their own, such as browsers, report it to accessibility tools.
/// </summary>
public static class TextCursor
{
    // STATE_SYSTEM_INVISIBLE, which marks a text cursor that isn't shown.
    private const int Invisible = 0x8000;

    /// <summary>
    /// Reads the app in front and Windows' text cursor of it. Windows keeps the position, so this
    /// doesn't ask the app or wait for it.
    /// </summary>
    public static unsafe ForegroundTextCursor Read()
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!PInvoke.GetGUIThreadInfo(0, ref info))
        {
            return default;
        }

        var window = info.hwndFocus.IsNull ? info.hwndActive : info.hwndFocus;
        return new ForegroundTextCursor(window, info.hwndCaret.IsNull ? null : FromWindows(info.hwndCaret, info.rcCaret));
    }

    /// <summary>
    /// Whether <paramref name="window"/>'s app may be asked for its text cursor with
    /// <see cref="AskAsync"/>: it runs at this process's integrity level, so that its answer, an
    /// object of the app's, doesn't come from a less trusted program.
    /// </summary>
    public static unsafe bool CanAsk(nint window)
    {
        uint processId = 0;
        return window != 0
            && PInvoke.GetWindowThreadProcessId(new HWND(window), &processId) != 0
            && Elevation.HasSameIntegrityLevel(processId);
    }

    /// <summary>
    /// Asks <paramref name="window"/> for its accessibility text cursor (<c>OBJID_CARET</c>), on a
    /// thread of its own, since that waits for the app. The result is <see langword="null"/> when
    /// the app doesn't answer within <paramref name="timeout"/>, answers only with Windows' standard
    /// answer, which comes from Windows' text cursor, or gives one the user can't see. The app's
    /// object can make the request wait longer than the timeout, but the task never fails for it.
    /// </summary>
    public static Task<Rectangle?> AskAsync(nint window, TimeSpan timeout)
    {
        var answer = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The app's object needs COM; this thread is in the multithreaded apartment, so nothing
        // waits on it but the request.
        var thread = new Thread(() =>
        {
            try
            {
                answer.SetResult(Ask(new HWND(window), timeout));
            }
            catch (Exception exception)
            {
                answer.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Text cursor request",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return answer.Task;
    }

    // Windows' text cursor, which the app gives in its window's client pixels as it sees them.
    private static unsafe Rectangle? FromWindows(HWND window, RECT caret)
    {
        if (caret.bottom <= caret.top || !PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOT)))
        {
            return null;
        }

        // As the app sees it: an app that isn't aware of each display's scale has pixels of its own.
        var topLeft = new Point(caret.left, caret.top);
        var bottomRight = new Point(caret.right, caret.bottom);
        var previous = PInvoke.SetThreadDpiAwarenessContext(PInvoke.GetWindowDpiAwarenessContext(window));
        try
        {
            if (!PInvoke.GetClientRect(window, out var client) || !Overlaps(caret, client)
                || !PInvoke.ClientToScreen(window, ref topLeft) || !PInvoke.ClientToScreen(window, ref bottomRight))
            {
                return null;
            }
        }
        finally
        {
            PInvoke.SetThreadDpiAwarenessContext(previous);
        }

        return OnDisplay(window, topLeft, bottomRight);
    }

    private static unsafe Rectangle? Ask(HWND window, TimeSpan timeout)
    {
        // A window that doesn't answer itself returns 0, and Windows' standard answer would be made
        // from Windows' text cursor, which was read first.
        var sent = PInvoke.SendMessageTimeout(window, PInvoke.WM_GETOBJECT, 0, (nint)OBJECT_IDENTIFIER.OBJID_CARET,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, (uint)timeout.TotalMilliseconds, out var result);
        if (sent == 0 || (nint)result <= 0)
        {
            return null;
        }

        if (PInvoke.ObjectFromLresult(new LRESULT((nint)result), typeof(IAccessible).GUID, 0, out var pointer).Failed)
        {
            return null;
        }

        IAccessible? accessible = null;
        try
        {
            accessible = (IAccessible)Marshal.GetObjectForIUnknown((nint)pointer);
            object self = (int)PInvoke.CHILDID_SELF;
            if (accessible.get_accState(self) is int state && (state & Invisible) != 0)
            {
                return null;
            }

            accessible.accLocation(out var left, out var top, out var width, out var height, self);
            if (height <= 0 || !PInvoke.GetWindowRect(PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOT), out var frame))
            {
                return null;
            }

            // In screen pixels as the app sees them; the window's frame is in this process's.
            var place = OnDisplay(window, new Point(left, top), new Point(left + width, top + height));
            return place is { } cursor && cursor.IntersectsWith(Rectangle.FromLTRB(frame.left, frame.top, frame.right, frame.bottom)) ? cursor : null;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            // The app ended, or its object isn't what it says.
            return null;
        }
        finally
        {
            if (accessible is not null)
            {
                Marshal.ReleaseComObject(accessible);
            }

            Marshal.Release((nint)pointer);
        }
    }

    // A text cursor in screen pixels as the window's app sees them, in physical pixels, when it's
    // on a display. Its last pixel is converted, which is in the window, unlike the edge after it.
    private static unsafe Rectangle? OnDisplay(HWND window, Point topLeft, Point bottomRight)
    {
        var last = new Point(Math.Max(bottomRight.X - 1, topLeft.X), bottomRight.Y - 1);
        if (!PInvoke.LogicalToPhysicalPointForPerMonitorDPI(window, ref topLeft) || !PInvoke.LogicalToPhysicalPointForPerMonitorDPI(window, ref last))
        {
            return null;
        }

        // At least one pixel wide, so a menu kept clear of it can't cover it.
        var cursor = Rectangle.FromLTRB(topLeft.X, topLeft.Y, Math.Max(last.X + 1, topLeft.X + 1), last.Y + 1);
        var area = new RECT { left = cursor.Left, top = cursor.Top, right = cursor.Right, bottom = cursor.Bottom };
        return PInvoke.MonitorFromRect(area, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull ? null : cursor;
    }

    private static bool Overlaps(RECT caret, RECT client) =>
        caret.left < client.right && caret.right >= client.left && caret.top < client.bottom && caret.bottom > client.top;
}
