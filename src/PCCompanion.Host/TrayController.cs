using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// The host's tray icon: selecting it opens the settings, and its menu has Settings and Exit.
/// Use it on the thread that runs the message loop.
/// </summary>
internal sealed partial class TrayController(SettingsAppLauncher settingsApp, ILogger<TrayController> logger) : IDisposable
{
    private const uint SettingsCommand = 1;
    private const uint ExitCommand = 2;

    private MessageWindow? _window;
    private TrayIcon? _icon;

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
            settingsApp.Open();
        };
        _icon.MenuRequested += (_, e) => ShowMenu(e.X, e.Y);
        _icon.NotificationSelected += (_, _) => NotificationSelected?.Invoke(this, EventArgs.Empty);

        if (!_icon.IsShown)
        {
            LogIconNotShown(logger);
        }
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
            settingsApp.Open();
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
        PopupMenuItem[] items = [new(SettingsCommand, Strings.TraySettings), new(ExitCommand, Strings.TrayExit)];

        LogMenuOpened(logger, rightToLeft);
        var command = PopupMenu.Show(_window!, items, x, y, rightToLeft);
        LogMenuClosed(logger, command switch { SettingsCommand => "Settings", ExitCommand => "Exit", _ => "nothing" });

        switch (command)
        {
            case SettingsCommand:
                settingsApp.Open();
                break;
            case ExitCommand:
                // Exit closes the whole app, the settings app included.
                if (SessionSignal.Send(SettingsAppProcess.ExitSignalName))
                {
                    LogClosingSettingsApp(logger);
                }

                MessageLoop.Quit();
                break;
        }
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
}
