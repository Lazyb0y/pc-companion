using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports when the displays turn off and back on, until disposed, as Windows reports the console's
/// display state (<c>GUID_CONSOLE_DISPLAY_STATE</c>). Windows reports the current state right
/// after the monitor is created.
/// </summary>
public sealed class DisplayStateMonitor : IDisposable
{
    private const uint Off = 0;

    private readonly MessageWindow _window;
    private readonly UnregisterPowerSettingNotificationSafeHandle _registration;
    private bool _disposed;

    /// <param name="window">The window that receives the reports, on the thread that uses the monitor.</param>
    /// <exception cref="Win32Exception">Windows refused to report the display state.</exception>
    public unsafe DisplayStateMonitor(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var setting = PInvoke.GUID_CONSOLE_DISPLAY_STATE;
        var registration = PInvoke.RegisterPowerSettingNotification((HANDLE)window.Handle, &setting, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);

        // A failure returns null, which the safe handle wouldn't see as invalid.
        if (registration == default)
        {
            throw new Win32Exception();
        }

        _registration = new UnregisterPowerSettingNotificationSafeHandle((nint)registration, ownsHandle: true);
        _window = window;
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>Raised on the window's thread when the displays turn off.</summary>
    public event EventHandler? TurnedOff;

    /// <summary>Raised on the window's thread when the displays turn on, or are dimmed.</summary>
    public event EventHandler? TurnedOn;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        _registration.Dispose();
    }

    private unsafe void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != PInvoke.WM_POWERBROADCAST || e.WParam != PInvoke.PBT_POWERSETTINGCHANGE || e.LParam == 0)
        {
            return;
        }

        var setting = (POWERBROADCAST_SETTING*)e.LParam;
        if (setting->PowerSetting != PInvoke.GUID_CONSOLE_DISPLAY_STATE || setting->DataLength < sizeof(uint))
        {
            return;
        }

        // 0 off, 1 on, 2 dimmed.
        if (*(uint*)&setting->Data == Off)
        {
            TurnedOff?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            TurnedOn?.Invoke(this, EventArgs.Empty);
        }
    }
}
