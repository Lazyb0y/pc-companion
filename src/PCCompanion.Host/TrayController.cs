using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// The host's tray icon: selecting it opens the settings, and its menu has the running features'
/// items, then Settings and Exit. The menu follows the Windows mode, as the taskbar does. Use it on
/// the thread that runs the message loop.
/// </summary>
internal sealed partial class TrayController(SettingsAppLauncher settingsApp, FeatureRunner features, ILogger<TrayController> logger) : IDisposable
{
    private const uint SettingsCommand = 1;
    private const uint ExitCommand = 2;

    // The features' items get menu IDs from here on.
    private const int FirstFeatureCommand = 100;

    private MessageWindow? _window;
    private TrayIcon? _icon;
    private MenuMode? _menuMode;

    /// <summary>Asks the running host to open the settings, for example from a second host launch.</summary>
    public static uint OpenSettingsMessage { get; } = MessageWindow.RegisterMessage("PCCompanion.Host.OpenSettings");

    /// <summary>Asks the running host to exit, from the settings app or from setup.</summary>
    public static uint ExitMessage { get; } = MessageWindow.RegisterMessage(HostProcess.ExitMessageName);

    /// <summary>Raised when the user selects the notification <see cref="ShowNotification"/> showed.</summary>
    public event EventHandler? NotificationSelected;

    /// <summary>Shows a notification from the tray icon. Returns false when Windows refused.</summary>
    public bool ShowNotification(string title, string text) => _icon?.ShowNotification(title, text) ?? false;

    /// <param name="window">The host's window, created with <see cref="HostProcess.WindowClassName"/>.</param>
    public void Show(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessageReceived;

        // The settings app and a second host launch run without admin rights.
        if (Elevation.IsElevated)
        {
            _window.AllowFromLowerIntegrity(OpenSettingsMessage);
            _window.AllowFromLowerIntegrity(ExitMessage);
        }

        _icon = new TrayIcon(_window, Strings.AppDisplayName);
        _icon.Selected += (_, _) =>
        {
            LogIconSelected(logger);
            OpenSettings();
        };
        _icon.MenuRequested += (_, e) => ShowMenu(e.X, e.Y);
        _icon.NotificationSelected += (_, _) => NotificationSelected?.Invoke(this, EventArgs.Empty);

        if (!_icon.IsShown)
        {
            LogIconNotShown(logger);
        }

        if (!MenuTheme.IsSupported)
        {
            LogMenuModeNotSupported(logger, Environment.OSVersion.Version.Build);
        }
    }

    /// <summary>
    /// Opens the settings app, or brings it to the front, on the page of the feature with
    /// <paramref name="featureId"/>, or on the page it shows first. Call it on the UI thread.
    /// </summary>
    public void OpenSettings(string? featureId = null) => settingsApp.Open(featureId);

    /// <summary>Stops the host and closes the settings app, as Exit in the menu does. Call it on the UI thread.</summary>
    public void Exit()
    {
        // Exit closes the whole app, the settings app included.
        if (SessionSignal.Send(SettingsAppProcess.ExitSignalName))
        {
            LogClosingSettingsApp(logger);
        }

        MessageLoop.Quit();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        if (_window is not null)
        {
            _window.MessageReceived -= OnMessageReceived;
        }
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == OpenSettingsMessage)
        {
            LogOpenSettingsRequested(logger);
            OpenSettings();
            e.Handled = true;
        }
        else if (e.Message == ExitMessage)
        {
            LogExitRequested(logger);
            MessageLoop.Quit();
            e.Handled = true;
        }
        else if (MessageWindow.IsCloseRequest(e.Message, e.WParam))
        {
            // Setup closes the host before it removes or replaces its files.
            LogCloseRequested(logger);
            MessageLoop.Quit();
            e.Handled = true;
        }
    }

    private void ShowMenu(int x, int y)
    {
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        var items = features.GetMenuItems(FirstFeatureCommand).Select(ToPopupMenuItem).ToList();
        if (items.Count > 0)
        {
            items.Add(PopupMenuItem.Separator);
        }

        items.Add(new PopupMenuItem(SettingsCommand, Strings.TraySettings));
        items.Add(new PopupMenuItem(ExitCommand, Strings.TrayExit));

        FollowWindowsMode();
        LogMenuOpened(logger, rightToLeft);
        var command = PopupMenu.Show(_window!, items, x, y, rightToLeft);
        LogMenuClosed(logger, command switch { SettingsCommand => "Settings", ExitCommand => "Exit", 0 => "nothing", _ => "a feature's item" });

        switch (command)
        {
            case 0:
                break;
            case SettingsCommand:
                OpenSettings();
                break;
            case ExitCommand:
                Exit();
                break;
            default:
                features.ChooseMenuItem((int)command);
                break;
        }
    }

    // A feature's text shows as written, without access keys.
    private static PopupMenuItem ToPopupMenuItem(HostMenuItem item) => item switch
    {
        { IsSeparator: true } => PopupMenuItem.Separator,
        { Items: { } submenuItems } => PopupMenuItem.Submenu(PopupMenu.Escape(item.Text), [.. submenuItems.Select(ToPopupMenuItem)]) with { IsEnabled = item.IsEnabled },
        _ => new PopupMenuItem((uint)item.Id, PopupMenu.Escape(item.Text)) { IsEnabled = item.IsEnabled, IsChecked = item.IsChecked },
    };

    // Checked each time the menu opens, so a change of the Windows mode needs no notification.
    private void FollowWindowsMode()
    {
        if (!MenuTheme.IsSupported)
        {
            return;
        }

        var mode = MenuTheme.ForWindowsMode();
        if (mode == _menuMode)
        {
            return;
        }

        if (MenuTheme.Set(mode))
        {
            LogMenuModeSet(logger, mode);
        }
        else
        {
            LogMenuModeNotSet(logger, mode);
        }

        _menuMode = mode;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The tray icon was selected.")]
    private static partial void LogIconSelected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asked to exit, by the settings app or by setup before it removes or replaces the host's files.")]
    private static partial void LogExitRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows asked the host to close, as setup does before it removes or replaces its files.")]
    private static partial void LogCloseRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Another host launch asked to open the settings.")]
    private static partial void LogOpenSettingsRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opened the tray menu (right to left: {RightToLeft}).")]
    private static partial void LogMenuOpened(ILogger logger, bool rightToLeft);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Closed the tray menu; chose {Command}.")]
    private static partial void LogMenuClosed(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Asked the settings app to close.")]
    private static partial void LogClosingSettingsApp(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tray icon couldn't be added yet; it is added when the taskbar starts.")]
    private static partial void LogIconNotShown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The tray menu stays light: the dark menu wasn't tested on Windows build {Build}.")]
    private static partial void LogMenuModeNotSupported(ILogger logger, int build);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The tray menu follows the Windows mode: {Mode}.")]
    private static partial void LogMenuModeSet(ILogger logger, MenuMode mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't draw the tray menu in the Windows mode ({Mode}): uxtheme.dll doesn't have the functions.")]
    private static partial void LogMenuModeNotSet(ILogger logger, MenuMode mode);
}
