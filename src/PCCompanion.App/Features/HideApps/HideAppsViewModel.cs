using System.Collections.Generic;
using System.Linq;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.HideApps;

namespace PCCompanion.App.Features.HideApps;

public sealed record HideAppShortcutOption(HideAppShortcut Shortcut, string DisplayName) : Choice(DisplayName);

public sealed record HideOthersShortcutOption(HideOthersShortcut Shortcut, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Hide Apps page. Every change is saved right away.
/// </summary>
public sealed class HideAppsViewModel : FeatureViewModel<HideAppsSettings>
{
    private readonly AppSettings _appSettings;
    private HideAppShortcutOption? _selectedHideAppShortcut;
    private HideOthersShortcutOption? _selectedHideOthersShortcut;

    public HideAppsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, HideAppsFeature.Definition)
    {
        _appSettings = appSettings;

        HideAppShortcuts =
        [
            new(HideAppShortcut.WindowsControlH, Strings.HideAppsHideAppWindowsControlH),
            new(HideAppShortcut.None, Strings.HideAppsShortcutNone),
        ];
        HideOthersShortcuts =
        [
            new(HideOthersShortcut.WindowsControlShiftH, Strings.HideAppsHideOthersWindowsControlShiftH),
            new(HideOthersShortcut.None, Strings.HideAppsShortcutNone),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedHideAppShortcut = HideAppShortcuts.FirstOrDefault(option => option.Shortcut == Settings.HideAppShortcut);
        _selectedHideOthersShortcut = HideOthersShortcuts.FirstOrDefault(option => option.Shortcut == Settings.HideOthersShortcut);
    }

    /// <summary>Which windows stay, with the host's rights now.</summary>
    public string StayInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.HideAppsStayInfoOn
        : Strings.HideAppsStayInfo;

    public IReadOnlyList<HideAppShortcutOption> HideAppShortcuts { get; }

    public IReadOnlyList<HideOthersShortcutOption> HideOthersShortcuts { get; }

    public HideAppShortcutOption? SelectedHideAppShortcut
    {
        get => _selectedHideAppShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedHideAppShortcut, value))
            {
                Save(Settings with { HideAppShortcut = value.Shortcut });
            }
        }
    }

    public HideOthersShortcutOption? SelectedHideOthersShortcut
    {
        get => _selectedHideOthersShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedHideOthersShortcut, value))
            {
                Save(Settings with { HideOthersShortcut = value.Shortcut });
            }
        }
    }

    public bool FrontDisplayOnly
    {
        get => Settings.FrontDisplayOnly;
        set => Change(Settings.FrontDisplayOnly, value, () => Settings with { FrontDisplayOnly = value });
    }

    public bool BringBackWholeApp
    {
        get => Settings.BringBackWholeApp;
        set => Change(Settings.BringBackWholeApp, value, () => Settings with { BringBackWholeApp = value });
    }
}
