using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.AppWindows;

public sealed record ShortcutOption(AppWindowsShortcut Shortcut, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The App Windows page. Every change is saved right away.
/// </summary>
public sealed partial class AppWindowsViewModel : FeatureViewModel<AppWindowsSettings>
{
    private static readonly CompositeFormat AltFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutAlt);
    private static readonly CompositeFormat WindowsFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutWindows);
    private static readonly CompositeFormat AltShiftFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutAltShift);
    private static readonly CompositeFormat WindowsShiftFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutWindowsShift);
    private static readonly CompositeFormat RefusedFormat = CompositeFormat.Parse(Strings.AppWindowsShortcutRefused);

    private readonly AppSettings _appSettings;
    private readonly ShortcutStatus _shortcutStatus;
    private readonly string _key;
    private ShortcutOption? _selectedShortcut;

    public AppWindowsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, AppWindowsFeature.Definition)
    {
        _appSettings = appSettings;
        _shortcutStatus = new ShortcutStatus(AppWindowsFeature.Definition.Id, () => IsOn);
        _shortcutStatus.Changed += (_, _) => OnRefusalsChanged();

        var key = KeyAboveTab();
        _key = key;
        Shortcuts =
        [
            new(AppWindowsShortcut.AltAboveTab, string.Format(CultureInfo.CurrentCulture, AltFormat, key)),
            new(AppWindowsShortcut.WindowsAboveTab, string.Format(CultureInfo.CurrentCulture, WindowsFormat, key)),
        ];

        // An unknown shortcut in the file shows none chosen.
        _selectedShortcut = Shortcuts.FirstOrDefault(option => option.Shortcut == Settings.Shortcut);
    }

    /// <summary>What happens while an app running as administrator is in front, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.AppWindowsAdministratorInfoOn
        : Strings.AppWindowsAdministratorInfo;

    /// <summary>Whether Windows refused the shortcut that goes to the next window, as the running feature last said.</summary>
    public bool NextRefused => _shortcutStatus.Refused.Contains(AppWindowsFeature.NextShortcut);

    /// <summary>Whether Windows refused the shortcut with Shift, which goes back.</summary>
    public bool PreviousRefused => _shortcutStatus.Refused.Contains(AppWindowsFeature.PreviousShortcut);

    /// <summary>The note that names the refused shortcut that goes to the next window.</summary>
    public string NextRefusedInfo => string.Format(CultureInfo.CurrentCulture, RefusedFormat, KeysOf(back: false));

    /// <summary>The note that names the refused shortcut with Shift.</summary>
    public string PreviousRefusedInfo => string.Format(CultureInfo.CurrentCulture, RefusedFormat, KeysOf(back: true));

    public IReadOnlyList<ShortcutOption> Shortcuts { get; }

    public ShortcutOption? SelectedShortcut
    {
        get => _selectedShortcut;
        set
        {
            if (value is not null && SetProperty(ref _selectedShortcut, value))
            {
                Save(Settings with { Shortcut = value.Shortcut });
                OnRefusalsChanged();
            }
        }
    }

    public bool IncludeMinimized
    {
        get => Settings.IncludeMinimized;
        set => Change(Settings.IncludeMinimized, value, () => Settings with { IncludeMinimized = value });
    }

    /// <summary>Reads which shortcuts Windows refused from the host while the page shows. Call it on the page's thread.</summary>
    public void StartShortcutStatus() => _shortcutStatus.Start();

    public void StopShortcutStatus() => _shortcutStatus.Stop();

    private void OnRefusalsChanged()
    {
        OnPropertyChanged(nameof(NextRefused));
        OnPropertyChanged(nameof(PreviousRefused));
        OnPropertyChanged(nameof(NextRefusedInfo));
        OnPropertyChanged(nameof(PreviousRefusedInfo));
    }

    // The shortcut as the page shows it, such as Alt + `, with Shift for the one that goes back.
    private string KeysOf(bool back) => (Settings.Shortcut, back) switch
    {
        (AppWindowsShortcut.WindowsAboveTab, false) => string.Format(CultureInfo.CurrentCulture, WindowsFormat, _key),
        (AppWindowsShortcut.WindowsAboveTab, true) => string.Format(CultureInfo.CurrentCulture, WindowsShiftFormat, _key),
        (_, false) => string.Format(CultureInfo.CurrentCulture, AltFormat, _key),
        _ => string.Format(CultureInfo.CurrentCulture, AltShiftFormat, _key),
    };

    // The character the key types in the user's layout, such as ` or ^, or a description when it
    // types none that can be shown.
    private static string KeyAboveTab() =>
        KeyboardLayout.CharacterOf(ShortcutKeeper.KeyAboveTab, KeyboardLayout.OfCurrentThread()) is { } character
        && !char.IsControl(character)
        && !char.IsWhiteSpace(character)
            ? character.ToString()
            : Strings.AppWindowsKeyAboveTab;
}
