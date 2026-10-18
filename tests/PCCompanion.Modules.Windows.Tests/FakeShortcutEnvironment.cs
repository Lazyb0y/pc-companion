using System;
using System.Collections.Generic;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up keyboard layouts and a shortcut register that refuses the shortcuts in <see cref="Taken"/>.
/// </summary>
internal sealed class FakeShortcutEnvironment : IShortcutEnvironment
{
    public const nint English = 0x04090409;
    public const nint Russian = 0x04190419;
    public const nint German = 0x04070407;

    // The virtual key of the key above Tab in each layout.
    private static readonly Dictionary<nint, uint> KeyAboveTab = new() { [English] = 0xC0, [Russian] = 0xC0, [German] = 0xDC };

    public nint? Foreground { get; set; } = English;

    public List<nint> Layouts { get; } = [English];

    public HashSet<(HotKeyModifiers Modifiers, uint Key)> Taken { get; } = [];

    public Dictionary<(HotKeyModifiers Modifiers, uint Key), Action> Registered { get; } = [];

    public int Registrations { get; private set; }

    public nint? ForegroundLayout() => Foreground;

    public IReadOnlyList<nint> InstalledLayouts() => Layouts;

    public uint? VirtualKeyOf(uint scanCode, nint layout) =>
        scanCode == ShortcutKeeper.KeyAboveTab && KeyAboveTab.TryGetValue(layout, out var key) ? key : null;

    public IDisposable? Register(HotKeyModifiers modifiers, uint virtualKey, Action pressed)
    {
        if (Taken.Contains((modifiers, virtualKey)))
        {
            return null;
        }

        Registrations++;
        Registered.Add((modifiers, virtualKey), pressed);
        return new Registration(() => Registered.Remove((modifiers, virtualKey)));
    }

    public void Press(HotKeyModifiers modifiers, uint virtualKey) => Registered[(modifiers, virtualKey)]();

    private sealed class Registration(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
