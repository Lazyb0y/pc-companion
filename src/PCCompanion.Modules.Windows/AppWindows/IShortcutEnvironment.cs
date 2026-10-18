using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// What <see cref="ShortcutKeeper"/> reads from Windows and registers there, so tests can supply it instead.
/// </summary>
public interface IShortcutEnvironment
{
    /// <summary>The keyboard layout of the window in front, or <see langword="null"/> when it can't be read.</summary>
    nint? ForegroundLayout();

    /// <summary>The user's keyboard layouts.</summary>
    IReadOnlyList<nint> InstalledLayouts();

    /// <summary>The virtual key that the key with <paramref name="scanCode"/> means in <paramref name="layout"/>, or <see langword="null"/>.</summary>
    uint? VirtualKeyOf(uint scanCode, nint layout);

    /// <summary>
    /// Registers a shortcut that calls <paramref name="pressed"/> each time it's pressed, until the
    /// result is disposed. Returns <see langword="null"/> when Windows refuses it.
    /// </summary>
    IDisposable? Register(HotKeyModifiers modifiers, uint virtualKey, Action pressed);
}
