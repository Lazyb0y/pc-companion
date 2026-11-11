using System;
using System.Collections.Generic;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// What the feature's thread reads from Windows and asks of it, and of the host, so tests can
/// supply it instead. None of it waits for another app.
/// </summary>
public interface ITriggerSystem
{
    /// <summary>The app of the window in front, or <see langword="null"/> when no window is in front.</summary>
    AppOfWindow? FrontApp();

    /// <summary>The user's keyboard layouts.</summary>
    IReadOnlyList<nint> InstalledLayouts();

    /// <summary>What the key with <paramref name="scanCode"/> types with AltGr in <paramref name="layout"/>; see <see cref="KeyboardLayout.TypedWithAltGr"/>.</summary>
    string? TypedWithAltGr(uint scanCode, bool shift, nint layout);

    /// <summary>Whether any of a shortcut's keys is still held: its modifiers, or its key in the layout in front.</summary>
    bool IsHeld(KeyPositionShortcut shortcut);

    /// <summary>Whether Alt is held now.</summary>
    bool IsAltHeld();

    /// <summary>Presses a key that does nothing, so that releasing Alt opens no menu bar.</summary>
    void PressMaskKey();

    void Lock();

    void TurnOffDisplays();

    void StartScreenSaver();

    /// <summary>Runs another feature's action through the host; see <see cref="IHostActions.Run"/>.</summary>
    void Run(FeatureAction action);

    /// <summary>Says which of the feature's shortcuts Windows refused, by their names, for the settings app.</summary>
    void ReportRefused(IEnumerable<string> shortcuts);
}

/// <summary>Reads from Windows and asks of it for the feature's thread, whose hidden window is <paramref name="window"/>.</summary>
public sealed class WindowsTriggerSystem(MessageWindow window, IHostActions actions, ShortcutRefusals refusals, string featureId) : ITriggerSystem
{
    // VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN and VK_RWIN.
    private const uint Shift = 0x10;
    private const uint Control = 0x11;
    private const uint Alt = 0x12;
    private const uint LeftWindows = 0x5B;
    private const uint RightWindows = 0x5C;

    private readonly AppOfWindowReader _apps = new(new WindowsAppIdentityReader());

    public AppOfWindow? FrontApp() => Foreground.Window is var front && front != 0 ? _apps.Of(TopLevelWindows.Describe(front)) : null;

    public IReadOnlyList<nint> InstalledLayouts() => KeyboardLayout.Installed();

    public string? TypedWithAltGr(uint scanCode, bool shift, nint layout) => KeyboardLayout.TypedWithAltGr(scanCode, shift, layout);

    public bool IsHeld(KeyPositionShortcut shortcut)
    {
        var modifiers = shortcut.Modifiers;
        if ((modifiers.HasFlag(HotKeyModifiers.Shift) && InputState.IsKeyDown(Shift))
            || (modifiers.HasFlag(HotKeyModifiers.Control) && InputState.IsKeyDown(Control))
            || (modifiers.HasFlag(HotKeyModifiers.Alt) && InputState.IsKeyDown(Alt))
            || (modifiers.HasFlag(HotKeyModifiers.Windows) && (InputState.IsKeyDown(LeftWindows) || InputState.IsKeyDown(RightWindows))))
        {
            return true;
        }

        var layout = KeyboardLayout.OfForegroundWindow() ?? KeyboardLayout.OfCurrentThread();
        return KeyboardLayout.VirtualKeyOf(shortcut.ScanCode, layout) is { } key && InputState.IsKeyDown(key);
    }

    public bool IsAltHeld() => InputState.IsKeyDown(Alt);

    public void PressMaskKey() => ShellActions.PressMaskKey();

    public void Lock() => ShellActions.Lock();

    public void TurnOffDisplays() => ShellActions.TurnOffDisplays(window);

    public void StartScreenSaver() => ShellActions.StartScreenSaver(window);

    public void Run(FeatureAction action) => actions.Run(action);

    public void ReportRefused(IEnumerable<string> shortcuts) => refusals.Set(featureId, shortcuts);
}
