using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// The host's tray icon: selecting it opens the settings, and its menu has Settings and Exit.
/// Use it on the thread that runs the message loop.
/// </summary>
internal sealed partial class TrayController(SettingsAppLauncher settingsApp, ILogger<TrayController> logger) : IDisposable
{
    /// <summary>The class name of the host's hidden window, which a second host launch looks for.</summary>
    public const string WindowClassName = "PCCompanion.Host";

    private const uint SettingsCommand = 1;
    private const uint ExitCommand = 2;

    private MessageWindow? _window;
    private TrayIcon? _icon;

    /// <summary>Asks the running host to open the settings, for example from a second host launch.</summary>
    public static uint OpenSettingsMessage { get; } = MessageWindow.RegisterMessage("PCCompanion.Host.OpenSettings");

    /// <param name="window">The host's window, created with <see cref="WindowClassName"/>.</param>
    public void Show(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessageReceived;

        _icon = new TrayIcon(_window, Strings.AppDisplayName);
        _icon.Selected += (_, _) => settingsApp.Open();
        _icon.MenuRequested += (_, e) => ShowMenu(e.X, e.Y);

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
            settingsApp.Open();
            e.Handled = true;
        }
    }

    private void ShowMenu(int x, int y)
    {
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        PopupMenuItem[] items = [new(SettingsCommand, Strings.TraySettings), new(ExitCommand, Strings.TrayExit)];

        switch (PopupMenu.Show(_window!, items, x, y, rightToLeft))
        {
            case SettingsCommand:
                settingsApp.Open();
                break;
            case ExitCommand:
                MessageLoop.Quit();
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tray icon couldn't be added yet; it is added when the taskbar starts.")]
    private static partial void LogIconNotShown(ILogger logger);
}
