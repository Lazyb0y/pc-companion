using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.AppWindows;

public sealed record ShortcutOption(AppWindowsShortcut Shortcut, string DisplayName);

/// <summary>
/// The App Windows page. Every change is saved right away.
/// </summary>
public sealed partial class AppWindowsViewModel : FeatureViewModel<AppWindowsSettings>
{
    private static readonly CompositeFormat AltFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutAlt);
    private static readonly CompositeFormat WindowsFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutWindows);

    private readonly AppSettings _appSettings;
    private ShortcutOption? _selectedShortcut;

    public AppWindowsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, AppWindowsFeature.Definition)
    {
        _appSettings = appSettings;

        var key = KeyAboveTab();
        Shortcuts =
        [
            new(AppWindowsShortcut.AltAboveTab, string.Format(CultureInfo.CurrentCulture, AltFormat, key)),
            new(AppWindowsShortcut.WindowsAboveTab, string.Format(CultureInfo.CurrentCulture, WindowsFormat, key)),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedShortcut = Shortcuts.FirstOrDefault(option => option.Shortcut == Settings.Shortcut);
    }

    /// <summary>What happens while an app running as administrator is in front.</summary>
    public string AdministratorInfo => _appSettings.General.AlwaysRunAsAdministrator
        ? Strings.AppWindowsAdministratorInfoOn
        : Strings.AppWindowsAdministratorInfo;

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

    public bool IncludeMinimized
    {
        get => Settings.IncludeMinimized;
        set => Change(Settings.IncludeMinimized, value, () => Settings with { IncludeMinimized = value });
    }

    // The character the key types in the user's layout, such as ` or ^, or a description when it
    // types none that can be shown.
    private static string KeyAboveTab() =>
        KeyboardLayout.CharacterOf(ShortcutKeeper.KeyAboveTab, KeyboardLayout.OfCurrentThread()) is { } character
        && !char.IsControl(character)
        && !char.IsWhiteSpace(character)
            ? character.ToString()
            : Strings.AppWindowsKeyAboveTab;
}
