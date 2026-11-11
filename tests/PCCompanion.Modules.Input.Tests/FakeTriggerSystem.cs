using System;
using System.Collections.Generic;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>
/// Stands in for Windows and the host for <see cref="TriggerController"/>: keyboard layouts, the
/// app in front, registrations, which a test presses, and what the controller asks for, recorded
/// as events such as "lock", "mask" or "run hideApps.hideOthers".
/// </summary>
internal sealed class FakeTriggerSystem : ITriggerSystem, IShortcutEnvironment
{
    public const nint English = 0x04090409;
    public const nint German = 0x04070407;

    public nint? Foreground { get; set; } = English;

    public List<nint> Layouts { get; } = [English];

    public HashSet<(HotKeyModifiers Modifiers, uint Key)> Taken { get; } = [];

    public Dictionary<(HotKeyModifiers Modifiers, uint Key), Action> Registered { get; } = [];

    public AppOfWindow? Front { get; set; }

    public int FrontReads { get; private set; }

    public bool AltHeld { get; set; }

    public bool KeysHeld { get; set; }

    public List<string> Events { get; } = [];

    public List<string> Refused { get; private set; } = [];

    public nint? ForegroundLayout() => Foreground;

    public IReadOnlyList<nint> InstalledLayouts() => Layouts;

    // H, E, A and F13, the same in both layouts; other keys mean none.
    public uint? VirtualKeyOf(uint scanCode, nint layout) => scanCode switch
    {
        0x23 => 0x48,
        0x12 => 0x45,
        0x1E => 0x41,
        0x64 => 0x7C,
        _ => null,
    };

    public IDisposable? Register(HotKeyModifiers modifiers, uint virtualKey, Action pressed)
    {
        if (Taken.Contains((modifiers, virtualKey)))
        {
            return null;
        }

        Registered[(modifiers, virtualKey)] = pressed;
        return new Registration(() => Registered.Remove((modifiers, virtualKey)));
    }

    public void Press(HotKeyModifiers modifiers, uint virtualKey) => Registered[(modifiers, virtualKey)]();

    public AppOfWindow? FrontApp()
    {
        FrontReads++;
        return Front;
    }

    // German types € with AltGr + E.
    public string? TypedWithAltGr(uint scanCode, bool shift, nint layout) => layout == German && scanCode == 0x12 && !shift ? "€" : null;

    public bool IsHeld(KeyPositionShortcut shortcut) => KeysHeld;

    public bool IsAltHeld() => AltHeld;

    public void PressMaskKey() => Events.Add("mask");

    public void Lock() => Events.Add("lock");

    public void TurnOffDisplays() => Events.Add("displays off");

    public void StartScreenSaver() => Events.Add("screen saver");

    public void Run(FeatureAction action) => Events.Add($"run {action.FeatureId}.{action.ActionId}");

    public void ReportRefused(IEnumerable<string> shortcuts) => Refused = [.. shortcuts];

    private sealed class Registration(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
