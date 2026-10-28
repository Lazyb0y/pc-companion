using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A window the app draws pixel by pixel, with transparency, that stays on top of other windows
/// and never takes the focus, such as a strip on the taskbar or a notice. It isn't on the taskbar
/// or in Alt + Tab. Create, use and dispose it on one thread, which must run a
/// <see cref="MessageLoop"/>.
/// </summary>
public sealed class LayeredWindow : IDisposable
{
    private const int MaximumToolTipWidth = 600;

    // Kept in a field so the delegate lives as long as the window.
    private readonly WNDPROC _windowProcedure;
    private readonly string _className;
    private HWND _handle;
    private HWND _toolTip;
    private string _toolTipText = string.Empty;
    private bool _tracking;

    /// <param name="className">A window class name that is unique to the app, so other processes can find the window.</param>
    /// <param name="title">The window's name, which screen readers read.</param>
    /// <param name="clickThrough">
    /// Whether all of the window lets clicks and the pointer through to what's below it, as a
    /// notice does; it then raises no pointer events. Otherwise only fully transparent pixels do.
    /// </param>
    /// <exception cref="Win32Exception">Windows refused the window.</exception>
    public unsafe LayeredWindow(string className, string title, bool clickThrough = false)
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
                lpszClassName = name,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        var style = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        if (clickThrough)
        {
            style |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        }

        _handle = PInvoke.CreateWindowEx(
            style,
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

    /// <summary>Raised when the pointer comes onto the window or leaves it; see <see cref="IsPointerOver"/>.</summary>
    public event EventHandler? PointerOverChanged;

    /// <summary>Raised when the user clicks the window.</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the user right-clicks the window, with the screen point to show a menu at.</summary>
    public event EventHandler<TrayMenuEventArgs>? MenuRequested;

    /// <summary>Raised for the other messages the window receives, such as those every top-level window gets.</summary>
    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public nint Handle => _handle;

    public bool IsShown { get; private set; }

    public bool IsPointerOver { get; private set; }

    /// <summary>
    /// How opaque the window shows its pixels, from 0 for invisible to 255 for as drawn, which
    /// <see cref="Show"/> and <see cref="SetOpacity"/> use. 255 at first.
    /// </summary>
    public byte Opacity { get; private set; } = byte.MaxValue;

    /// <summary>
    /// Shows premultiplied 32-bit pixels (alpha, red, green and blue, from the high byte down),
    /// row by row from the top, at <paramref name="bounds"/> in physical pixels, with
    /// <see cref="Opacity"/>. Fully transparent pixels let clicks through.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't draw the window.</exception>
    public unsafe void Show(ReadOnlySpan<uint> pixels, Rectangle bounds)
    {
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Width, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Height, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, bounds.Width * bounds.Height, nameof(pixels));

        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        try
        {
            using var bitmap = TopDownBitmap.Create(memory, bounds.Width, bounds.Height, out var bits);

            pixels.CopyTo(new Span<uint>(bits, pixels.Length));
            var previous = PInvoke.SelectObject(memory, (HGDIOBJ)bitmap.DangerousGetHandle());
            try
            {
                var position = new Point(bounds.X, bounds.Y);
                var size = new SIZE(bounds.Width, bounds.Height);
                var origin = Point.Empty;
                var blend = Blend(Opacity);
                if (!PInvoke.UpdateLayeredWindow(_handle, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                PInvoke.SelectObject(memory, previous);
            }
        }
        finally
        {
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }

        if (!IsShown)
        {
            PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            IsShown = true;
        }
    }

    /// <summary>
    /// Changes how opaque the window is without drawing it again, as for a step of a fade. While
    /// the window is hidden, the next <see cref="Show"/> uses it.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't change it.</exception>
    public unsafe void SetOpacity(byte opacity)
    {
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        Opacity = opacity;
        if (!IsShown)
        {
            return;
        }

        // Without a source, Windows keeps the pixels, position and size it has.
        var blend = Blend(opacity);
        if (!PInvoke.UpdateLayeredWindow(_handle, HDC.Null, null, null, HDC.Null, null, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
        {
            throw new Win32Exception();
        }
    }

    /// <summary>Hides the window until the next <see cref="Show"/>.</summary>
    public void Hide()
    {
        if (!IsShown || _handle.IsNull)
        {
            return;
        }

        PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_HIDE);
        IsShown = false;
        SetPointerOver(false);
    }

    /// <summary>Sets the window's name, which screen readers read.</summary>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        PInvoke.SetWindowText(_handle, title);
    }

    /// <summary>
    /// Sets the tooltip shown while the pointer rests on the window. Lines are separated by
    /// <c>\n</c>.
    /// </summary>
    public unsafe void SetToolTip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        var adding = _toolTip.IsNull;
        if (adding)
        {
            _toolTip = CreateToolTip();
            if (_toolTip.IsNull)
            {
                return;
            }
        }

        if (!adding && text == _toolTipText)
        {
            return;
        }

        _toolTipText = text;
        var lines = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        fixed (char* textPointer = lines)
        {
            var tool = new TTTOOLINFOW
            {
                cbSize = (uint)sizeof(TTTOOLINFOW),
                uFlags = TOOLTIP_FLAGS.TTF_IDISHWND | TOOLTIP_FLAGS.TTF_SUBCLASS,
                hwnd = _handle,
                uId = (nuint)_handle.Value,
                lpszText = textPointer,
            };

            PInvoke.SendMessage(_toolTip, adding ? PInvoke.TTM_ADDTOOLW : PInvoke.TTM_UPDATETIPTEXTW, 0, (nint)(&tool));
        }
    }

    /// <summary>
    /// Brings the shown window in front of the other windows that stay on top, such as an app's
    /// that the user keeps on top, without taking the focus. Returns whether it did.
    /// </summary>
    public bool BringToFront() => IsShown && PInvoke.SetWindowPos(
        _handle,
        HWND.HWND_TOPMOST,
        0,
        0,
        0,
        0,
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

    /// <summary>
    /// Brings the window back in front of <paramref name="other"/> when <paramref name="other"/>
    /// came in front of it, as the taskbar does when it's clicked. Returns whether it did.
    /// </summary>
    public bool KeepInFrontOf(nint other)
    {
        if (!IsShown || !TopLevelWindows.IsInFrontOf(other, _handle))
        {
            return false;
        }

        return PInvoke.SetWindowPos(
            _handle,
            HWND.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    public void Dispose()
    {
        if (_handle.IsNull)
        {
            return;
        }

        if (!_toolTip.IsNull)
        {
            PInvoke.DestroyWindow(_toolTip);
            _toolTip = HWND.Null;
        }

        PInvoke.DestroyWindow(_handle);
        _handle = HWND.Null;
        IsShown = false;
        PInvoke.UnregisterClass(_className, PInvoke.GetModuleHandle((string?)null));
    }

    // Premultiplied pixels, shown with the window's opacity.
    private static BLENDFUNCTION Blend(byte opacity) => new()
    {
        BlendOp = (byte)PInvoke.AC_SRC_OVER,
        SourceConstantAlpha = opacity,
        AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA,
    };

    private unsafe HWND CreateToolTip()
    {
        var controls = new INITCOMMONCONTROLSEX
        {
            dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
            dwICC = INITCOMMONCONTROLSEX_ICC.ICC_WIN95_CLASSES,
        };
        PInvoke.InitCommonControlsEx(controls);

        // Always shown, since the window is never active; it's above the window, which is topmost.
        var toolTip = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            PInvoke.TOOLTIPS_CLASS,
            null,
            WINDOW_STYLE.WS_POPUP | (WINDOW_STYLE)(PInvoke.TTS_ALWAYSTIP | PInvoke.TTS_NOPREFIX),
            PInvoke.CW_USEDEFAULT,
            PInvoke.CW_USEDEFAULT,
            PInvoke.CW_USEDEFAULT,
            PInvoke.CW_USEDEFAULT,
            _handle,
            null,
            PInvoke.GetModuleHandle((string?)null),
            null);

        if (!toolTip.IsNull)
        {
            // A width lets the tooltip break its text into lines.
            PInvoke.SendMessage(toolTip, PInvoke.TTM_SETMAXTIPWIDTH, 0, MaximumToolTipWidth);
        }

        return toolTip;
    }

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
                return (LRESULT)0;

            case PInvoke.WM_MOUSELEAVE:
                _tracking = false;
                SetPointerOver(false);
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONUP:
                Clicked?.Invoke(this, EventArgs.Empty);
                return (LRESULT)0;

            case PInvoke.WM_RBUTTONUP:
                PInvoke.GetCursorPos(out var point);
                MenuRequested?.Invoke(this, new TrayMenuEventArgs(point.X, point.Y));
                return (LRESULT)0;
        }

        if (MessageReceived is { } handler)
        {
            var args = new WindowMessageEventArgs(message, wParam.Value, lParam.Value);
            handler(this, args);
            if (args.Handled)
            {
                return (LRESULT)0;
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
