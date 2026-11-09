using System.Collections.Generic;
using System.Linq;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.EndApp;

namespace PCCompanion.App.Features.EndApp;

public sealed record EndAppShortcutOption(EndAppShortcut Shortcut, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The End App page. Every change is saved right away.
/// </summary>
public sealed class EndAppViewModel : FeatureViewModel<EndAppSettings>
{
    private readonly AppSettings _appSettings;
    private EndAppShortcutOption? _selectedShortcut;

    public EndAppViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, EndAppFeature.Definition)
    {
        _appSettings = appSettings;

        Shortcuts =
        [
            new(EndAppShortcut.ControlAltEscape, Strings.EndAppShortcutControlAltEscape),
            new(EndAppShortcut.WindowsControlEscape, Strings.EndAppShortcutWindowsControlEscape),
            new(EndAppShortcut.None, Strings.EndAppShortcutNone),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedShortcut = Shortcuts.FirstOrDefault(option => option.Shortcut == Settings.Shortcut);
    }

    /// <summary>Which apps can't be ended, with the host's rights now.</summary>
    public string CantEndInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.EndAppCantEndInfoOn
        : Strings.EndAppCantEndInfo;

    public IReadOnlyList<EndAppShortcutOption> Shortcuts { get; }

    public EndAppShortcutOption? SelectedShortcut
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
}
