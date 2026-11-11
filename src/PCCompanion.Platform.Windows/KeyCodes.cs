using System;
using System.Collections.Generic;
using System.Collections.Frozen;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The keys a shortcut can use, by their position on the keyboard, named as the web's
/// <c>KeyboardEvent.code</c> names them, such as <c>KeyH</c>, <c>Digit1</c> and <c>Backquote</c>,
/// with the scan code Windows reports for each. Extended keys, such as the arrows, have 0xE0 in
/// the scan code's high byte, and Pause 0xE1, as <c>MapVirtualKeyEx</c> takes them. See "Keys by
/// position" in docs/features/input/shortcuts-and-mouse-buttons.md.
/// </summary>
public static class KeyCodes
{
    private static readonly FrozenDictionary<string, uint> ByName = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        // Letters.
        ["KeyA"] = 0x1E, ["KeyB"] = 0x30, ["KeyC"] = 0x2E, ["KeyD"] = 0x20, ["KeyE"] = 0x12, ["KeyF"] = 0x21,
        ["KeyG"] = 0x22, ["KeyH"] = 0x23, ["KeyI"] = 0x17, ["KeyJ"] = 0x24, ["KeyK"] = 0x25, ["KeyL"] = 0x26,
        ["KeyM"] = 0x32, ["KeyN"] = 0x31, ["KeyO"] = 0x18, ["KeyP"] = 0x19, ["KeyQ"] = 0x10, ["KeyR"] = 0x13,
        ["KeyS"] = 0x1F, ["KeyT"] = 0x14, ["KeyU"] = 0x16, ["KeyV"] = 0x2F, ["KeyW"] = 0x11, ["KeyX"] = 0x2D,
        ["KeyY"] = 0x15, ["KeyZ"] = 0x2C,

        // The digits above the letters.
        ["Digit1"] = 0x02, ["Digit2"] = 0x03, ["Digit3"] = 0x04, ["Digit4"] = 0x05, ["Digit5"] = 0x06,
        ["Digit6"] = 0x07, ["Digit7"] = 0x08, ["Digit8"] = 0x09, ["Digit9"] = 0x0A, ["Digit0"] = 0x0B,

        // Punctuation, whose characters differ by layout.
        ["Minus"] = 0x0C, ["Equal"] = 0x0D, ["BracketLeft"] = 0x1A, ["BracketRight"] = 0x1B,
        ["Backslash"] = 0x2B, ["Semicolon"] = 0x27, ["Quote"] = 0x28, ["Backquote"] = 0x29,
        ["Comma"] = 0x33, ["Period"] = 0x34, ["Slash"] = 0x35, ["IntlBackslash"] = 0x56,
        ["IntlRo"] = 0x73, ["IntlYen"] = 0x7D,

        // Function keys; F12 is left out, since Windows keeps it for debuggers.
        ["F1"] = 0x3B, ["F2"] = 0x3C, ["F3"] = 0x3D, ["F4"] = 0x3E, ["F5"] = 0x3F, ["F6"] = 0x40,
        ["F7"] = 0x41, ["F8"] = 0x42, ["F9"] = 0x43, ["F10"] = 0x44, ["F11"] = 0x57,
        ["F13"] = 0x64, ["F14"] = 0x65, ["F15"] = 0x66, ["F16"] = 0x67, ["F17"] = 0x68, ["F18"] = 0x69,
        ["F19"] = 0x6A, ["F20"] = 0x6B, ["F21"] = 0x6C, ["F22"] = 0x6D, ["F23"] = 0x6E, ["F24"] = 0x76,

        // Named keys.
        ["Escape"] = 0x01, ["Tab"] = 0x0F, ["Space"] = 0x39, ["Enter"] = 0x1C, ["Backspace"] = 0x0E,
        ["Insert"] = 0xE052, ["Delete"] = 0xE053, ["Home"] = 0xE047, ["End"] = 0xE04F,
        ["PageUp"] = 0xE049, ["PageDown"] = 0xE051, ["ArrowUp"] = 0xE048, ["ArrowDown"] = 0xE050,
        ["ArrowLeft"] = 0xE04B, ["ArrowRight"] = 0xE04D, ["PrintScreen"] = 0xE037, ["Pause"] = 0xE11D,
        ["ContextMenu"] = 0xE05D,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<uint, string> ByScanCode = ByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Every key a shortcut can use, by name.</summary>
    public static IReadOnlyCollection<string> All => ByName.Keys;

    /// <summary>The scan code of the key named <paramref name="code"/>, or <see langword="null"/> for a key a shortcut can't use.</summary>
    public static uint? ScanCodeOf(string code) => code is not null && ByName.TryGetValue(code, out var scanCode) ? scanCode : null;

    /// <summary>The name of the key with <paramref name="scanCode"/>, or <see langword="null"/> for a key a shortcut can't use.</summary>
    public static string? NameOf(uint scanCode) => ByScanCode.GetValueOrDefault(scanCode);

    /// <summary>Whether the key can make a shortcut on its own: F13 to F24, which no keyboard types with.</summary>
    public static bool StandsAlone(string code) => code is ['F', _, _] && int.TryParse(code.AsSpan(1), out var number) && number is >= 13 and <= 24;
}
