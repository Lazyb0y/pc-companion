using System;
using System.Collections.Generic;

namespace PCCompanion.Platform.Windows;

/// <summary>Why a shortcut can't be used, as <see cref="ShortcutRules.Check"/> finds it.</summary>
public enum ShortcutRuleProblem
{
    /// <summary>It can be used.</summary>
    None,

    /// <summary>Its key isn't one a shortcut can use, such as F12, a modifier key, a lock key or the numeric keypad.</summary>
    UnknownKey,

    /// <summary>It needs Ctrl, Alt or the Windows key; only F13 to F24 can be pressed alone or with Shift.</summary>
    NeedsModifier,

    /// <summary>Windows never gives it to a program: Windows + L and Ctrl + Alt + Del.</summary>
    Reserved,
}

/// <summary>A character that a shortcut's keys type with AltGr in one of the user's keyboard layouts.</summary>
/// <param name="Layout">The layout, such as German.</param>
/// <param name="Text">What it types, such as €.</param>
public readonly record struct AltGrCharacter(nint Layout, string Text);

/// <summary>
/// Which shortcuts can be used: see "Which shortcuts" in
/// docs/features/input/shortcuts-and-mouse-buttons.md. The same rules apply in the settings app,
/// as a shortcut is recorded, and in the host, as it's registered.
/// </summary>
public static class ShortcutRules
{
    private const HotKeyModifiers Held = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Windows;

    // Shortcuts Windows lets a program register, which Windows or apps then lose, as the spike
    // found: see "Windows' own shortcuts" in docs/features/input/shortcuts-and-mouse-buttons.md.
    private static readonly (HotKeyModifiers Modifiers, string Key)[] UsedByWindowsList =
    [
        (HotKeyModifiers.Alt, "Space"),
        (HotKeyModifiers.Alt, "Escape"),
        (HotKeyModifiers.Control, "Space"),
        (HotKeyModifiers.Alt, "PrintScreen"),
        (HotKeyModifiers.Windows, "Slash"),
        (HotKeyModifiers.Windows, "Minus"),
        (HotKeyModifiers.Windows, "Escape"),
        (HotKeyModifiers.Windows | HotKeyModifiers.Alt, "KeyH"),
        (HotKeyModifiers.Windows | HotKeyModifiers.Alt, "Enter"),
        (HotKeyModifiers.Windows, "KeyJ"),
        (HotKeyModifiers.Windows, "KeyW"),
        (HotKeyModifiers.Windows, "KeyY"),
    ];

    /// <summary>Why the shortcut <paramref name="modifiers"/> + <paramref name="key"/> can't be used, or <see cref="ShortcutRuleProblem.None"/>.</summary>
    /// <param name="key">The key's name, such as <c>KeyH</c>; see <see cref="KeyCodes"/>.</param>
    public static ShortcutRuleProblem Check(HotKeyModifiers modifiers, string key)
    {
        if (KeyCodes.ScanCodeOf(key) is null)
        {
            return ShortcutRuleProblem.UnknownKey;
        }

        if ((modifiers & Held) == 0 && !KeyCodes.StandsAlone(key))
        {
            return ShortcutRuleProblem.NeedsModifier;
        }

        var reserved = (modifiers == HotKeyModifiers.Windows && key == "KeyL")
            || ((modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt)) == (HotKeyModifiers.Control | HotKeyModifiers.Alt) && key == "Delete");
        return reserved ? ShortcutRuleProblem.Reserved : ShortcutRuleProblem.None;
    }

    /// <summary>
    /// Whether Windows or apps use the shortcut although Windows lets a program register it, such
    /// as Alt + Space, so that the action takes its place.
    /// </summary>
    public static bool UsedByWindows(HotKeyModifiers modifiers, string key) =>
        Array.Exists(UsedByWindowsList, used => used.Modifiers == modifiers && used.Key == key);

    /// <summary>
    /// The first character that the shortcut's key types with AltGr in one of
    /// <paramref name="layouts"/>, which Windows would no longer type while the shortcut is
    /// registered, since it treats AltGr as Ctrl + Alt; or <see langword="null"/>. Only Ctrl + Alt
    /// without the Windows key is AltGr, with Shift when the shortcut has it.
    /// </summary>
    /// <param name="typedWithAltGr">What a key types with AltGr in a layout, as <see cref="KeyboardLayout.TypedWithAltGr"/> reads it.</param>
    public static AltGrCharacter? AltGrConflict(HotKeyModifiers modifiers, string key, IReadOnlyList<nint> layouts, Func<uint, bool, nint, string?> typedWithAltGr)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(typedWithAltGr);

        if ((modifiers & Held) != (HotKeyModifiers.Control | HotKeyModifiers.Alt) || KeyCodes.ScanCodeOf(key) is not { } scanCode)
        {
            return null;
        }

        var shift = modifiers.HasFlag(HotKeyModifiers.Shift);
        foreach (var layout in layouts)
        {
            if (typedWithAltGr(scanCode, shift, layout) is { Length: > 0 } text)
            {
                return new AltGrCharacter(layout, text);
            }
        }

        return null;
    }
}
