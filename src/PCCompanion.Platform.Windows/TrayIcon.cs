using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The app's icon in the notification area of the taskbar. It shows the icon of the executable
/// and is added again when Explorer restarts.
/// </summary>
/// <remarks>
/// The executable's manifest must depend on Common Controls 6, which provides <c>LoadIconMetric</c>.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    // The .NET SDK embeds <ApplicationIcon> as the icon group with this resource ID.
    private const ushort ApplicationIconResourceId = 32512;
    private const uint IconId = 1;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private readonly DestroyIconSafeHandle _icon;
    private readonly string _toolTip;
    private bool _disposed;

    /// <param name="window">Receives the icon's messages; the icon is used on its thread.</param>
    /// <param name="toolTip">Shown when the pointer rests on the icon.</param>
    public TrayIcon(MessageWindow window, string toolTip)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(toolTip);

        _window = window;
        _toolTip = toolTip;
        _icon = LoadApplicationIcon();
        _taskbarCreatedMessage = MessageWindow.RegisterMessage("TaskbarCreated");

        // The taskbar runs without admin rights; Windows would block its messages to an elevated window.
        if (Elevation.IsElevated)
        {
            window.AllowFromLowerIntegrity(CallbackMessage);
            window.AllowFromLowerIntegrity(_taskbarCreatedMessage);
        }

        _window.MessageReceived += OnMessageReceived;
        IsShown = Add();
    }

    /// <summary>Raised when the user clicks the icon or selects it with the keyboard.</summary>
    public event EventHandler? Selected;

    /// <summary>Raised when the user right-clicks the icon or opens its menu with the keyboard.</summary>
    public event EventHandler<TrayMenuEventArgs>? MenuRequested;

    /// <summary>
    /// False when Windows couldn't add the icon, for example because the taskbar isn't running yet.
    /// The icon is added when the taskbar starts.
    /// </summary>
    public bool IsShown { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        var data = CreateData();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _icon.Dispose();
    }

    private static unsafe DestroyIconSafeHandle LoadApplicationIcon()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        HICON icon;
        PInvoke.LoadIconMetric(
            (HINSTANCE)module.DangerousGetHandle(),
            (PCWSTR)(char*)ApplicationIconResourceId,
            _LI_METRIC.LIM_SMALL,
            &icon).ThrowOnFailure();

        return new DestroyIconSafeHandle(icon, ownsHandle: true);
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = (HWND)_window.Handle,
        uID = IconId,
    };

    private bool Add()
    {
        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = (HICON)_icon.DangerousGetHandle();
        data.szTip = _toolTip;

        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            return false;
        }

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage)
        {
            IsShown = Add();
            return;
        }

        if (e.Message != CallbackMessage)
        {
            return;
        }

        // With NOTIFYICON_VERSION_4 the low word of lParam is the event, and wParam holds the
        // screen point to show a menu at.
        switch ((uint)(e.LParam & 0xFFFF))
        {
            case PInvoke.NIN_SELECT:
            case PInvoke.NIN_SELECT | PInvoke.NINF_KEY:
                Selected?.Invoke(this, EventArgs.Empty);
                break;
            case PInvoke.WM_CONTEXTMENU:
                MenuRequested?.Invoke(this, new TrayMenuEventArgs((short)(e.WParam & 0xFFFF), (short)((e.WParam >> 16) & 0xFFFF)));
                break;
        }

        e.Handled = true;
    }
}
