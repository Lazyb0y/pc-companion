using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>An action in the editor's list, such as "Hide Apps: Hide others".</summary>
public sealed record ActionChoice(CatalogAction Action) : Choice(Action.FullName);

/// <summary>A key in Choose keys' list, by its name as the user's layout writes it.</summary>
public sealed record KeyChoice(string Key, string DisplayName) : Choice(DisplayName);

/// <summary>A mouse button in the editor's list, such as "Back button".</summary>
public sealed record ButtonChoice(TriggerButton Button, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The editor of a shortcut or mouse button: its action; its keys, recorded or chosen with check
/// boxes, or its button and the keys held with it; what's wrong with them; and the apps it skips.
/// Save is available when it has an action and keys or a button that can be used. See "The editor"
/// in docs/features/input/shortcuts-and-mouse-buttons.md.
/// </summary>
public sealed partial class TriggerEditor : ObservableObject
{
    private static readonly CompositeFormat FeatureOffFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorFeatureOff);
    private static readonly CompositeFormat ShortcutNameFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorShortcutName);
    private static readonly CompositeFormat SameAsFeatureFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorSameAsFeature);
    private static readonly CompositeFormat SameAsTriggerFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorSameAsTrigger);
    private static readonly CompositeFormat AltGrFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorAltGr);
    private static readonly CompositeFormat AltGrOtherLayoutFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorAltGrOtherLayout);
    private static readonly CompositeFormat SameButtonFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsEditorSameButton);

    // Named keys in Choose keys' list, in this order after the letters, digits and function keys.
    private static readonly string[] NamedKeys =
        ["Escape", "Tab", "Space", "Enter", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "PrintScreen", "Pause", "ContextMenu"];

    private readonly TriggerInput? _originalInput;
    private readonly IReadOnlyList<KnownShortcut> _features;
    private readonly IReadOnlyList<KnownShortcut> _triggers;
    private readonly FeatureSettingsEditor _settings;
    private readonly nint _layout;
    private readonly List<ListedApp> _excluded;
    private ActionChoice? _selectedAction;
    private RecordedShortcut? _checked;
    private ShortcutCheckResult? _check;
    private bool _following;
    private bool _control;
    private bool _alt;
    private bool _shift;
    private bool _windows;
    private KeyChoice? _selectedKey;
    private bool _isMouseButton;
    private ButtonChoice? _selectedButton;
    private bool _buttonControl;
    private bool _buttonAlt;
    private bool _buttonShift;
    private bool _buttonWindows;

    /// <param name="trigger">The trigger to edit, or <see langword="null"/> for a new one.</param>
    /// <param name="index">The trigger's place in <paramref name="triggers"/>, or <see langword="null"/> for a new one.</param>
    /// <param name="triggers">The page's triggers, whose shortcuts the new one can't have.</param>
    /// <param name="layout">The keyboard layout the keys are shown and tested in.</param>
    public TriggerEditor(Trigger? trigger, int? index, IReadOnlyList<Trigger> triggers, ActionCatalog catalog, FeatureSettingsEditor settings, nint layout)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);

        IsNew = trigger is null;
        _settings = settings;
        _layout = layout;
        _originalInput = trigger is null ? null : TriggerCheck.InputOf(trigger);
        Actions = [.. catalog.Actions.Select(action => new ActionChoice(action))];
        _selectedAction = trigger is null ? null : Actions.FirstOrDefault(choice => choice.Action.Action == trigger.Action.ToFeatureAction());
        _features = [.. catalog.OtherShortcuts.Select(other => new KnownShortcut(other.Input, other.Action.FeatureName))];
        _triggers =
        [
            .. triggers
                .Select((other, place) => (Trigger: other, Place: place))
                .Where(other => other.Place != index && TriggerCheck.InputOf(other.Trigger) is not null)
                .Select(other => new KnownShortcut(
                    TriggerCheck.InputOf(other.Trigger)!.Value,
                    catalog.Find(other.Trigger.Action.ToFeatureAction())?.Name ?? Strings.ShortcutsAndMouseButtonsUnknownActionName)),
        ];
        Keys = [.. KeyCodes.All.OrderBy(Rank).ThenBy(Order).Select(key => new KeyChoice(key, TriggerText.KeyName(key, layout)))];

        _excluded = [.. trigger?.ExcludedApps ?? []];
        ExcludedApps = new AppListViewModel(() => _excluded, AddExcluded, RemoveExcluded);

        Buttons = [.. Enum.GetValues<TriggerButton>().Select(button => new ButtonChoice(button, TriggerText.ButtonName(button)))];

        Recorder.Changed += (_, _) => OnRecorderChanged();
        if (trigger?.Shortcut is { } shortcut && TriggerModifiers.ModifiersOf(shortcut.Modifiers) is { } modifiers)
        {
            Recorder.Set(new RecordedShortcut(modifiers, KeyCodes.ScanCodeOf(shortcut.Key) is null ? null : shortcut.Key, Taken: false));
        }
        else if (trigger?.MouseButton is { } mouseButton && TriggerModifiers.ModifiersOf(mouseButton.Modifiers) is { } held)
        {
            _isMouseButton = true;
            _selectedButton = Buttons.FirstOrDefault(choice => choice.Button == mouseButton.Button);
            _buttonControl = held.HasFlag(HotKeyModifiers.Control);
            _buttonAlt = held.HasFlag(HotKeyModifiers.Alt);
            _buttonShift = held.HasFlag(HotKeyModifiers.Shift);
            _buttonWindows = held.HasFlag(HotKeyModifiers.Windows) || mouseButton.Button == TriggerButton.Middle;
        }
    }

    /// <summary>Whether the trigger is a mouse button rather than a shortcut.</summary>
    public bool IsMouseButton
    {
        get => _isMouseButton;
        set
        {
            if (SetProperty(ref _isMouseButton, value))
            {
                OnPropertyChanged(nameof(IsShortcut));
                OnPropertyChanged(nameof(TriggerKindIndex));
                OnInputChanged();
            }
        }
    }

    public bool IsShortcut => !IsMouseButton;

    /// <summary>The names of the trigger's kinds, in <see cref="TriggerKindIndex"/>'s order.</summary>
    public IReadOnlyList<string> Kinds { get; } = [Strings.ShortcutsAndMouseButtonsEditorShortcutHeader, Strings.ShortcutsAndMouseButtonsEditorKindMouseButton];

    /// <summary>The trigger's kind for the editor's choice: 0 for a shortcut, 1 for a mouse button.</summary>
    public int TriggerKindIndex
    {
        get => IsMouseButton ? 1 : 0;
        set => IsMouseButton = value == 1;
    }

    /// <summary>Back, Forward and the middle button.</summary>
    public IReadOnlyList<ButtonChoice> Buttons { get; }

    public ButtonChoice? SelectedButton
    {
        get => _selectedButton;
        set
        {
            if (SetProperty(ref _selectedButton, value))
            {
                // The middle button belongs to the apps without the Windows logo key.
                if (value?.Button == TriggerButton.Middle)
                {
                    ButtonWindows = true;
                }

                OnPropertyChanged(nameof(CanUncheckButtonWindows));
                OnInputChanged();
            }
        }
    }

    public bool ButtonControl
    {
        get => _buttonControl;
        set => ChooseButtonKey(ref _buttonControl, value);
    }

    public bool ButtonAlt
    {
        get => _buttonAlt;
        set => ChooseButtonKey(ref _buttonAlt, value);
    }

    public bool ButtonShift
    {
        get => _buttonShift;
        set => ChooseButtonKey(ref _buttonShift, value);
    }

    public bool ButtonWindows
    {
        get => _buttonWindows;
        set => ChooseButtonKey(ref _buttonWindows, value || SelectedButton?.Button == TriggerButton.Middle);
    }

    /// <summary>Whether the Windows logo key can be left out: not for the middle button.</summary>
    public bool CanUncheckButtonWindows => SelectedButton?.Button != TriggerButton.Middle;

    /// <summary>Why the mouse button can't be saved, or an empty string.</summary>
    public string ButtonProblem =>
        ButtonInput is { } input && _triggers.FirstOrDefault(known => known.Input == input) is { } other
            ? string.Format(CultureInfo.CurrentCulture, SameButtonFormat, other.Name)
            : string.Empty;

    public bool HasButtonProblem => ButtonProblem.Length > 0;

    /// <summary>Whether Back or Forward is chosen without keys, which apps such as browsers then lose.</summary>
    public bool IsBackOrForwardAlone => SelectedButton?.Button is TriggerButton.Back or TriggerButton.Forward && ButtonModifiers == HotKeyModifiers.None;

    /// <summary>What the apps in Not in these apps get: the keys, or the click.</summary>
    public string ExcludedDescription => IsMouseButton
        ? Strings.ShortcutsAndMouseButtonsEditorExcludedMouseDescription
        : Strings.ShortcutsAndMouseButtonsEditorExcludedDescription;

    private HotKeyModifiers ButtonModifiers =>
        (ButtonControl ? HotKeyModifiers.Control : HotKeyModifiers.None)
        | (ButtonAlt ? HotKeyModifiers.Alt : HotKeyModifiers.None)
        | (ButtonShift ? HotKeyModifiers.Shift : HotKeyModifiers.None)
        | (ButtonWindows ? HotKeyModifiers.Windows : HotKeyModifiers.None);

    private TriggerInput? ButtonInput => SelectedButton is { } choice ? new TriggerInput(ButtonModifiers, null, choice.Button) : null;

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.ShortcutsAndMouseButtonsAddTitle : Strings.ShortcutsAndMouseButtonsEditTitle;

    /// <summary>The actions of the features supported on this PC, by feature.</summary>
    public IReadOnlyList<ActionChoice> Actions { get; }

    public ActionChoice? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
            {
                OnPropertyChanged(nameof(ActionNote));
                OnPropertyChanged(nameof(HasActionNote));
                OnPropertyChanged(nameof(ShortcutBoxName));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>Why the chosen action does nothing yet, such as its feature being off, or an empty string.</summary>
    public string ActionNote =>
        SelectedAction?.Action is { } action
        && action.Feature.Info.Id != ShortcutsAndMouseButtonsFeature.Definition.Id
        && !_settings.IsEnabled(action.Feature.Info.Id)
            ? string.Format(CultureInfo.CurrentCulture, FeatureOffFormat, action.FeatureName)
            : string.Empty;

    public bool HasActionNote => ActionNote.Length > 0;

    /// <summary>The box's name for screen readers, such as "Shortcut for Hide others".</summary>
    public string ShortcutBoxName => SelectedAction is { } choice
        ? string.Format(CultureInfo.CurrentCulture, ShortcutNameFormat, choice.Action.Name)
        : Strings.ShortcutsAndMouseButtonsEditorShortcutHeader;

    /// <summary>Records the keys pressed in the box.</summary>
    public ShortcutRecorder Recorder { get; } = new();

    /// <summary>What the box shows: the keys held, the shortcut, or that it waits for keys.</summary>
    public string ShortcutText
    {
        get
        {
            if (Recorder.IsHolding)
            {
                return string.Join(" + ", [.. TriggerText.ModifierNames(Recorder.Held), "…"]);
            }

            return Recorder.Recorded switch
            {
                { Key: { } key } recorded => TriggerText.Of(recorded.Modifiers, key, _layout),
                { } recorded => string.Join(" + ", [.. TriggerText.ModifierNames(recorded.Modifiers), "…"]),
                null => Strings.ShortcutsAndMouseButtonsEditorPressKeys,
            };
        }
    }

    /// <summary>Why the shortcut can't be saved, or an empty string.</summary>
    public string Problem => _check is { } check ? ProblemText(check) : string.Empty;

    public bool HasProblem => Problem.Length > 0;

    /// <summary>A warning that allows saving: that Windows or apps use the shortcut. An empty string otherwise.</summary>
    public string Warning => _check is { CanSave: true, UsedByWindows: true } ? Strings.ShortcutsAndMouseButtonsEditorUsedByWindows : string.Empty;

    public bool HasWarning => Warning.Length > 0;

    public bool CanSave => SelectedAction is not null
        && (IsMouseButton
            ? SelectedButton is not null && !HasButtonProblem
            : Recorder.Recorded is { Key: not null } && _check is { CanSave: true });

    public bool ChooseControl
    {
        get => _control;
        set => Choose(ref _control, value);
    }

    public bool ChooseAlt
    {
        get => _alt;
        set => Choose(ref _alt, value);
    }

    public bool ChooseShift
    {
        get => _shift;
        set => Choose(ref _shift, value);
    }

    public bool ChooseWindows
    {
        get => _windows;
        set => Choose(ref _windows, value);
    }

    /// <summary>Every key a shortcut can use: letters, digits, function keys, named keys and punctuation.</summary>
    public IReadOnlyList<KeyChoice> Keys { get; }

    public KeyChoice? SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (SetProperty(ref _selectedKey, value) && !_following)
            {
                UseChosenKeys();
            }
        }
    }

    /// <summary>The apps the shortcut skips, which get the keys themselves.</summary>
    public AppListViewModel ExcludedApps { get; }

    /// <summary>The trigger as edited. Call it only when <see cref="CanSave"/>.</summary>
    public Trigger ToTrigger()
    {
        if (SelectedAction is not { } action)
        {
            throw new InvalidOperationException("The trigger has no action.");
        }

        if (IsMouseButton)
        {
            return SelectedButton is { } choice
                ? new Trigger { Action = TriggerAction.For(action.Action.Action), MouseButton = TriggerMouseButton.For(choice.Button, ButtonModifiers), ExcludedApps = [.. _excluded] }
                : throw new InvalidOperationException("The trigger has no button.");
        }

        return Recorder.Recorded is { Key: { } key } recorded
            ? new Trigger { Action = TriggerAction.For(action.Action.Action), Shortcut = TriggerShortcut.For(recorded.Modifiers, key), ExcludedApps = [.. _excluded] }
            : throw new InvalidOperationException("The trigger has no shortcut.");
    }

    private void ChooseButtonKey(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnInputChanged();
        }
    }

    private void OnInputChanged()
    {
        OnPropertyChanged(nameof(ButtonProblem));
        OnPropertyChanged(nameof(HasButtonProblem));
        OnPropertyChanged(nameof(IsBackOrForwardAlone));
        OnPropertyChanged(nameof(ExcludedDescription));
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnRecorderChanged()
    {
        var recorded = Recorder.Recorded;
        if (!ReferenceEquals(recorded, _checked))
        {
            _checked = recorded;
            _check = recorded is null
                ? null
                : ShortcutCheck.Check(recorded, _features, _triggers, KeyboardLayout.Installed(), KeyboardLayout.TypedWithAltGr, IsFree);
            Follow(recorded);
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(HasProblem));
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(CanSave));
        }

        OnPropertyChanged(nameof(ShortcutText));
    }

    // Choose keys' check boxes and list show the shortcut recorded.
    private void Follow(RecordedShortcut? recorded)
    {
        _following = true;
        var modifiers = recorded?.Modifiers ?? HotKeyModifiers.None;
        ChooseControl = modifiers.HasFlag(HotKeyModifiers.Control);
        ChooseAlt = modifiers.HasFlag(HotKeyModifiers.Alt);
        ChooseShift = modifiers.HasFlag(HotKeyModifiers.Shift);
        ChooseWindows = modifiers.HasFlag(HotKeyModifiers.Windows);
        SelectedKey = Keys.FirstOrDefault(choice => choice.Key == recorded?.Key);
        _following = false;
    }

    private void Choose(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && !_following)
        {
            UseChosenKeys();
        }
    }

    private void UseChosenKeys()
    {
        if (SelectedKey is not { } key)
        {
            return;
        }

        var modifiers = HotKeyModifiers.None;
        modifiers |= ChooseControl ? HotKeyModifiers.Control : HotKeyModifiers.None;
        modifiers |= ChooseAlt ? HotKeyModifiers.Alt : HotKeyModifiers.None;
        modifiers |= ChooseShift ? HotKeyModifiers.Shift : HotKeyModifiers.None;
        modifiers |= ChooseWindows ? HotKeyModifiers.Windows : HotKeyModifiers.None;
        Recorder.Set(new RecordedShortcut(modifiers, key.Key, Taken: false));
    }

    // The trigger's own shortcut is PC Companion's already; the host lets go of the others while the box listens.
    private bool IsFree(HotKeyModifiers modifiers, string key)
    {
        if (_originalInput == new TriggerInput(modifiers, key, null))
        {
            return true;
        }

        return KeyCodes.ScanCodeOf(key) is not { } scanCode
            || KeyboardLayout.VirtualKeyOf(scanCode, _layout) is not { } virtualKey
            || HotKey.IsFree(modifiers, virtualKey);
    }

    private string ProblemText(ShortcutCheckResult check) => check.Problem switch
    {
        ShortcutCheckProblem.UnknownKey => Strings.ShortcutsAndMouseButtonsEditorUnknownKey,
        ShortcutCheckProblem.NeedsModifier => Strings.ShortcutsAndMouseButtonsEditorNeedsModifier,
        ShortcutCheckProblem.Reserved => Strings.ShortcutsAndMouseButtonsEditorReserved,
        ShortcutCheckProblem.SameAsFeature => string.Format(CultureInfo.CurrentCulture, SameAsFeatureFormat, check.Other?.Name),
        ShortcutCheckProblem.SameAsTrigger => string.Format(CultureInfo.CurrentCulture, SameAsTriggerFormat, check.Other?.Name),
        ShortcutCheckProblem.AltGr when check.AltGr is { } typed => KeyboardLanguage.NameOf(typed.Layout) is { } language
            ? string.Format(CultureInfo.CurrentCulture, AltGrFormat, language, ShortcutText, typed.Text)
            : string.Format(CultureInfo.CurrentCulture, AltGrOtherLayoutFormat, ShortcutText, typed.Text),
        ShortcutCheckProblem.Taken => Strings.ShortcutsAndMouseButtonsEditorTaken,
        _ => string.Empty,
    };

    private bool AddExcluded(ListedApp app)
    {
        if (_excluded.Any(listed => listed.IsSameAppAs(app)))
        {
            return false;
        }

        _excluded.Add(app);
        return true;
    }

    private bool RemoveExcluded(int index)
    {
        if (index < 0 || index >= _excluded.Count)
        {
            return false;
        }

        _excluded.RemoveAt(index);
        return true;
    }

    // Letters, then digits, function keys, named keys and punctuation.
    private static int Rank(string key) => key switch
    {
        ['K', 'e', 'y', _] => 0,
        ['D', 'i', 'g', 'i', 't', _] => 1,
        ['F', var digit, ..] when char.IsAsciiDigit(digit) => 2,
        _ when Array.IndexOf(NamedKeys, key) >= 0 => 3,
        _ => 4,
    };

    // Within a rank: A to Z, 1 to 9 and 0, F1 to F24, the named keys' order, and punctuation by position.
    private static int Order(string key) => Rank(key) switch
    {
        0 => key[3],
        1 => key[5] == '0' ? 10 : key[5] - '0',
        2 => int.Parse(key.AsSpan(1), CultureInfo.InvariantCulture),
        3 => Array.IndexOf(NamedKeys, key),
        _ => (int)(KeyCodes.ScanCodeOf(key) ?? 0),
    };
}
