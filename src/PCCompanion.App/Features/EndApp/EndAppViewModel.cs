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
    private readonly ShortcutStatus _shortcutStatus;
    private EndAppShortcutOption? _selectedShortcut;

    public EndAppViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, EndAppFeature.Definition)
    {
        _appSettings = appSettings;
        _shortcutStatus = new ShortcutStatus(EndAppFeature.Definition.Id, () => IsOn);
        _shortcutStatus.Changed += (_, _) => OnPropertyChanged(nameof(ShortcutRefused));

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

    /// <summary>Whether Windows refused the shortcut, as the running feature last said.</summary>
    public bool ShortcutRefused => _shortcutStatus.Refused.Contains(EndAppFeature.ChooseAppShortcut);

    /// <summary>The note that names the shortcut chosen now.</summary>
    public string ShortcutRefusedInfo => ShortcutStatus.RefusedNote(Settings.Shortcut == EndAppShortcut.WindowsControlEscape
        ? Strings.EndAppShortcutWindowsControlEscape
        : Strings.EndAppShortcutControlAltEscape);

    public IReadOnlyList<EndAppShortcutOption> Shortcuts { get; }

    /// <summary>Reads which shortcuts Windows refused from the host while the page shows. Call it on the page's thread.</summary>
    public void StartShortcutStatus() => _shortcutStatus.Start();

    public void StopShortcutStatus() => _shortcutStatus.Stop();

    public EndAppShortcutOption? SelectedShortcut
    {
        get => _selectedShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedShortcut, value))
            {
                Save(Settings with { Shortcut = value.Shortcut });
                OnPropertyChanged(nameof(ShortcutRefusedInfo));
            }
        }
    }
}
