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
/// unless this process runs as administrator too. A process has one raw input registration per
/// device type, so every monitor shares one: the first monitor makes it, with a hidden window on
/// its thread, and the last one disposed removes it. While any monitor exists, create and dispose
/// monitors only on that thread, which runs a <see cref="MessageLoop"/>: the host's UI thread.
/// </summary>
public sealed class RawPointerMonitor : IDisposable
{
    private bool _disposed;

    /// <exception cref="Win32Exception">Windows refused to report mouse input.</exception>
    /// <exception cref="InvalidOperationException">Monitors exist on another thread.</exception>
    public RawPointerMonitor()
    {
        Registration.Add(this);
    }

    /// <summary>
    /// Raised on the monitors' thread for each mouse event: movement, a button or the wheel.
    /// Read the position with <see cref="InputState.PointerPosition"/>.
    /// </summary>
    public event EventHandler? InputReceived;

    /// <exception cref="InvalidOperationException">Called on another thread than the monitors'.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Registration.Remove(this);
    }

    // The process's one registration for mouse raw input, its window and the monitors that share it.
    private static class Registration
    {
        private const ushort GenericDesktopPage = 0x01;
        private const ushort MouseUsage = 0x02;
        private const string WindowClassName = "PCCompanion.RawPointerInput";

        // Replaced, never changed, when a monitor comes or goes, so each event reads it without copying.
        private static RawPointerMonitor[] _monitors = [];
        private static MessageWindow? _window;
        private static int _threadId;

        public static void Add(RawPointerMonitor monitor)
        {
            if (_window is not null)
            {
                CheckThread();
            }
            else
            {
                var window = new MessageWindow(WindowClassName);
                if (!Register(RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, (HWND)window.Handle))
                {
                    var exception = new Win32Exception();
                    window.Dispose();
                    throw exception;
                }

                window.MessageReceived += OnMessageReceived;
                (_window, _threadId) = (window, Environment.CurrentManagedThreadId);
            }

            _monitors = [.. _monitors, monitor];
        }

        public static void Remove(RawPointerMonitor monitor)
        {
            CheckThread();
            var remaining = Array.FindAll(_monitors, other => other != monitor);
            if (remaining.Length == _monitors.Length)
            {
                return;
            }

            _monitors = remaining;
            if (remaining.Length > 0 || _window is not { } window)
            {
                return;
            }

            Register(RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, HWND.Null);
            window.MessageReceived -= OnMessageReceived;
            window.Dispose();
            _window = null;
        }

        // Windows delivers the input to the window's thread only.
        private static void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != _threadId)
            {
                throw new InvalidOperationException("Raw pointer monitors exist on another thread.");
            }
        }

        // Left unhandled, so DefWindowProc frees the input data. A handler can dispose its monitor or
        // create another; the event goes on to the monitors there were when it came.
        private static void OnMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message != PInvoke.WM_INPUT)
            {
                return;
            }

            foreach (var monitor in _monitors)
            {
                if (!monitor._disposed)
                {
                    monitor.InputReceived?.Invoke(monitor, EventArgs.Empty);
                }
            }
        }

        private static unsafe bool Register(RAWINPUTDEVICE_FLAGS flags, HWND target)
        {
            var device = new RAWINPUTDEVICE { usUsagePage = GenericDesktopPage, usUsage = MouseUsage, dwFlags = flags, hwndTarget = target };
            return PInvoke.RegisterRawInputDevices(&device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }
    }
}
