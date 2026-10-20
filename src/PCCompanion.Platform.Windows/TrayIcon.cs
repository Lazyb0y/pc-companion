using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// An icon in the notification area of the taskbar: the app's own, which shows the icon of the
/// executable, or one the app draws, such as a meter. It's added again when Explorer restarts.
/// </summary>
/// <remarks>
/// The executable's manifest must depend on Common Controls 6, which provides <c>LoadIconMetric</c>.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    /// <summary>The ID of the app's own icon. The app's other icons use other IDs.</summary>
    public const uint ApplicationIconId = 1;

    // The .NET SDK embeds <ApplicationIcon> as the icon group with this resource ID.
    private const ushort ApplicationIconResourceId = 32512;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;

    // The lengths of the tooltip, the notification's title and its text, without the terminating null.
    private const int MaximumToolTipLength = 127;
    private const int MaximumTitleLength = 63;
    private const int MaximumTextLength = 255;

    private readonly MessageWindow _window;
    private readonly uint _id;
    private readonly uint _taskbarCreatedMessage;
    private readonly string _name;
    private readonly DestroyIconSafeHandle? _applicationIcon;
    private IconImage? _image;
    private string _toolTip;
    private DestroyIconSafeHandle? _notificationIcon;
    private bool _disposed;

    /// <summary>Adds the app's own icon.</summary>
    /// <param name="window">Receives the icon's messages; the icon is used on its thread.</param>
    /// <param name="toolTip">Shown when the pointer rests on the icon.</param>
    public TrayIcon(MessageWindow window, string toolTip)
        : this(window, ApplicationIconId, toolTip, LoadApplicationIcon(_LI_METRIC.LIM_SMALL), null)
    {
    }

    /// <summary>
    /// Adds an icon the app draws, such as a meter, and takes over <paramref name="icon"/>.
    /// </summary>
    /// <param name="window">Receives the icon's messages; the icon is used on its thread.</param>
    /// <param name="id">
    /// The icon's ID, other than <see cref="ApplicationIconId"/>. Windows remembers where the user
    /// put an icon by the program's path and this ID, so keep it the same from one start to the next.
    /// </param>
    /// <param name="toolTip">
    /// Shown when the pointer rests on the icon. Windows 11 names the icon for screen readers with
    /// the tooltip it was added with, followed by the current one, so give a name that stays the
    /// same here, and changing values with <see cref="Update"/>. The icon is added with this name
    /// again when the taskbar restarts.
    /// </param>
    public TrayIcon(MessageWindow window, uint id, IconImage icon, string toolTip)
        : this(window, id, toolTip, null, icon ?? throw new ArgumentNullException(nameof(icon)))
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, ApplicationIconId);
    }

    private TrayIcon(MessageWindow window, uint id, string toolTip, DestroyIconSafeHandle? applicationIcon, IconImage? image)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(toolTip);

        _window = window;
        _id = id;
        _toolTip = toolTip;
        _name = toolTip;
        _applicationIcon = applicationIcon;
        _image = image;
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

    /// <summary>Raised when the user selects the notification <see cref="ShowNotification"/> showed.</summary>
    public event EventHandler? NotificationSelected;

    /// <summary>
    /// False when Windows couldn't add the icon, for example because the taskbar isn't running yet.
    /// The icon is added when the taskbar starts.
    /// </summary>
    public bool IsShown { get; private set; }

    private HICON CurrentIcon => (HICON)(_image?.Handle ?? _applicationIcon!.DangerousGetHandle());

    /// <summary>
    /// Shows a new picture and tooltip, and takes over <paramref name="icon"/>; the one before is
    /// destroyed, since the taskbar keeps a copy. When the taskbar lost the icon, it's added
    /// again. Returns false when Windows refused.
    /// </summary>
    public bool Update(IconImage icon, string toolTip)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(toolTip);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var previous = _image;
        (_image, _toolTip) = (icon, toolTip);
        IsShown = Modify() || Add();

        if (!ReferenceEquals(previous, icon))
        {
            previous?.Dispose();
        }

        return IsShown;
    }

    /// <summary>
    /// Shows a notification from the icon, which Windows shows as a toast and keeps in the
    /// notification center, unless the user turned off PC Companion's notifications or quiet time
    /// applies. Text that doesn't fit is cut. Returns false when Windows refused, for example
    /// because the icon isn't shown.
    /// </summary>
    public bool ShowNotification(string title, string text)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        _notificationIcon ??= LoadApplicationIcon(_LI_METRIC.LIM_LARGE);

        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.szInfoTitle = Fit(title, MaximumTitleLength);
        data.szInfo = Fit(text, MaximumTextLength);
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_USER | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_LARGE_ICON | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_RESPECT_QUIET_TIME;
        data.hBalloonIcon = (HICON)_notificationIcon.DangerousGetHandle();
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

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
        _applicationIcon?.Dispose();
        _image?.Dispose();
        _notificationIcon?.Dispose();
    }

    // Cuts text that doesn't fit, without splitting a character made of two UTF-16 code units.
    private static string Fit(string text, int maximumLength)
    {
        if (text.Length <= maximumLength)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[maximumLength - 1]) ? maximumLength - 1 : maximumLength)];
    }

    private static unsafe DestroyIconSafeHandle LoadApplicationIcon(_LI_METRIC size)
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        HICON icon;
        PInvoke.LoadIconMetric(
            (HINSTANCE)module.DangerousGetHandle(),
            (PCWSTR)(char*)ApplicationIconResourceId,
            size,
            &icon).ThrowOnFailure();

        return new DestroyIconSafeHandle(icon, ownsHandle: true);
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = (HWND)_window.Handle,
        uID = _id,
    };

    // Adds the icon with its name, which screen readers keep, then shows the current tooltip.
    private bool Add()
    {
        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = CurrentIcon;
        data.szTip = Fit(_name, MaximumToolTipLength);

        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            return false;
        }

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data) && (_toolTip == _name || Modify());
    }

    private bool Modify()
    {
        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.hIcon = CurrentIcon;
        data.szTip = Fit(_toolTip, MaximumToolTipLength);
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage)
        {
            IsShown = Add();
            return;
        }

        // With NOTIFYICON_VERSION_4 the low word of lParam is the event, its high word the icon's
        // ID, and wParam holds the screen point to show a menu at. A window can have several icons.
        if (e.Message != CallbackMessage || (uint)((e.LParam >> 16) & 0xFFFF) != _id)
        {
            return;
        }

        switch ((uint)(e.LParam & 0xFFFF))
        {
            case PInvoke.NIN_SELECT:
            case PInvoke.NIN_SELECT | PInvoke.NINF_KEY:
                Selected?.Invoke(this, EventArgs.Empty);
                break;
            case PInvoke.WM_CONTEXTMENU:
                MenuRequested?.Invoke(this, new TrayMenuEventArgs((short)(e.WParam & 0xFFFF), (short)((e.WParam >> 16) & 0xFFFF)));
                break;
            case PInvoke.NIN_BALLOONUSERCLICK:
                NotificationSelected?.Invoke(this, EventArgs.Empty);
                break;
        }

        e.Handled = true;
    }
}
