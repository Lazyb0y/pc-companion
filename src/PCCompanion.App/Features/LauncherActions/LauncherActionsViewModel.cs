using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCompanion.App.Features.ShortcutsAndMouseButtons;
using PCCompanion.Core;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.LauncherActions;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using Windows.ApplicationModel.DataTransfer;

namespace PCCompanion.App.Features.LauncherActions;

/// <summary>A row of Actions: the action, its feature, and a button that copies its command.</summary>
public sealed record LauncherActionItem(string Name, string FeatureName, string CopyName, ICommand Copy);

/// <summary>A switch of Features in Start: whether a feature's actions are in Start, with the actions it has now.</summary>
public sealed partial class FeatureInStartItem(string featureId, string name, string actions, bool isOn, Action<string, bool> changed) : ObservableObject
{
    private bool _isOn = isOn;

    public string Name => name;

    public string Actions => actions;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
            {
                changed(featureId, value);
            }
        }
    }
}

/// <summary>
/// The Launcher Actions page. Every change is saved right away, and the settings app then updates
/// the entries in Start.
/// </summary>
public sealed partial class LauncherActionsViewModel : FeatureViewModel<LauncherActionsSettings>
{
    private static readonly CompositeFormat CopyNameFormat = CompositeFormat.Parse(Strings.LauncherActionsCopyCommandName);
    private static readonly CompositeFormat CopiedFormat = CompositeFormat.Parse(Strings.LauncherActionsCommandCopied);

    private string _copied = string.Empty;

    public LauncherActionsViewModel(FeatureSettingsEditor editor)
        : base(editor, LauncherActionsFeature.Definition)
    {
        ArgumentNullException.ThrowIfNull(editor);

        JsonElement? Section(string featureId) => editor.Read(featureId, section => section);
        var available = LauncherCatalog.Read(Section);
        var program = Program;
        Actions =
        [
            .. available.Select(action => new LauncherActionItem(
                action.Name,
                action.FeatureName,
                string.Format(CultureInfo.CurrentCulture, CopyNameFormat, action.Name),
                new RelayCommand(() => Copy(action, program)))),
        ];

        // Each feature that has actions for launchers now, on or off, and Launcher Actions' own.
        FeaturesInStart =
        [
            .. LauncherCatalog.Read(Section, withFeaturesOff: true)
                .GroupBy(action => action.Action.FeatureId)
                .Select(feature => new FeatureInStartItem(
                    feature.Key,
                    feature.First().FeatureName,
                    string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", feature.Select(action => action.Name)),
                    !Settings.HiddenFeatures.Contains(feature.Key, StringComparer.Ordinal),
                    SetInStart)),
        ];

        HasOnlyItsOwnActions = available.All(action => action.Action.FeatureId == LauncherActionsFeature.Definition.Id);
        OpenShortcutsCommand = new RelayCommand(() => ShortcutsAndMouseButtonsViewModel.ShowFeature(ShortcutsAndMouseButtonsFeature.Definition.Id));
    }

    public bool ShowInStart
    {
        get => Settings.ShowInStart;
        set => Change(Settings.ShowInStart, value, () => Settings with { ShowInStart = value });
    }

    /// <summary>The features whose actions can be left out of Start, read when the page opens.</summary>
    public IReadOnlyList<FeatureInStartItem> FeaturesInStart { get; }

    /// <summary>The actions available now, read when the page opens.</summary>
    public IReadOnlyList<LauncherActionItem> Actions { get; }

    /// <summary>Whether no other feature that's on offers actions, so the page says which can.</summary>
    public bool HasOnlyItsOwnActions { get; }

    /// <summary>Which command was copied last, which screen readers announce; empty until then.</summary>
    public string Copied
    {
        get => _copied;
        private set => SetProperty(ref _copied, value);
    }

    public ICommand OpenShortcutsCommand { get; }

    // The settings app itself, which runs the command.
    private static string Program => Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, SettingsAppProcess.FileName);

    private void SetInStart(string featureId, bool isOn)
    {
        var hidden = Settings.HiddenFeatures.Where(id => id != featureId);
        Save(Settings with { HiddenFeatures = isOn ? [.. hidden] : [.. hidden, featureId] });
    }

    private void Copy(LauncherAction action, string program)
    {
        var package = new DataPackage();
        package.SetText(LauncherActionList.Command(program, action.Action));
        Clipboard.SetContent(package);
        Copied = string.Format(CultureInfo.CurrentCulture, CopiedFormat, action.Name);
    }
}
