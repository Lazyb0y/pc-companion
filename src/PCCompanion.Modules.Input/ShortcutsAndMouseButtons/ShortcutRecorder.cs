using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>A key the settings app's box sees: its virtual key, and its scan code, with 0xE0 in the high byte for an extended key.</summary>
public readonly record struct RecorderKey(uint VirtualKey, uint ScanCode);

/// <summary>A shortcut the box recorded.</summary>
/// <param name="Key">The key's name, as <see cref="KeyCodes"/> names it, or <see langword="null"/> for a key that shortcuts can't use, such as one of the numeric keypad.</param>
/// <param name="Taken">
/// Whether only the key's release arrived, as when Windows or another program has the shortcut and
/// takes the press.
/// </param>
public sealed record RecordedShortcut(HotKeyModifiers Modifiers, string? Key, bool Taken);

/// <summary>
/// Records a shortcut from the keys pressed in the settings app's box, as "The editor" in
/// docs/features/input/shortcuts-and-mouse-buttons.md says. It keeps the shortcut once a key that
/// isn't a modifier is pressed; Tab, Esc, Enter and Space without Ctrl, Alt or the Windows key keep
/// their usual meaning, so keyboard users can leave the box and the dialog. A key whose release
/// arrives without its press, while Ctrl, Alt or the Windows key is held, is a shortcut that
/// Windows or another program has. Use it on one thread.
/// </summary>
public sealed class ShortcutRecorder
{
    private const HotKeyModifiers StartsAShortcut = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Windows;

    private readonly HashSet<uint> _down = [];
    private bool _keyWhileHeld;

    /// <summary>Raised after <see cref="Held"/>, <see cref="Recorded"/> or <see cref="IsHolding"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The modifier keys held now.</summary>
    public HotKeyModifiers Held { get; private set; }

    /// <summary>The shortcut recorded last, or <see langword="null"/>.</summary>
    public RecordedShortcut? Recorded { get; private set; }

    /// <summary>
    /// Whether modifier keys are held and no other key was pressed with them yet, so the box shows
    /// the keys held rather than <see cref="Recorded"/>.
    /// </summary>
    public bool IsHolding => Held != HotKeyModifiers.None && !_keyWhileHeld;

    /// <summary>Starts listening, with the modifier keys that are already held, as when the box gets the focus.</summary>
    public void Start(HotKeyModifiers held)
    {
        _down.Clear();
        _keyWhileHeld = false;
        Held = held;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the shortcut shown, as when the box starts with the trigger's or one chosen with check boxes.</summary>
    public void Set(RecordedShortcut? shortcut)
    {
        Recorded = shortcut;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A key went down. Returns whether the box handles it; one it doesn't keeps its usual meaning.</summary>
    public bool KeyDown(RecorderKey key)
    {
        if (ModifierOf(key.VirtualKey) is { } modifier)
        {
            if (!Held.HasFlag(modifier))
            {
                Held |= modifier;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        var repeat = !_down.Add(key.ScanCode);
        if ((Held & StartsAShortcut) == 0 && KeepsItsMeaning(key.VirtualKey))
        {
            return false;
        }

        if (!repeat)
        {
            _keyWhileHeld = true;
            Recorded = new RecordedShortcut(Held, KeyCodes.NameOf(key.ScanCode), Taken: false);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>A key went up. Returns whether the box handles it.</summary>
    public bool KeyUp(RecorderKey key)
    {
        if (ModifierOf(key.VirtualKey) is { } modifier)
        {
            Held &= ~modifier;
            if (Held == HotKeyModifiers.None)
            {
                _keyWhileHeld = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (_down.Remove(key.ScanCode))
        {
            return !((Held & StartsAShortcut) == 0 && KeepsItsMeaning(key.VirtualKey));
        }

        // Windows or another program took the press. Without Ctrl, Alt or the Windows key, it's a
        // key held as the box got the focus, such as the Tab that moved it there.
        if ((Held & StartsAShortcut) == 0)
        {
            return false;
        }

        _keyWhileHeld = true;
        Recorded = new RecordedShortcut(Held, KeyCodes.NameOf(key.ScanCode), Taken: true);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Tab, Esc, Enter and Space.
    private static bool KeepsItsMeaning(uint virtualKey) => virtualKey is 0x09 or 0x1B or 0x0D or 0x20;

    private static HotKeyModifiers? ModifierOf(uint virtualKey) => virtualKey switch
    {
        0x10 or 0xA0 or 0xA1 => HotKeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => HotKeyModifiers.Control,
        0x12 or 0xA4 or 0xA5 => HotKeyModifiers.Alt,
        0x5B or 0x5C => HotKeyModifiers.Windows,
        _ => null,
    };
}
