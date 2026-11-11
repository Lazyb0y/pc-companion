using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>
/// A row of the list of shortcuts and mouse buttons: its keys or button, its action and, when it
/// does nothing, why, with a button to the page that can change that.
/// </summary>
/// <param name="Index">Its place in the settings' list.</param>
public sealed record TriggerItem(int Index, string Input, string Action, string Glyph, string? Warning, string? WarningButton, ICommand? WarningCommand, string EditName, string MoreName)
{
    public bool HasWarning => Warning is not null;
}

/// <summary>A row of Shortcuts on other pages, with a button that opens the feature's page.</summary>
public sealed record OtherShortcutItem(string Input, string Action, string OpenText, ICommand Open);

/// <summary>
/// The Shortcuts & Mouse Buttons page. Every change is saved right away.
/// </summary>
public sealed class ShortcutsAndMouseButtonsViewModel : FeatureViewModel<ShortcutsAndMouseButtonsSettings>
{
    private const string KeyboardGlyph = "";
    private const string MouseGlyph = "";

    private static readonly CompositeFormat EditFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsMoreName);
    private static readonly CompositeFormat ActionNameFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsActionName);
    private static readonly CompositeFormat NotSupportedFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsNotSupported);
    private static readonly CompositeFormat NotOfferedFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsNotOffered);
    private static readonly CompositeFormat TargetMissingFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsTargetMissing);
    private static readonly CompositeFormat FeatureOffFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsFeatureOff);
    private static readonly CompositeFormat TakenFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsTaken);
    private static readonly CompositeFormat AltGrFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsAltGr);
    private static readonly CompositeFormat AltGrOtherLayoutFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsAltGrOtherLayout);
    private static readonly CompositeFormat OpenFeatureFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsOpenFeature);

    private readonly FeatureSettingsEditor _editor;
    private readonly AppSettings _appSettings;
    private readonly ShortcutStatus _shortcutStatus;
    private readonly nint _layout = KeyboardLayout.OfCurrentThread();
    private readonly ActionCatalog _catalog;

    public ShortcutsAndMouseButtonsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, ShortcutsAndMouseButtonsFeature.Definition)
    {
        ArgumentNullException.ThrowIfNull(appSettings);

        _editor = editor;
        _appSettings = appSettings;
        _catalog = ActionCatalog.Read(editor, _layout);
        _shortcutStatus = new ShortcutStatus(ShortcutsAndMouseButtonsFeature.Definition.Id, () => IsOn);
        _shortcutStatus.Changed += (_, _) => RefreshTriggers();

        OtherShortcuts =
        [
            .. _catalog.OtherShortcuts.Select(other => new OtherShortcutItem(
                TriggerText.Of(other.Modifiers, other.Key, _layout),
                other.Action.FullName,
                Format(OpenFeatureFormat, other.Action.FeatureName),
                new RelayCommand(() => ShowFeature(other.Action.Feature.Info.Id)))),
        ];
        RefreshTriggers();
    }

    public ObservableCollection<TriggerItem> Triggers { get; } = [];

    public bool HasNoTriggers => Triggers.Count == 0;

    public bool PauseMouseButtonsInFullScreen
    {
        get => Settings.PauseMouseButtonsInFullScreen;
        set => Change(Settings.PauseMouseButtonsInFullScreen, value, () => Settings with { PauseMouseButtonsInFullScreen = value });
    }

    /// <summary>The shortcuts of the other features that are on, read when the page opens.</summary>
    public IReadOnlyList<OtherShortcutItem> OtherShortcuts { get; }

    public bool HasNoOtherShortcuts => OtherShortcuts.Count == 0;

    /// <summary>What works while an app running as administrator is in front, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.ShortcutsAndMouseButtonsAdministratorInfoOn
        : Strings.ShortcutsAndMouseButtonsAdministratorInfo;

    /// <summary>Reads which shortcuts Windows refused from the host while the page shows. Call it on the page's thread.</summary>
    public void StartShortcutStatus() => _shortcutStatus.Start();

    public void StopShortcutStatus() => _shortcutStatus.Stop();

    /// <summary>Opens the page of the feature with the ID <paramref name="featureId"/>.</summary>
    public static void ShowFeature(string featureId) => App.Current.MainWindow?.ShowFeature(featureId);

    /// <summary>The editor of the trigger at <paramref name="index"/>, or of a new one when it's <see langword="null"/>.</summary>
    public TriggerEditor CreateEditor(int? index)
    {
        var trigger = index is { } place && place >= 0 && place < Settings.Triggers.Count ? Settings.Triggers[place] : null;
        return new TriggerEditor(trigger, trigger is null ? null : index, Settings.Triggers, _catalog, _editor, _layout);
    }

    /// <summary>Adds <paramref name="trigger"/> at the end of the list and saves.</summary>
    public void Add(Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        Save(Settings with { Triggers = [.. Settings.Triggers, trigger] });
        RefreshTriggers();
    }

    /// <summary>Puts <paramref name="trigger"/> in place of the one at <paramref name="index"/> and saves.</summary>
    public void Replace(int index, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (index < 0 || index >= Settings.Triggers.Count)
        {
            return;
        }

        Save(Settings with { Triggers = [.. Settings.Triggers.Select((other, place) => place == index ? trigger : other)] });
        RefreshTriggers();
    }

    /// <summary>Deletes the trigger at <paramref name="index"/> and saves.</summary>
    public void Delete(int index)
    {
        if (index < 0 || index >= Settings.Triggers.Count)
        {
            return;
        }

        Save(Settings with { Triggers = [.. Settings.Triggers.Where((_, place) => place != index)] });
        RefreshTriggers();
    }

    private void RefreshTriggers()
    {
        var layouts = KeyboardLayout.Installed();
        Triggers.Clear();
        foreach (var checkedTrigger in TriggerCheck.CheckAll(Settings.Triggers))
        {
            Triggers.Add(ItemOf(checkedTrigger, layouts));
        }

        OnPropertyChanged(nameof(HasNoTriggers));
    }

    private TriggerItem ItemOf(CheckedTrigger checkedTrigger, IReadOnlyList<nint> layouts)
    {
        var trigger = checkedTrigger.Trigger;
        var input = TriggerText.Of(trigger, _layout) ?? Strings.ShortcutsAndMouseButtonsUnknownInput;
        var action = trigger.Action.ToFeatureAction();
        var feature = FeatureCatalog.Find(action.FeatureId);
        var named = _catalog.Find(action);
        var actionName = named?.FullName
            ?? (feature is null
                ? Strings.ShortcutsAndMouseButtonsUnknownActionName
                : Format(ActionNameFormat, Localized.GetString(feature.Info.NameKey), action.Target ?? Strings.ShortcutsAndMouseButtonsUnavailableActionName));

        var (warning, page) = WarningOf(checkedTrigger, input, feature, named, layouts);
        var pageFeature = page is null ? null : FeatureCatalog.Find(page);
        return new TriggerItem(
            checkedTrigger.Index,
            input,
            actionName,
            trigger.MouseButton is not null && trigger.Shortcut is null ? MouseGlyph : KeyboardGlyph,
            warning,
            pageFeature is null ? null : Format(OpenFeatureFormat, Localized.GetString(pageFeature.Info.NameKey)),
            pageFeature is null ? null : new RelayCommand(() => ShowFeature(pageFeature.Info.Id)),
            Format(EditFormat, input),
            Format(MoreFormat, input));
    }

    // Why the trigger does nothing, if it does nothing, and the ID of the feature whose page can change that.
    private (string? Warning, string? Page) WarningOf(CheckedTrigger checkedTrigger, string input, CatalogFeature? feature, CatalogAction? named, IReadOnlyList<nint> layouts)
    {
        if (checkedTrigger.Problem != TriggerProblem.None)
        {
            return (ProblemText(checkedTrigger.Problem), null);
        }

        if (feature is null)
        {
            return (Strings.ShortcutsAndMouseButtonsNoSuchAction, null);
        }

        var featureName = Localized.GetString(feature.Info.NameKey);
        if (!feature.Support.IsSupported)
        {
            return (Format(NotSupportedFormat, featureName), null);
        }

        if (named is null)
        {
            return checkedTrigger.Trigger.Action.Target is { } target
                ? (Format(TargetMissingFormat, featureName, target), feature.Info.Id)
                : (Format(NotOfferedFormat, featureName), feature.Info.Id);
        }

        // This feature's own actions run while it's on, which its switch shows.
        if (feature.Info.Id != ShortcutsAndMouseButtonsFeature.Definition.Id && !_editor.IsEnabled(feature.Info.Id))
        {
            return (Format(FeatureOffFormat, featureName), feature.Info.Id);
        }

        var used = TriggerCheck.InputOf(checkedTrigger.Trigger)!.Value;
        if (_shortcutStatus.Refused.Contains(used.Name))
        {
            // Windows doesn't say who has it, but it may be another feature's shortcut.
            return _catalog.OtherShortcuts.FirstOrDefault(other => other.Input == used) is { } other
                ? (Format(TakenFormat, other.Action.FeatureName), other.Action.Feature.Info.Id)
                : (ShortcutStatus.RefusedNote(input), null);
        }

        if (used.Key is { } key && ShortcutRules.AltGrConflict(used.Modifiers, key, layouts, KeyboardLayout.TypedWithAltGr) is { } typed)
        {
            return (KeyboardLanguage.NameOf(typed.Layout) is { } language ? Format(AltGrFormat, language, input, typed.Text) : Format(AltGrOtherLayoutFormat, input, typed.Text), null);
        }

        return (null, null);
    }

    private static string ProblemText(TriggerProblem problem) => problem switch
    {
        TriggerProblem.NoAction => Strings.ShortcutsAndMouseButtonsProblemNoAction,
        TriggerProblem.NoInput => Strings.ShortcutsAndMouseButtonsProblemNoInput,
        TriggerProblem.BothInputs => Strings.ShortcutsAndMouseButtonsProblemBothInputs,
        TriggerProblem.UnknownModifier => Strings.ShortcutsAndMouseButtonsProblemUnknownModifier,
        TriggerProblem.UnknownKey => Strings.ShortcutsAndMouseButtonsProblemUnknownKey,
        TriggerProblem.NeedsModifier => Strings.ShortcutsAndMouseButtonsProblemNeedsModifier,
        TriggerProblem.Reserved => Strings.ShortcutsAndMouseButtonsProblemReserved,
        TriggerProblem.UnknownButton => Strings.ShortcutsAndMouseButtonsProblemUnknownButton,
        TriggerProblem.MiddleNeedsWindowsKey => Strings.ShortcutsAndMouseButtonsProblemMiddleNeedsWindowsKey,
        _ => Strings.ShortcutsAndMouseButtonsProblemSameAsEarlier,
    };

    private static string Format(CompositeFormat format, params object?[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
