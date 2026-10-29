using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.MinimizeToTray;

public sealed record ShortcutOption(MinimizeToTrayShortcut Shortcut, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Minimize to Tray page. Every change is saved right away.
/// </summary>
public sealed partial class MinimizeToTrayViewModel : FeatureViewModel<MinimizeToTraySettings>
{
    private readonly AppSettings _appSettings;
    private readonly ILogger<MinimizeToTrayViewModel> _logger;
    private ShortcutOption? _selectedShortcut;

    public MinimizeToTrayViewModel(FeatureSettingsEditor editor, AppSettings appSettings, ILogger<MinimizeToTrayViewModel> logger)
        : base(editor, MinimizeToTrayFeature.Definition)
    {
        _appSettings = appSettings;
        _logger = logger;

        Shortcuts =
        [
            new(MinimizeToTrayShortcut.WindowsShiftZ, Strings.MinimizeToTrayShortcutWindowsShiftZ),
            new(MinimizeToTrayShortcut.None, Strings.MinimizeToTrayShortcutNone),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedShortcut = Shortcuts.FirstOrDefault(option => option.Shortcut == Settings.Shortcut);

        MinimizedApps = new AppListViewModel(
            () => Settings.MinimizedApps,
            app => SaveMinimizedApps(Settings.WithMinimizedApp(app)),
            index => SaveMinimizedApps(Settings.WithoutMinimizedApp(index)));
    }

    /// <summary>Which windows can't go to the tray, with the host's rights now.</summary>
    public string CantGoInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.MinimizeToTrayCantGoInfoOn
        : Strings.MinimizeToTrayCantGoInfo;

    public IReadOnlyList<ShortcutOption> Shortcuts { get; }

    public ShortcutOption? SelectedShortcut
    {
        get => _selectedShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedShortcut, value))
            {
                Save(Settings with { Shortcut = value.Shortcut });
            }
        }
    }

    public bool ShiftMinimize
    {
        get => Settings.ShiftMinimize;
        set => Change(Settings.ShiftMinimize, value, () => Settings with { ShiftMinimize = value });
    }

    /// <summary>The apps whose windows go to the tray whenever they're minimized.</summary>
    public AppListViewModel MinimizedApps { get; }

    private bool SaveMinimizedApps(MinimizeToTraySettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
    }

    [RelayCommand]
    private void OpenTaskbarSettings()
    {
        if (!ShellActions.OpenTaskbarSettings())
        {
            LogCantOpenTaskbarSettings(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open the taskbar settings.")]
    private static partial void LogCantOpenTaskbarSettings(ILogger logger);
}
