using System.Collections.Generic;
using System.Linq;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.WindowTabs;

namespace PCCompanion.App.Features.WindowTabs;

public sealed record TabSwitchOption(TabSwitchShortcuts Shortcuts, string DisplayName) : Choice(DisplayName);

public sealed record TabGroupOption(TabGroupShortcuts Shortcuts, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Window Tabs page. Every change is saved right away.
/// </summary>
public sealed class WindowTabsViewModel : FeatureViewModel<WindowTabsSettings>
{
    private readonly AppSettings _appSettings;
    private readonly ShortcutStatus _shortcutStatus;
    private TabSwitchOption? _selectedSwitch;
    private TabGroupOption? _selectedGroup;

    public WindowTabsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, WindowTabsFeature.Definition)
    {
        _appSettings = appSettings;
        _shortcutStatus = new ShortcutStatus(WindowTabsFeature.Definition.Id, () => IsOn);
        _shortcutStatus.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(ShortcutRefused));
            OnPropertyChanged(nameof(GroupShortcutRefused));
        };

        SwitchOptions =
        [
            new(TabSwitchShortcuts.WindowsControlPage, Strings.WindowTabsSwitchWindowsControlPage),
            new(TabSwitchShortcuts.WindowsAltPage, Strings.WindowTabsSwitchWindowsAltPage),
            new(TabSwitchShortcuts.None, Strings.WindowTabsSwitchNone),
        ];

        GroupOptions =
        [
            new(TabGroupShortcuts.WindowsControlT, Strings.WindowTabsGroupWindowsControlT),
            new(TabGroupShortcuts.None, Strings.WindowTabsSwitchNone),
        ];

        // An unknown choice in the file shows none chosen.
        _selectedSwitch = SwitchOptions.FirstOrDefault(option => option.Shortcuts == Settings.SwitchShortcuts);
        _selectedGroup = GroupOptions.FirstOrDefault(option => option.Shortcuts == Settings.GroupShortcuts);
    }

    /// <summary>Which windows can't be tabs, with the host's rights now.</summary>
    public string CantBeTabsInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.WindowTabsCantBeTabsInfoOn
        : Strings.WindowTabsCantBeTabsInfo;

    public bool GroupByDropping
    {
        get => Settings.GroupByDropping;
        set => Change(Settings.GroupByDropping, value, () => Settings with { GroupByDropping = value });
    }

    public IReadOnlyList<TabSwitchOption> SwitchOptions { get; }

    public TabSwitchOption? SelectedSwitch
    {
        get => _selectedSwitch;
        set
        {
            if (value is not null && SetProperty(ref _selectedSwitch, value))
            {
                Save(Settings with { SwitchShortcuts = value.Shortcuts });
                OnPropertyChanged(nameof(ShortcutRefusedInfo));
            }
        }
    }

    /// <summary>Whether Windows refused a switch shortcut, as the running feature last said.</summary>
    public bool ShortcutRefused => _shortcutStatus.Refused.Contains(WindowTabsFeature.SwitchShortcutsName);

    /// <summary>The note that names the shortcuts chosen now.</summary>
    public string ShortcutRefusedInfo => ShortcutStatus.RefusedNote(Settings.SwitchShortcuts == TabSwitchShortcuts.WindowsAltPage
        ? Strings.WindowTabsSwitchWindowsAltPage
        : Strings.WindowTabsSwitchWindowsControlPage);

    public IReadOnlyList<TabGroupOption> GroupOptions { get; }

    public TabGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (value is not null && SetProperty(ref _selectedGroup, value))
            {
                Save(Settings with { GroupShortcuts = value.Shortcuts });
            }
        }
    }

    /// <summary>Whether Windows refused a group shortcut, as the running feature last said.</summary>
    public bool GroupShortcutRefused => _shortcutStatus.Refused.Contains(WindowTabsFeature.GroupShortcutsName);

    /// <summary>The note that names the group shortcuts.</summary>
    public string GroupShortcutRefusedInfo { get; } = ShortcutStatus.RefusedNote(Strings.WindowTabsGroupWindowsControlT);

    /// <summary>Reads which shortcuts Windows refused from the host while the page shows. Call it on the page's thread.</summary>
    public void StartShortcutStatus() => _shortcutStatus.Start();

    public void StopShortcutStatus() => _shortcutStatus.Stop();
}
