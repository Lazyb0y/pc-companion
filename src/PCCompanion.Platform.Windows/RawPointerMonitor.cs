using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports mouse input anywhere on the desktop through raw input, which costs nothing while the
/// mouse is still: moves, buttons and the wheel, with the time of each. Windows doesn't deliver it while an app running as administrator is in front,
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
    /// Read the position with <see cref="InputState.PointerPosition"/>. Raw input can report an
    /// event before the pointer's position follows it, and before low-level mouse hooks see it.
    /// </summary>
    public event EventHandler<PointerInputEventArgs>? InputReceived;

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

            var input = Read(e.LParam);
            foreach (var monitor in _monitors)
            {
                if (!monitor._disposed)
                {
                    monitor.InputReceived?.Invoke(monitor, input);
                }
            }
        }

        // The mouse's button flags and their data. Input that can't be read counts as a move.
        private static unsafe PointerInputEventArgs Read(nint handle)
        {
            var time = unchecked((uint)PInvoke.GetMessageTime());
            RAWINPUT data;
            var size = (uint)sizeof(RAWINPUT);
            var read = PInvoke.GetRawInputData(new HRAWINPUT(handle), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, &data, &size, (uint)sizeof(RAWINPUTHEADER));
            if (read == uint.MaxValue || read < sizeof(RAWINPUTHEADER) || data.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEMOUSE)
            {
                return new PointerInputEventArgs(PointerButtonChanges.None, 0, time);
            }

            var buttons = data.data.mouse.Anonymous.Anonymous;
            return PointerInputEventArgs.FromRawMouse(buttons.usButtonFlags, buttons.usButtonData, time);
        }

        private static unsafe bool Register(RAWINPUTDEVICE_FLAGS flags, HWND target)
        {
            var device = new RAWINPUTDEVICE { usUsagePage = GenericDesktopPage, usUsage = MouseUsage, dwFlags = flags, hwndTarget = target };
            return PInvoke.RegisterRawInputDevices(&device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }
    }
}
