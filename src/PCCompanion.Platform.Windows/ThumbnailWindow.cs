using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A window that shows a picture of part of another window, which Windows draws and keeps up to
/// date (a DWM thumbnail), such as a rolled-up window's title bar. It never takes the focus and
/// isn't on the taskbar or in Alt + Tab. Create, use and dispose it on one thread, which must run a
/// <see cref="MessageLoop"/>. Positions are in physical pixels.
/// </summary>
public sealed class ThumbnailWindow : IDisposable
{
    // Kept in a field so the delegate lives as long as the window.
    private readonly WNDPROC _windowProcedure;
    private readonly string _className;
    private HWND _handle;
    private nint _thumbnail;
    private bool _tracking;

    /// <param name="className">A window class name that is unique to the app.</param>
    /// <param name="title">The window's name, which screen readers read.</param>
    /// <param name="source">The window the picture is of.</param>
    /// <param name="bounds">Where the window shows.</param>
    /// <param name="part">The part of <paramref name="source"/> shown, in its rectangle with its invisible borders.</param>
    /// <param name="topmost">Whether the window stays on top of other windows.</param>
    /// <exception cref="Win32Exception">Windows refused the window or the picture.</exception>
    public unsafe ThumbnailWindow(string className, string title, nint source, Rectangle bounds, Rectangle part, bool topmost)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(title);

        _className = className;
        _windowProcedure = WindowProcedure;
        var instance = PInvoke.GetModuleHandle((string?)null);

        fixed (char* name = className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                hbrBackground = (HBRUSH)(nint)(SYS_COLOR_INDEX.COLOR_WINDOWFRAME + 1),
                lpszClassName = name,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        var style = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        if (topmost)
        {
            style |= WINDOW_EX_STYLE.WS_EX_TOPMOST;
        }

        _handle = PInvoke.CreateWindowEx(style, className, title, WINDOW_STYLE.WS_POPUP, bounds.X, bounds.Y, bounds.Width, bounds.Height, HWND.Null, null, instance, null);
        if (_handle.IsNull)
        {
            var exception = new Win32Exception();
            PInvoke.UnregisterClass(className, instance);
            throw exception;
        }

        // The picture is taken as the window is shown, before the source goes away.
        var result = PInvoke.DwmRegisterThumbnail(_handle, new HWND(source), out var thumbnail);
        if (result.Failed)
        {
            Dispose();
            throw new Win32Exception(result.Value);
        }

        _thumbnail = thumbnail;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_RECTSOURCE | PInvoke.DWM_TNP_OPACITY | PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT(0, 0, bounds.Width, bounds.Height),
            rcSource = new RECT(part.Left, part.Top, part.Right, part.Bottom),
            opacity = byte.MaxValue,
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        PInvoke.DwmUpdateThumbnailProperties(_thumbnail, properties);
        PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    /// <summary>Raised when the pointer comes onto the window or leaves it; see <see cref="IsPointerOver"/>.</summary>
    public event EventHandler? PointerOverChanged;

    /// <summary>
    /// Raised for each move of the pointer over the window, or anywhere while the window has it
    /// (<see cref="CapturePointer"/>), each press and release of the left button and each release
    /// of the right one, with the position in the window's pixels.
    /// </summary>
    public event EventHandler<WindowPointerEventArgs>? PointerInput;

    /// <summary>Raised when the wheel turns over the window: above zero up, below zero down, 120 a notch.</summary>
    public event EventHandler<int>? WheelTurned;

    public nint Handle => _handle;

    public bool IsPointerOver { get; private set; }

    /// <summary>Where the window is.</summary>
    public Rectangle Bounds
    {
        get
        {
            PInvoke.GetWindowRect(_handle, out var rect);
            return Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
        }
    }

    /// <summary>Moves the window, without taking the focus.</summary>
    public bool MoveTo(Point location) =>
        PInvoke.SetWindowPos(_handle, HWND.Null, location.X, location.Y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>Makes the window stay on top of other windows, or no longer, without taking the focus.</summary>
    public bool SetTopmost(bool topmost) =>
        PInvoke.SetWindowPos(_handle, topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public void Show() => PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

    public void Hide() => PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Gets the pointer's moves and button releases wherever the pointer goes, as during a drag.</summary>
    public void CapturePointer() => PInvoke.SetCapture(_handle);

    /// <summary>Lets the pointer's input go to the windows under it again.</summary>
    public void ReleasePointer()
    {
        if (!_handle.IsNull && PInvoke.GetCapture() == _handle)
        {
            PInvoke.ReleaseCapture();
        }
    }

    public void Dispose()
    {
        if (_thumbnail != 0)
        {
            PInvoke.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }

        if (_handle.IsNull)
        {
            return;
        }

        PInvoke.DestroyWindow(_handle);
        _handle = HWND.Null;
        PInvoke.UnregisterClass(_className, PInvoke.GetModuleHandle((string?)null));
    }

    // Mouse messages carry the position in the window's pixels as two signed 16-bit numbers.
    private void RaisePointerInput(WindowPointerAction action, LPARAM lParam) =>
        PointerInput?.Invoke(this, new WindowPointerEventArgs(action, new Point((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF))));

    private void SetPointerOver(bool over)
    {
        if (over == IsPointerOver)
        {
            return;
        }

        IsPointerOver = over;
        PointerOverChanged?.Invoke(this, EventArgs.Empty);
    }

    private unsafe LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (window != _handle)
        {
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case PInvoke.WM_MOUSEACTIVATE:
                return (LRESULT)(nint)PInvoke.MA_NOACTIVATE;

            case PInvoke.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = _handle };
                    _tracking = PInvoke.TrackMouseEvent(ref track);
                }

                SetPointerOver(true);
                RaisePointerInput(WindowPointerAction.Moved, lParam);
                return (LRESULT)0;

            case PInvoke.WM_MOUSELEAVE:
                _tracking = false;
                SetPointerOver(false);
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONDOWN:
                RaisePointerInput(WindowPointerAction.LeftDown, lParam);
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONUP:
                RaisePointerInput(WindowPointerAction.LeftUp, lParam);
                return (LRESULT)0;

            case PInvoke.WM_RBUTTONUP:
                RaisePointerInput(WindowPointerAction.RightUp, lParam);
                return (LRESULT)0;

            case PInvoke.WM_CAPTURECHANGED:
                RaisePointerInput(WindowPointerAction.CaptureLost, (LPARAM)0);
                return (LRESULT)0;

            // The wheel's turn is in the high word of wParam, as a signed number.
            case PInvoke.WM_MOUSEWHEEL:
                WheelTurned?.Invoke(this, (short)((wParam.Value >> 16) & 0xFFFF));
                return (LRESULT)0;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
