using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// An invisible window over every display that takes the front, the pointer and the keys, so the
/// user can point at another program's window without that program getting the clicks or keys. It
/// draws nothing (<c>WS_EX_NOREDIRECTIONBITMAP</c>) and isn't on the taskbar or in Alt + Tab.
/// Create, use and dispose it on one thread, which must run a <see cref="MessageLoop"/>.
/// </summary>
public sealed class CoveringWindow : IDisposable
{
    // Kept in a field so the delegate lives as long as the window.
    private readonly WNDPROC _windowProcedure;
    private readonly string _className;
    private HWND _handle;
    private Point _origin;
    private CoveringPointer _pointer;

    /// <param name="className">A window class name that is unique to the app.</param>
    /// <param name="title">The window's name, which screen readers read as it takes the focus.</param>
    /// <exception cref="Win32Exception">Windows refused the window.</exception>
    public unsafe CoveringWindow(string className, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(title);

        _className = className;
        _windowProcedure = WindowProcedure;
        var instance = PInvoke.GetModuleHandle((string?)null);

        fixed (char* name = className)
        {
            // No class pointer: the window sets the pointer itself.
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = name,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        _handle = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            className,
            title,
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            instance,
            null);

        if (_handle.IsNull)
        {
            var exception = new Win32Exception();
            PInvoke.UnregisterClass(className, instance);
            throw exception;
        }
    }

    /// <summary>Raised when the pointer moves, with its place on the screen in physical pixels.</summary>
    public event EventHandler<CoveringPointEventArgs>? PointerMoved;

    /// <summary>Raised when a left or right click ends, as the button is released, with where it was.</summary>
    public event EventHandler<CoveringPointEventArgs>? Clicked;

    /// <summary>Raised for each key pressed, with Alt or without, while the window has the focus.</summary>
    public event EventHandler<CoveringKeyEventArgs>? KeyPressed;

    /// <summary>
    /// Raised when the shown window loses the front, as for Start, Alt + Tab, Ctrl + Alt + Del or
    /// locking the PC.
    /// </summary>
    public event EventHandler? Deactivated;

    /// <summary>Raised when displays are added, removed or change their resolution.</summary>
    public event EventHandler? DisplaysChanged;

    public nint Handle => _handle;

    public bool IsShown { get; private set; }

    /// <summary>The pointer the window shows; Windows' crosshair at first.</summary>
    public CoveringPointer PointerShape
    {
        get => _pointer;
        set
        {
            _pointer = value;
            if (IsShown)
            {
                SetPointer();
            }
        }
    }

    /// <summary>
    /// Covers every display and takes the front, which Windows allows right after the user pressed
    /// a shortcut or chose a menu item of this process. Returns whether the window is in front.
    /// </summary>
    public bool Show()
    {
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        _origin = new Point(
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN));
        PInvoke.SetWindowPos(
            _handle,
            HWND.HWND_TOPMOST,
            _origin.X,
            _origin.Y,
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN),
            SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        IsShown = true;
        PInvoke.SetForegroundWindow(_handle);
        SetPointer();
        return PInvoke.GetForegroundWindow() == _handle;
    }

    /// <summary>Hides the window, which gives the front to the window under it.</summary>
    public void Hide()
    {
        if (!IsShown || _handle.IsNull)
        {
            return;
        }

        IsShown = false;
        PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_HIDE);
    }

    /// <summary>
    /// Sets the window's name, which screen readers read; Windows reports the change to them
    /// (<c>EVENT_OBJECT_NAMECHANGE</c>).
    /// </summary>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        PInvoke.SetWindowText(_handle, title);
    }

    public void Dispose()
    {
        if (_handle.IsNull)
        {
            return;
        }

        IsShown = false;
        PInvoke.DestroyWindow(_handle);
        _handle = HWND.Null;
        PInvoke.UnregisterClass(_className, PInvoke.GetModuleHandle((string?)null));
    }

    private void SetPointer() =>
        PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, _pointer == CoveringPointer.Unavailable ? PInvoke.IDC_NO : PInvoke.IDC_CROSS));

    private Point ScreenPoint(LPARAM lParam) =>
        new(_origin.X + (short)(lParam.Value & 0xFFFF), _origin.Y + (short)((lParam.Value >> 16) & 0xFFFF));

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (window != _handle)
        {
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case PInvoke.WM_SETCURSOR:
                SetPointer();
                return (LRESULT)1;

            case PInvoke.WM_ERASEBKGND:
                return (LRESULT)1;

            case PInvoke.WM_MOUSEMOVE:
                PointerMoved?.Invoke(this, new CoveringPointEventArgs(ScreenPoint(lParam), isRight: false));
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONUP:
            case PInvoke.WM_RBUTTONUP:
                Clicked?.Invoke(this, new CoveringPointEventArgs(ScreenPoint(lParam), message == PInvoke.WM_RBUTTONUP));
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONDOWN:
            case PInvoke.WM_RBUTTONDOWN:
            case PInvoke.WM_CONTEXTMENU:
                return (LRESULT)0;

            case PInvoke.WM_KEYDOWN:
            case PInvoke.WM_SYSKEYDOWN:
                var shift = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0;
                KeyPressed?.Invoke(this, new CoveringKeyEventArgs((uint)wParam.Value, shift));
                return (LRESULT)0;

            case PInvoke.WM_ACTIVATE:
                if ((wParam.Value & 0xFFFF) == PInvoke.WA_INACTIVE && IsShown)
                {
                    Deactivated?.Invoke(this, EventArgs.Empty);
                }

                break;

            case PInvoke.WM_DISPLAYCHANGE:
                DisplaysChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}

/// <summary>The pointer a <see cref="CoveringWindow"/> shows.</summary>
public enum CoveringPointer
{
    /// <summary>Windows' crosshair (<c>IDC_CROSS</c>).</summary>
    Cross,

    /// <summary>Windows' Unavailable pointer (<c>IDC_NO</c>).</summary>
    Unavailable,
}

/// <summary>Where the pointer is, in physical pixels on the screen, and for a click, which button.</summary>
public sealed class CoveringPointEventArgs(Point point, bool isRight) : EventArgs
{
    public Point Point { get; } = point;

    /// <summary>Whether it's the right button's click; otherwise the left button's, or a move.</summary>
    public bool IsRight { get; } = isRight;
}

/// <summary>A key pressed on a <see cref="CoveringWindow"/>: its virtual key, and whether Shift was held.</summary>
public sealed class CoveringKeyEventArgs(uint virtualKey, bool shift) : EventArgs
{
    public uint VirtualKey { get; } = virtualKey;

    public bool Shift { get; } = shift;
}
