namespace PCCompanion.Platform.Windows;

/// <summary>
/// The keys of a shortcut as a feature registers them: the modifier keys and a key, either by its
/// virtual key, which follows the layout's characters, such as H, or by its position, such as the
/// key above Tab. The settings app compares them with the user's own shortcuts by position.
/// </summary>
/// <param name="Key">The virtual key, or the scan code when <paramref name="IsPosition"/>.</param>
public readonly record struct ShortcutKeys(HotKeyModifiers Modifiers, uint Key, bool IsPosition)
{
    /// <summary>A shortcut registered with <paramref name="virtualKey"/>, such as 0x48 for H.</summary>
    public static ShortcutKeys OfVirtualKey(HotKeyModifiers modifiers, uint virtualKey) => new(modifiers, virtualKey, IsPosition: false);

    /// <summary>A shortcut registered by its key's position.</summary>
    public static ShortcutKeys OfPosition(KeyPositionShortcut shortcut) => new(shortcut.Modifiers, shortcut.ScanCode, IsPosition: true);

    /// <summary>
    /// The shortcut by its key's position in <paramref name="layout"/>, or <see langword="null"/>
    /// when no key there means the virtual key.
    /// </summary>
    public KeyPositionShortcut? InLayout(nint layout) =>
        IsPosition
            ? new KeyPositionShortcut(Modifiers, Key)
            : KeyboardLayout.ScanCodeOf(Key, layout) is { } scanCode ? new KeyPositionShortcut(Modifiers, scanCode) : null;
}
