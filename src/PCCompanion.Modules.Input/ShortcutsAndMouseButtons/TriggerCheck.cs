using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>Why a trigger can't be used, as <see cref="TriggerCheck"/> finds it.</summary>
public enum TriggerProblem
{
    /// <summary>It can be used.</summary>
    None,

    /// <summary>It names no feature or no action.</summary>
    NoAction,

    /// <summary>It has neither a shortcut nor a mouse button.</summary>
    NoInput,

    /// <summary>It has both a shortcut and a mouse button.</summary>
    BothInputs,

    /// <summary>One of its modifier keys isn't Ctrl, Alt, Shift or the Windows key.</summary>
    UnknownModifier,

    /// <summary>Its key isn't one a shortcut can use, such as F12.</summary>
    UnknownKey,

    /// <summary>Its shortcut needs Ctrl, Alt or the Windows key.</summary>
    NeedsModifier,

    /// <summary>Its shortcut is one Windows never gives a program, such as Windows + L.</summary>
    Reserved,

    /// <summary>Its mouse button isn't Back, Forward or Middle.</summary>
    UnknownButton,

    /// <summary>Its middle button doesn't have the Windows key.</summary>
    MiddleNeedsWindowsKey,

    /// <summary>A trigger earlier in the list has the same shortcut or mouse button.</summary>
    SameAsEarlier,
}

/// <summary>A trigger, its place in the list and why it can't be used, if it can't.</summary>
public sealed record CheckedTrigger(int Index, Trigger Trigger, TriggerProblem Problem)
{
    public bool IsUsable => Problem == TriggerProblem.None;
}

/// <summary>
/// Which triggers can be used, as "The format" in docs/features/input/shortcuts-and-mouse-buttons.md
/// says. The settings app shows a trigger that can't be used with its problem; the host skips it.
/// </summary>
public static class TriggerCheck
{
    /// <summary>Checks each trigger of <paramref name="triggers"/>, in order; a later one with the same shortcut or button as a usable earlier one can't be used.</summary>
    public static IReadOnlyList<CheckedTrigger> CheckAll(IReadOnlyList<Trigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        var checkedTriggers = new List<CheckedTrigger>(triggers.Count);
        var inputs = new HashSet<TriggerInput>();
        for (var index = 0; index < triggers.Count; index++)
        {
            var trigger = triggers[index];
            var problem = Check(trigger);
            if (problem == TriggerProblem.None && InputOf(trigger) is { } input && !inputs.Add(input))
            {
                problem = TriggerProblem.SameAsEarlier;
            }

            checkedTriggers.Add(new CheckedTrigger(index, trigger, problem));
        }

        return checkedTriggers;
    }

    /// <summary>Why <paramref name="trigger"/> can't be used on its own, without the others of its list.</summary>
    public static TriggerProblem Check(Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (string.IsNullOrWhiteSpace(trigger.Action.Feature) || string.IsNullOrWhiteSpace(trigger.Action.Id))
        {
            return TriggerProblem.NoAction;
        }

        return (trigger.Shortcut, trigger.MouseButton) switch
        {
            (null, null) => TriggerProblem.NoInput,
            ({ }, { }) => TriggerProblem.BothInputs,
            ({ } shortcut, null) => CheckShortcut(shortcut),
            (null, { } button) => CheckButton(button),
        };
    }

    /// <summary>
    /// What the trigger is pressed with, to tell two triggers with the same shortcut or button
    /// apart from others; <see langword="null"/> for a trigger that can't be used.
    /// </summary>
    public static TriggerInput? InputOf(Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (Check(trigger) != TriggerProblem.None)
        {
            return null;
        }

        return trigger.Shortcut is { } shortcut
            ? new TriggerInput(TriggerModifiers.ModifiersOf(shortcut.Modifiers)!.Value, shortcut.Key, null)
            : new TriggerInput(TriggerModifiers.ModifiersOf(trigger.MouseButton!.Modifiers)!.Value, null, trigger.MouseButton.Button);
    }

    private static TriggerProblem CheckShortcut(TriggerShortcut shortcut)
    {
        if (TriggerModifiers.ModifiersOf(shortcut.Modifiers) is not { } modifiers)
        {
            return TriggerProblem.UnknownModifier;
        }

        return ShortcutRules.Check(modifiers, shortcut.Key) switch
        {
            ShortcutRuleProblem.UnknownKey => TriggerProblem.UnknownKey,
            ShortcutRuleProblem.NeedsModifier => TriggerProblem.NeedsModifier,
            ShortcutRuleProblem.Reserved => TriggerProblem.Reserved,
            _ => TriggerProblem.None,
        };
    }

    private static TriggerProblem CheckButton(TriggerMouseButton button)
    {
        if (TriggerModifiers.ModifiersOf(button.Modifiers) is not { } modifiers)
        {
            return TriggerProblem.UnknownModifier;
        }

        if (!Enum.IsDefined(button.Button))
        {
            return TriggerProblem.UnknownButton;
        }

        return button.Button == TriggerButton.Middle && !modifiers.HasFlag(HotKeyModifiers.Windows)
            ? TriggerProblem.MiddleNeedsWindowsKey
            : TriggerProblem.None;
    }
}

/// <summary>A shortcut, by its modifiers and key, or a mouse button, by its modifiers and button.</summary>
public readonly record struct TriggerInput(HotKeyModifiers Modifiers, string? Key, TriggerButton? Button)
{
    /// <summary>
    /// The input's name, such as <c>Control+Alt+KeyH</c> or <c>Windows+Middle</c>, as the feature names it in
    /// <see cref="ShortcutRefusals"/> and the log; it names keys, never what they type.
    /// </summary>
    public string Name => string.Join('+', [.. TriggerModifiers.NamesOf(Modifiers), Key ?? Button?.ToString() ?? string.Empty]);
}
