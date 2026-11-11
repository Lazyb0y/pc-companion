using System;
using System.Collections.Generic;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// Shortcuts and mouse buttons as the settings app shows them, such as "Windows logo key + Ctrl +
/// H" or "Ctrl + Back button": the modifiers in Microsoft's order and terms, a named key by its
/// name, and another key as the keyboard layout writes it, so the same key shows "`" in US English
/// and "^" in German.
/// </summary>
public static class TriggerText
{
    private const string Separator = " + ";

    /// <summary>The text of a shortcut, with its key as <paramref name="layout"/> writes it.</summary>
    public static string Of(HotKeyModifiers modifiers, string key, nint layout)
    {
        ArgumentNullException.ThrowIfNull(key);

        return string.Join(Separator, [.. ModifierNames(modifiers), KeyName(key, layout)]);
    }

    /// <summary>The text of a mouse button with <paramref name="modifiers"/>.</summary>
    public static string Of(HotKeyModifiers modifiers, TriggerButton button) =>
        string.Join(Separator, [.. ModifierNames(modifiers), ButtonName(button)]);

    /// <summary>The text of a trigger's input, or <see langword="null"/> for a trigger that has neither a usable shortcut nor a usable mouse button.</summary>
    public static string? Of(Trigger trigger, nint layout)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger.Shortcut is { } shortcut && TriggerModifiers.ModifiersOf(shortcut.Modifiers) is { } keys && KeyCodes.ScanCodeOf(shortcut.Key) is not null)
        {
            return Of(keys, shortcut.Key, layout);
        }

        return trigger.MouseButton is { } button && TriggerModifiers.ModifiersOf(button.Modifiers) is { } buttonKeys && Enum.IsDefined(button.Button)
            ? Of(buttonKeys, button.Button)
            : null;
    }

    /// <summary>
    /// A key's name: its own for a named key, such as "Esc", and otherwise what it types in
    /// <paramref name="layout"/>, upper case, such as "H" or "Ö", or its code name when it types
    /// nothing that can be shown.
    /// </summary>
    public static string KeyName(string key, nint layout)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (NamedKey(key) is { } name)
        {
            return name;
        }

        if (key.Length is 2 or 3 && key[0] == 'F' && char.IsAsciiDigit(key[1]))
        {
            return key;
        }

        return KeyCodes.ScanCodeOf(key) is { } scanCode
            && KeyboardLayout.CharacterOf(scanCode, layout) is { } character
            && !char.IsControl(character)
            && !char.IsWhiteSpace(character)
                ? char.ToUpper(character, System.Globalization.CultureInfo.CurrentCulture).ToString()
                : key;
    }

    /// <summary>The modifiers' names, in the order Windows logo key, Ctrl, Alt, Shift.</summary>
    public static IReadOnlyList<string> ModifierNames(HotKeyModifiers modifiers)
    {
        var names = new List<string>(4);
        if (modifiers.HasFlag(HotKeyModifiers.Windows))
        {
            names.Add(Strings.ShortcutsAndMouseButtonsKeyWindows);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Control))
        {
            names.Add(Strings.ShortcutsAndMouseButtonsKeyControl);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            names.Add(Strings.ShortcutsAndMouseButtonsKeyAlt);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            names.Add(Strings.ShortcutsAndMouseButtonsKeyShift);
        }

        return names;
    }

    /// <summary>A mouse button's name, such as "Back button".</summary>
    public static string ButtonName(TriggerButton button) => button switch
    {
        TriggerButton.Back => Strings.ShortcutsAndMouseButtonsButtonBack,
        TriggerButton.Forward => Strings.ShortcutsAndMouseButtonsButtonForward,
        _ => Strings.ShortcutsAndMouseButtonsButtonMiddle,
    };

    private static string? NamedKey(string key) => key switch
    {
        "Escape" => Strings.ShortcutsAndMouseButtonsKeyEscape,
        "Tab" => Strings.ShortcutsAndMouseButtonsKeyTab,
        "Space" => Strings.ShortcutsAndMouseButtonsKeySpace,
        "Enter" => Strings.ShortcutsAndMouseButtonsKeyEnter,
        "Backspace" => Strings.ShortcutsAndMouseButtonsKeyBackspace,
        "Insert" => Strings.ShortcutsAndMouseButtonsKeyInsert,
        "Delete" => Strings.ShortcutsAndMouseButtonsKeyDelete,
        "Home" => Strings.ShortcutsAndMouseButtonsKeyHome,
        "End" => Strings.ShortcutsAndMouseButtonsKeyEnd,
        "PageUp" => Strings.ShortcutsAndMouseButtonsKeyPageUp,
        "PageDown" => Strings.ShortcutsAndMouseButtonsKeyPageDown,
        "ArrowUp" => Strings.ShortcutsAndMouseButtonsKeyArrowUp,
        "ArrowDown" => Strings.ShortcutsAndMouseButtonsKeyArrowDown,
        "ArrowLeft" => Strings.ShortcutsAndMouseButtonsKeyArrowLeft,
        "ArrowRight" => Strings.ShortcutsAndMouseButtonsKeyArrowRight,
        "PrintScreen" => Strings.ShortcutsAndMouseButtonsKeyPrintScreen,
        "Pause" => Strings.ShortcutsAndMouseButtonsKeyPause,
        "ContextMenu" => Strings.ShortcutsAndMouseButtonsKeyMenu,
        _ => null,
    };
}
