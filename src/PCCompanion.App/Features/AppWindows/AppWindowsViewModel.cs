using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.AppWindows;

public sealed record ShortcutOption(AppWindowsShortcut Shortcut, string DisplayName);

/// <summary>
/// The App Windows page. Every change is saved right away.
/// </summary>
public sealed partial class AppWindowsViewModel : ObservableObject
{
    private static readonly CompositeFormat AltFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutAlt);
    private static readonly CompositeFormat WindowsFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutWindows);

    private readonly FeatureSettingsEditor _editor;
    private readonly AppSettings _appSettings;
    private AppWindowsSettings _settings;
    private ShortcutOption? _selectedShortcut;
    private bool _saveFailed;

    public AppWindowsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
    {
        _editor = editor;
        _appSettings = appSettings;
        _settings = editor.Read(AppWindowsFeature.Definition.Id, AppWindowsSettings.Read);

        var key = KeyAboveTab();
        Shortcuts =
        [
            new(AppWindowsShortcut.AltAboveTab, string.Format(CultureInfo.CurrentCulture, AltFormat, key)),
            new(AppWindowsShortcut.WindowsAboveTab, string.Format(CultureInfo.CurrentCulture, WindowsFormat, key)),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedShortcut = Shortcuts.FirstOrDefault(option => option.Shortcut == _settings.Shortcut);
    }

    public static string Glyph => AppWindowsFeature.Definition.Glyph;

    /// <summary>What happens while an app running as administrator is in front.</summary>
    public string AdministratorInfo => _appSettings.General.AlwaysRunAsAdministrator
        ? Strings.AppWindowsAdministratorInfoOn
        : Strings.AppWindowsAdministratorInfo;

    public IReadOnlyList<ShortcutOption> Shortcuts { get; }

    public bool IsOn
    {
        get => _settings.Enabled;
        set
        {
            if (value != _settings.Enabled)
            {
                Save(_settings with { Enabled = value });
                OnPropertyChanged();
            }
        }
    }

    public ShortcutOption? SelectedShortcut
    {
        get => _selectedShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedShortcut, value))
            {
                Save(_settings with { Shortcut = value.Shortcut });
            }
        }
    }

    public bool IncludeMinimized
    {
        get => _settings.IncludeMinimized;
        set
        {
            if (value != _settings.IncludeMinimized)
            {
                Save(_settings with { IncludeMinimized = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }

    // The character the key types in the user's layout, such as ` or ^, or a description when it
    // types none that can be shown.
    private static string KeyAboveTab() =>
        KeyboardLayout.CharacterOf(ShortcutKeeper.KeyAboveTab, KeyboardLayout.OfCurrentThread()) is { } character
        && !char.IsControl(character)
        && !char.IsWhiteSpace(character)
            ? character.ToString()
            : Strings.AppWindowsKeyAboveTab;

    private void Save(AppWindowsSettings settings)
    {
        _settings = settings;
        SaveFailed = !_editor.Save(AppWindowsFeature.Definition.Id, settings.Write());
    }
}
