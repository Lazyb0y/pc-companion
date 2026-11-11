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
    private readonly ShortcutStatus _shortcutStatus;
    private HideAppShortcutOption? _selectedHideAppShortcut;
    private HideOthersShortcutOption? _selectedHideOthersShortcut;

    public HideAppsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, HideAppsFeature.Definition)
    {
        _appSettings = appSettings;
        _shortcutStatus = new ShortcutStatus(HideAppsFeature.Definition.Id, () => IsOn);
        _shortcutStatus.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HideAppRefused));
            OnPropertyChanged(nameof(HideOthersRefused));
        };

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

    /// <summary>Whether Windows refused Hide app's shortcut, as the running feature last said.</summary>
    public bool HideAppRefused => _shortcutStatus.Refused.Contains(HideAppsFeature.HideAppShortcut);

    /// <summary>Whether Windows refused Hide others' shortcut.</summary>
    public bool HideOthersRefused => _shortcutStatus.Refused.Contains(HideAppsFeature.HideOthersShortcut);

    public string HideAppRefusedInfo { get; } = ShortcutStatus.RefusedNote(Strings.HideAppsHideAppWindowsControlH);

    public string HideOthersRefusedInfo { get; } = ShortcutStatus.RefusedNote(Strings.HideAppsHideOthersWindowsControlShiftH);

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

    /// <summary>Reads which shortcuts Windows refused from the host while the page shows. Call it on the page's thread.</summary>
    public void StartShortcutStatus() => _shortcutStatus.Start();

    public void StopShortcutStatus() => _shortcutStatus.Stop();

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
