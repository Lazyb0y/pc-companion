using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports mouse input anywhere on the desktop through raw input, which costs nothing while the
/// mouse is still. Windows doesn't deliver it while an app running as administrator is in front,
/// unless this process runs as administrator too. A process has one raw input target per device
/// type, so create one monitor at a time.
/// </summary>
public sealed class RawPointerMonitor : IDisposable
{
    private const ushort GenericDesktopPage = 0x01;
    private const ushort MouseUsage = 0x02;

    private readonly MessageWindow _window;
    private bool _disposed;

    /// <param name="window">The window that receives the input, on the thread that uses the monitor.</param>
    public RawPointerMonitor(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!Register(RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, (HWND)window.Handle))
        {
            throw new Win32Exception();
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Raised on the window's thread for each mouse event: movement, a button or the wheel.
    /// Read the position with <see cref="InputState.PointerPosition"/>.
    /// </summary>
    public event EventHandler? InputReceived;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        Register(RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, HWND.Null);
    }

    private static unsafe bool Register(RAWINPUTDEVICE_FLAGS flags, HWND target)
    {
        var device = new RAWINPUTDEVICE { usUsagePage = GenericDesktopPage, usUsage = MouseUsage, dwFlags = flags, hwndTarget = target };
        return PInvoke.RegisterRawInputDevices(&device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    // Left unhandled, so DefWindowProc frees the input data.
    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == PInvoke.WM_INPUT)
        {
            InputReceived?.Invoke(this, EventArgs.Empty);
        }
    }
}
