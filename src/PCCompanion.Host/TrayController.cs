using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// The host's tray icon: selecting it opens the settings, and its menu has the running features'
/// items, then Settings and Exit. While an update is available, the menu starts with an item for
/// it, and the icon can have a dot. The menu follows the Windows mode, as the taskbar does. Use it
/// on the thread that runs the message loop.
/// </summary>
internal sealed partial class TrayController(SettingsAppLauncher settingsApp, FeatureRunner features, ILogger<TrayController> logger) : IDisposable
{
    private const uint SettingsCommand = 1;
    private const uint ExitCommand = 2;
    private const uint UpdateCommand = 3;

    // The features' items get menu IDs from here on.
    private const int FirstFeatureCommand = 100;

    private MessageWindow? _window;
    private TrayIcon? _icon;
    private MenuMode? _menuMode;
    private bool _disposed;

    // The update the menu's item names and whether the icon has the dot; then what the icon shows:
    // its tooltip, and the size and color of its dot, or null while it shows the executable's icon.
    private ReleaseVersion? _update;
    private bool _dot;
    private string _toolTip = Strings.AppDisplayName;
    private DotLooks? _shownDot;

    // Counts the dots asked for, so a drawing that finishes after a later request, or after the
    // dot went away, isn't shown.
    private int _dotRequests;

    /// <summary>Asks the running host to open the settings, for example from a second host launch.</summary>
    public static uint OpenSettingsMessage { get; } = MessageWindow.RegisterMessage("PCCompanion.Host.OpenSettings");

    /// <summary>Asks the running host to exit, from the settings app or from setup.</summary>
    public static uint ExitMessage { get; } = MessageWindow.RegisterMessage(HostProcess.ExitMessageName);

    /// <summary>Raised when the user selects the notification <see cref="ShowNotification"/> showed.</summary>
    public event EventHandler? NotificationSelected;

    /// <summary>Raised when the user chooses the update item that <see cref="ShowUpdate"/> put in the menu.</summary>
    public event EventHandler<UpdateChosenEventArgs>? UpdateChosen;

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

        // When the taskbar restarts, the display's scale may have changed too.
        _icon.AddedAgain += (_, _) => RedrawDot();
        _icon.AppearanceChanged += (_, _) => RedrawDot();

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
    /// Shows <paramref name="update"/> in the menu's first item and in the icon's tooltip, or
    /// neither when it's <see langword="null"/>, and the dot on the icon when <paramref name="dot"/>
    /// is true. Call it on the UI thread, and again after the host's language changes. See
    /// docs/features/general/update-check.md#tray-icon.
    /// </summary>
    public void ShowUpdate(ReleaseVersion? update, bool dot)
    {
        if (_icon is null || _disposed)
        {
            return;
        }

        (_update, _dot) = (update, dot);
        var request = ++_dotRequests;
        if (!dot)
        {
            ShowWithoutDot(UpdateToolTip());
            return;
        }

        // Drawn off the UI thread: the first drawing reads the accent color from Windows and the
        // icon from the executable, which takes tens of milliseconds.
        var window = _window!;
        _ = Task.Run(() =>
        {
            var drawn = DrawDot();
            if (!window.BeginInvoke(() => ShowDot(request, drawn)))
            {
                drawn.Picture?.Dispose();
            }
        });
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
        _disposed = true;
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

        // Kept, since the update can change while the menu is open.
        var update = _update;
        var items = update is { } version ? [new(UpdateCommand, PopupMenu.Escape(UpdateText(version))), PopupMenuItem.Separator] : new List<PopupMenuItem>();
        var featureItems = features.GetMenuItems(FirstFeatureCommand).Select(ToPopupMenuItem).ToList();
        items.AddRange(featureItems);
        if (featureItems.Count > 0)
        {
            items.Add(PopupMenuItem.Separator);
        }

        items.Add(new PopupMenuItem(SettingsCommand, Strings.TraySettings));
        items.Add(new PopupMenuItem(ExitCommand, Strings.TrayExit));

        FollowWindowsMode();
        LogMenuOpened(logger, rightToLeft);
        var command = PopupMenu.Show(_window!, items, x, y, rightToLeft);
        LogMenuClosed(logger, command switch { SettingsCommand => "Settings", ExitCommand => "Exit", UpdateCommand => "the update", 0 => "nothing", _ => "a feature's item" });

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
            case UpdateCommand when update is { } chosen:
                UpdateChosen?.Invoke(this, new UpdateChosenEventArgs(chosen));
                break;
            default:
                features.ChooseMenuItem((int)command);
                break;
        }
    }

    // "PC Companion 0.6.0 is available", parsed each time: the host's language can change while it runs.
    private static string UpdateText(ReleaseVersion update) =>
        string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.GeneralUpdateStatusAvailable), update);

    // On the thread pool, with the size and color, since the first read of the accent color is
    // slow. A size or color that changes later is drawn again when Windows says so.
    private static DrawnDot DrawDot()
    {
        try
        {
            var looks = new DotLooks(Displays.SmallIconSize(Displays.MainDisplayDpi()), AttentionDot.TaskbarColor());
            if (TrayIcon.ApplicationIconPixels(looks.Size) is not { } pixels)
            {
                return new DrawnDot(null, null, null);
            }

            AttentionDot.Draw(pixels, looks.Size, looks.Color);
            return new DrawnDot(looks, IconImage.FromPixels(pixels, looks.Size), null);
        }
        catch (Exception exception) when (exception is Win32Exception or COMException)
        {
            return new DrawnDot(null, null, exception);
        }
    }

    private string UpdateToolTip() => _update is { } update ? UpdateText(update) : Strings.AppDisplayName;

    // Draws the dot again when the taskbar restarts or Windows' settings, colors or display scale
    // change. It's shown only when its size or color changed.
    private void RedrawDot()
    {
        if (_dot)
        {
            ShowUpdate(_update, _dot);
        }
    }

    // On the UI thread, unless a later request or the dot going away replaced this one.
    private void ShowDot(int request, DrawnDot drawn)
    {
        if (request != _dotRequests || _disposed)
        {
            drawn.Picture?.Dispose();
            return;
        }

        var toolTip = UpdateToolTip();
        if (drawn is not { Looks: { } looks, Picture: { } picture })
        {
            LogDotNotDrawn(logger, drawn.Error);
            ShowWithoutDot(toolTip);
            return;
        }

        if (looks == _shownDot && toolTip == _toolTip)
        {
            picture.Dispose();
            return;
        }

        _icon!.Update(picture, toolTip);
        (_shownDot, _toolTip) = (looks, toolTip);
        LogDotShown(logger, looks.Size);
    }

    private void ShowWithoutDot(string toolTip)
    {
        if (_shownDot is not null || toolTip != _toolTip)
        {
            _icon!.ShowApplicationIcon(toolTip);
            (_shownDot, _toolTip) = (null, toolTip);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "The tray icon shows the update dot, {Size} pixels wide.")]
    private static partial void LogDotShown(ILogger logger, int size);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't draw the update dot on the tray icon, so it shows without it.")]
    private static partial void LogDotNotDrawn(ILogger logger, Exception? exception);

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

    // The size and color a dot was drawn in.
    private sealed record DotLooks(int Size, uint Color);

    // A dot drawn on the icon, or why it couldn't be drawn.
    private sealed record DrawnDot(DotLooks? Looks, IconImage? Picture, Exception? Error);
}

/// <summary>The update whose item in the tray menu the user chose.</summary>
internal sealed class UpdateChosenEventArgs(ReleaseVersion version) : EventArgs
{
    public ReleaseVersion Version { get; } = version;
}
