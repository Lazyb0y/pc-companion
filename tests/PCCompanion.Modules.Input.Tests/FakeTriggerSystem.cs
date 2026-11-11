using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    public nint FrontWindowHandle { get; set; }

    public Dictionary<nint, AppOfWindow> AppsOfWindows { get; } = [];

    public List<(nint Window, AppOfWindow App)> Taskbar { get; } = [];

    public int TaskbarReads { get; private set; }

    public HashSet<nint> Closed { get; } = [];

    public bool FullScreen { get; set; }

    public int FullScreenReads { get; private set; }

    public bool Watching { get; private set; }

    public bool ButtonHeld { get; set; }

    public PointerClaim? Claimed { get; private set; }

    public int Claims { get; private set; }

    public Dictionary<uint, PointerInputOutcome> Outcomes { get; } = [];

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

    public nint FrontWindow() => FrontWindowHandle;

    public AppOfWindow AppOf(nint window) => AppsOfWindows[window];

    public Task<IReadOnlyList<(nint Window, AppOfWindow App)>> ReadTaskbarAppsAsync()
    {
        TaskbarReads++;
        return Task.FromResult<IReadOnlyList<(nint Window, AppOfWindow App)>>([.. Taskbar]);
    }

    public bool Exists(nint window) => !Closed.Contains(window);

    public bool IsFrontFullScreen()
    {
        FullScreenReads++;
        return FullScreen;
    }

    public void WatchFront(bool watch) => Watching = watch;

    public bool IsHeld(PointerTrigger trigger) => ButtonHeld;

    public void Claim(PointerClaim? claim)
    {
        Claimed = claim;
        Claims++;
    }

    public PointerInputOutcome Outcome(uint time) => Outcomes.GetValueOrDefault(time, PointerInputOutcome.LetThrough);

    public void SetHookAgain() => Events.Add("hook again");

    private sealed class Registration(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
