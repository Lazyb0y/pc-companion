using System;
using System.Collections.Generic;
using System.Globalization;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// The meters' readings, icons, menu and Task Manager, from Windows.
/// </summary>
internal sealed class WindowsMetersSystem : IMetersSystem
{
    private const uint TaskManagerCommand = 1;
    private const uint SettingsCommand = 2;
    private const uint ExitCommand = 3;

    public ISystemReadings Readings { get; } = new WindowsSystemReadings();

    public IMeterAppearance Appearance { get; } = new WindowsMeterAppearance();

    public int WindowsBuild => Environment.OSVersion.Version.Build;

    public IMeterIcons CreateIcons(MessageWindow window) => new NotificationIcons(window);

    public IStripHost CreateStripHost(MessageWindow window) => new WindowsStripHost(window);

    public IMeterPauses WatchPauses(MessageWindow window) => new WindowsMeterPauses(window);

    public bool OpenTaskManager() => ShellActions.OpenTaskManager();

    // The menu follows the Windows mode and the language, as the PC Companion icon's menu does.
    public MeterMenuCommand ShowMenu(MessageWindow window, int x, int y)
    {
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        PopupMenuItem[] items =
        [
            new(TaskManagerCommand, Strings.SystemMetersTaskManager),
            new(SettingsCommand, Strings.TraySettings),
            new(ExitCommand, Strings.TrayExit),
        ];

        if (MenuTheme.IsSupported)
        {
            MenuTheme.Set(MenuTheme.ForWindowsMode());
        }

        return PopupMenu.Show(window, items, x, y, rightToLeft) switch
        {
            TaskManagerCommand => MeterMenuCommand.TaskManager,
            SettingsCommand => MeterMenuCommand.Settings,
            ExitCommand => MeterMenuCommand.Exit,
            _ => MeterMenuCommand.None,
        };
    }

    private sealed class WindowsMeterAppearance : IMeterAppearance
    {
        private const uint White = 0xFFFFFF;
        private const uint Black = 0x000000;

        public int IconSize() => Displays.SmallIconSize(Displays.MainDisplayDpi());

        public uint Color() =>
            WindowsTheme.IsHighContrast() ? WindowsTheme.TextColor() : WindowsTheme.IsDark() ? White : Black;

        public bool IsHighContrast() => WindowsTheme.IsHighContrast();
    }

    // The session's lock and the displays' state, from Windows' notifications to the meters' window.
    private sealed class WindowsMeterPauses : IMeterPauses
    {
        private readonly SessionMonitor _sessions;
        private readonly DisplayStateMonitor _displays;

        public WindowsMeterPauses(MessageWindow window)
        {
            _sessions = new SessionMonitor(window);
            try
            {
                _displays = new DisplayStateMonitor(window);
            }
            catch
            {
                _sessions.Dispose();
                throw;
            }

            _sessions.Locked += (_, e) => Locked?.Invoke(this, e);
            _sessions.Unlocked += (_, e) => Unlocked?.Invoke(this, e);
            _displays.TurnedOff += (_, e) => DisplaysOff?.Invoke(this, e);
            _displays.TurnedOn += (_, e) => DisplaysOn?.Invoke(this, e);
        }

        public event EventHandler? Locked;

        public event EventHandler? Unlocked;

        public event EventHandler? DisplaysOff;

        public event EventHandler? DisplaysOn;

        public void Dispose()
        {
            _displays.Dispose();
            _sessions.Dispose();
        }
    }

    // An icon per meter, with fixed IDs after the PC Companion icon's, so Windows keeps each in its place.
    private sealed class NotificationIcons(MessageWindow window) : IMeterIcons
    {
        private readonly Dictionary<Meter, TrayIcon> _icons = [];

        public event EventHandler? Selected;

        public event EventHandler<TrayMenuEventArgs>? MenuRequested;

        public event EventHandler<MeterEventArgs>? AddedAgain;

        public bool Show(Meter meter, uint[] pixels, int size, string name, string toolTip)
        {
            var image = IconImage.FromPixels(pixels, size);
            if (!_icons.TryGetValue(meter, out var icon))
            {
                try
                {
                    // Added with the meter's name, which screen readers keep as the icon's name.
                    icon = new TrayIcon(window, TrayIcon.ApplicationIconId + 1 + (uint)meter, image, name);
                }
                catch
                {
                    image.Dispose();
                    throw;
                }

                icon.Selected += (_, e) => Selected?.Invoke(this, e);
                icon.MenuRequested += (_, e) => MenuRequested?.Invoke(this, e);
                icon.AddedAgain += (_, _) => AddedAgain?.Invoke(this, new MeterEventArgs(meter));
                _icons[meter] = icon;
            }

            return icon.Update(image, toolTip);
        }

        public void Remove(Meter meter)
        {
            if (_icons.Remove(meter, out var icon))
            {
                icon.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var icon in _icons.Values)
            {
                icon.Dispose();
            }

            _icons.Clear();
        }
    }
}
