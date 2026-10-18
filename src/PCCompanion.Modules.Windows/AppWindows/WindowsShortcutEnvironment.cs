using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Reads keyboard layouts from Windows and registers shortcuts for <paramref name="window"/>.
/// </summary>
internal sealed class WindowsShortcutEnvironment(MessageWindow window) : IShortcutEnvironment
{
    public nint? ForegroundLayout() => KeyboardLayout.OfForegroundWindow();

    public IReadOnlyList<nint> InstalledLayouts() => KeyboardLayout.Installed();

    public uint? VirtualKeyOf(uint scanCode, nint layout) => KeyboardLayout.VirtualKeyOf(scanCode, layout);

    public IDisposable? Register(HotKeyModifiers modifiers, uint virtualKey, Action pressed)
    {
        if (HotKey.Register(window, modifiers, virtualKey) is not { } hotKey)
        {
            return null;
        }

        hotKey.Pressed += (_, _) => pressed();
        return hotKey;
    }
}
