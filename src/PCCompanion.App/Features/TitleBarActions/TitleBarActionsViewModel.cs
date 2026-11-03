using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Modules.Windows.TitleBarActions;

namespace PCCompanion.App.Features.TitleBarActions;

public sealed record ClickActionOption(TitleBarClickAction Action, string DisplayName) : Choice(DisplayName);

public sealed record WheelActionOption(TitleBarWheelAction Action, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Title Bar Actions page. Every change is saved right away.
/// </summary>
public sealed class TitleBarActionsViewModel : FeatureViewModel<TitleBarActionsSettings>
{
    private readonly FeatureSettingsEditor _editor;
    private readonly AppSettings _appSettings;

    public TitleBarActionsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, TitleBarActionsFeature.Definition)
    {
        _editor = editor;
        _appSettings = appSettings;

        ClickActions =
        [
            new(TitleBarClickAction.None, Strings.TitleBarActionsNone),
            new(TitleBarClickAction.Minimize, Strings.TitleBarActionsMinimize),
            new(TitleBarClickAction.SendToBack, Strings.TitleBarActionsSendToBack),
            new(TitleBarClickAction.MinimizeToTray, Strings.TitleBarActionsMinimizeToTray),
            new(TitleBarClickAction.HideApp, Strings.TitleBarActionsHideApp),
        ];
        WheelActions =
        [
            new(TitleBarWheelAction.None, Strings.TitleBarActionsNone),
            new(TitleBarWheelAction.Opacity, Strings.TitleBarActionsOpacity),
        ];

        ExcludedApps = new AppListViewModel(
            () => Settings.ExcludedApps,
            app => SaveExcludedApps(Settings.WithExcludedApp(app)),
            index => SaveExcludedApps(Settings.WithoutExcludedApp(index)));
    }

    public IReadOnlyList<ClickActionOption> ClickActions { get; }

    public IReadOnlyList<WheelActionOption> WheelActions { get; }

    // An unknown action in the file shows none chosen.
    public ClickActionOption? SelectedMiddleClick
    {
        get => ClickOption(Settings.MiddleClick);
        set => ChangeClick(Settings.MiddleClick, value, action => Settings with { MiddleClick = action });
    }

    public ClickActionOption? SelectedShiftMiddleClick
    {
        get => ClickOption(Settings.ShiftMiddleClick);
        set => ChangeClick(Settings.ShiftMiddleClick, value, action => Settings with { ShiftMiddleClick = action });
    }

    public ClickActionOption? SelectedControlMiddleClick
    {
        get => ClickOption(Settings.ControlMiddleClick);
        set => ChangeClick(Settings.ControlMiddleClick, value, action => Settings with { ControlMiddleClick = action });
    }

    public WheelActionOption? SelectedWheel
    {
        get => WheelOption(Settings.Wheel);
        set
        {
            if (value is not null)
            {
                Change(Settings.Wheel, value.Action, () => Settings with { Wheel = value.Action });
            }
        }
    }

    public WheelActionOption? SelectedControlWheel
    {
        get => WheelOption(Settings.ControlWheel);
        set
        {
            if (value is not null)
            {
                Change(Settings.ControlWheel, value.Action, () => Settings with { ControlWheel = value.Action });
            }
        }
    }

    /// <summary>The apps that keep their title-bar clicks.</summary>
    public AppListViewModel ExcludedApps { get; }

    /// <summary>
    /// Whether a click is set to Minimize to tray or Hide app while that feature is off, as it was
    /// when the page opened.
    /// </summary>
    public bool NeedsFeatures =>
        (Chooses(TitleBarClickAction.MinimizeToTray) && !_editor.IsEnabled(MinimizeToTrayFeature.Definition.Id))
        || (Chooses(TitleBarClickAction.HideApp) && !_editor.IsEnabled(HideAppsFeature.Definition.Id));

    /// <summary>Which windows get no actions, with the host's rights now.</summary>
    public string LeftOutInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.TitleBarActionsLeftOutInfoOn
        : Strings.TitleBarActionsLeftOutInfo;

    private ClickActionOption? ClickOption(TitleBarClickAction action) => ClickActions.FirstOrDefault(option => option.Action == action);

    private WheelActionOption? WheelOption(TitleBarWheelAction action) => WheelActions.FirstOrDefault(option => option.Action == action);

    private bool Chooses(TitleBarClickAction action) =>
        Settings.MiddleClick == action || Settings.ShiftMiddleClick == action || Settings.ControlMiddleClick == action;

    private void ChangeClick(TitleBarClickAction current, ClickActionOption? value, Func<TitleBarClickAction, TitleBarActionsSettings> changed, [CallerMemberName] string? propertyName = null)
    {
        if (value is not null && Change(current, value.Action, () => changed(value.Action), propertyName))
        {
            OnPropertyChanged(nameof(NeedsFeatures));
        }
    }

    private bool SaveExcludedApps(TitleBarActionsSettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
    }
}
