using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Modules.Windows.WindowShading;

namespace PCCompanion.App.Features.WindowShading;

/// <summary>A delay in the hover lists, in milliseconds, with its name in the user's language.</summary>
public sealed record ShadingDelayOption(int Milliseconds, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Window Shading page. Every change is saved right away.
/// </summary>
public sealed class WindowShadingViewModel : FeatureViewModel<WindowShadingSettings>
{
    private static readonly CompositeFormat FractionFormat = CompositeFormat.Parse(Strings.WindowShadingSecondsFraction);

    private readonly AppSettings _appSettings;

    public WindowShadingViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, WindowShadingFeature.Definition)
    {
        _appSettings = appSettings;
        RollDownDelays = [.. ShadingHover.RollDownChoices.Select(Option)];
        RollUpDelays = [.. ShadingHover.RollUpChoices.Select(Option)];
        OpenTitleBarActionsCommand = new RelayCommand(() => App.Current.MainWindow?.ShowFeature(TitleBarActionsFeature.Definition.Id));
        OpenShortcutsCommand = new RelayCommand(() => App.Current.MainWindow?.ShowFeature(ShortcutsAndMouseButtonsFeature.Definition.Id));
    }

    public ICommand OpenTitleBarActionsCommand { get; }

    public ICommand OpenShortcutsCommand { get; }

    public bool KeepOnTop
    {
        get => Settings.KeepOnTop;
        set => Change(Settings.KeepOnTop, value, () => Settings with { KeepOnTop = value });
    }

    public bool Hover
    {
        get => Settings.Hover.Enabled;
        set => Change(Settings.Hover.Enabled, value, () => Settings with { Hover = Settings.Hover with { Enabled = value } });
    }

    public IReadOnlyList<ShadingDelayOption> RollDownDelays { get; }

    public IReadOnlyList<ShadingDelayOption> RollUpDelays { get; }

    // A delay in the file that isn't offered shows the one the feature uses instead.
    public ShadingDelayOption? SelectedRollDownDelay
    {
        get => RollDownDelays.FirstOrDefault(option => option.Milliseconds == (int)Settings.Hover.RollDownAfter.TotalMilliseconds);
        set
        {
            if (value is not null)
            {
                Change(Settings.Hover.RollDownAfterMs, value.Milliseconds, () => Settings with { Hover = Settings.Hover with { RollDownAfterMs = value.Milliseconds } });
            }
        }
    }

    public ShadingDelayOption? SelectedRollUpDelay
    {
        get => RollUpDelays.FirstOrDefault(option => option.Milliseconds == (int)Settings.Hover.RollUpAfter.TotalMilliseconds);
        set
        {
            if (value is not null)
            {
                Change(Settings.Hover.RollUpAfterMs, value.Milliseconds, () => Settings with { Hover = Settings.Hover with { RollUpAfterMs = value.Milliseconds } });
            }
        }
    }

    /// <summary>Which apps' windows can be rolled up, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.WindowShadingAdministratorInfoOn
        : Strings.WindowShadingAdministratorInfo;

    // Whole seconds take the language's plural forms; a fraction of a second is always plural.
    private static ShadingDelayOption Option(int milliseconds) =>
        new(milliseconds, milliseconds % 1000 == 0
            ? PluralStrings.Format("WindowShadingSeconds", milliseconds / 1000)
            : ReadingOrder.Format(FractionFormat, milliseconds / 1000.0));
}
