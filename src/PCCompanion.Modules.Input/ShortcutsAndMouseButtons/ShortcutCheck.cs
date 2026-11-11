using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>Why the editor can't save a shortcut.</summary>
public enum ShortcutCheckProblem
{
    /// <summary>It can be saved.</summary>
    None,

    /// <summary>Its key isn't one a shortcut can use.</summary>
    UnknownKey,

    /// <summary>It needs Ctrl, Alt or the Windows key.</summary>
    NeedsModifier,

    /// <summary>Windows never gives it to a program, such as Windows + L.</summary>
    Reserved,

    /// <summary>Another feature of PC Companion has it on its own page.</summary>
    SameAsFeature,

    /// <summary>Another shortcut on this page has it.</summary>
    SameAsTrigger,

    /// <summary>It types a character with AltGr in one of the user's keyboard layouts.</summary>
    AltGr,

    /// <summary>Windows or another program has it.</summary>
    Taken,
}

/// <summary>A shortcut that PC Companion already has, with the name of what has it: a feature's, or an action's on this page.</summary>
public sealed record KnownShortcut(TriggerInput Input, string Name);

/// <summary>What the editor's checks found about a shortcut.</summary>
/// <param name="Other">For <see cref="ShortcutCheckProblem.SameAsFeature"/> and <see cref="ShortcutCheckProblem.SameAsTrigger"/>, what has the shortcut.</param>
/// <param name="AltGr">For <see cref="ShortcutCheckProblem.AltGr"/>, the layout and the character.</param>
/// <param name="UsedByWindows">Whether Windows or apps use it although it can be registered, which the editor warns about and allows.</param>
public sealed record ShortcutCheckResult(ShortcutCheckProblem Problem, KnownShortcut? Other = null, AltGrCharacter? AltGr = null, bool UsedByWindows = false)
{
    public bool CanSave => Problem == ShortcutCheckProblem.None;
}

/// <summary>
/// The editor's checks of a recorded or chosen shortcut, in the order "The editor" in
/// docs/features/input/shortcuts-and-mouse-buttons.md lists them.
/// </summary>
public static class ShortcutCheck
{
    /// <param name="features">The shortcuts the other features have on their pages.</param>
    /// <param name="triggers">The other shortcuts on this page.</param>
    /// <param name="typedWithAltGr">What a key types with AltGr in a layout, as <see cref="KeyboardLayout.TypedWithAltGr"/> reads it.</param>
    /// <param name="isFree">Whether Windows would give PC Companion the shortcut now, by a test registration.</param>
    public static ShortcutCheckResult Check(
        RecordedShortcut shortcut,
        IEnumerable<KnownShortcut> features,
        IEnumerable<KnownShortcut> triggers,
        IReadOnlyList<nint> layouts,
        Func<uint, bool, nint, string?> typedWithAltGr,
        Func<HotKeyModifiers, string, bool> isFree)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(isFree);

        if (shortcut.Key is not { } key)
        {
            return new ShortcutCheckResult(ShortcutCheckProblem.UnknownKey);
        }

        var rule = ShortcutRules.Check(shortcut.Modifiers, key) switch
        {
            ShortcutRuleProblem.UnknownKey => ShortcutCheckProblem.UnknownKey,
            ShortcutRuleProblem.NeedsModifier => ShortcutCheckProblem.NeedsModifier,
            ShortcutRuleProblem.Reserved => ShortcutCheckProblem.Reserved,
            _ => ShortcutCheckProblem.None,
        };
        if (rule != ShortcutCheckProblem.None)
        {
            return new ShortcutCheckResult(rule);
        }

        var input = new TriggerInput(shortcut.Modifiers, key, null);
        if (features.FirstOrDefault(known => known.Input == input) is { } feature)
        {
            return new ShortcutCheckResult(ShortcutCheckProblem.SameAsFeature, feature);
        }

        if (triggers.FirstOrDefault(known => known.Input == input) is { } trigger)
        {
            return new ShortcutCheckResult(ShortcutCheckProblem.SameAsTrigger, trigger);
        }

        if (ShortcutRules.AltGrConflict(shortcut.Modifiers, key, layouts, typedWithAltGr) is { } typed)
        {
            return new ShortcutCheckResult(ShortcutCheckProblem.AltGr, AltGr: typed);
        }

        return shortcut.Taken || !isFree(shortcut.Modifiers, key)
            ? new ShortcutCheckResult(ShortcutCheckProblem.Taken)
            : new ShortcutCheckResult(ShortcutCheckProblem.None, UsedByWindows: ShortcutRules.UsedByWindows(shortcut.Modifiers, key));
    }
}
