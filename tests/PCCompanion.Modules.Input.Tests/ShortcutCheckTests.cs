using System.Collections.Generic;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class ShortcutCheckTests
{
    private const HotKeyModifiers ControlAlt = HotKeyModifiers.Control | HotKeyModifiers.Alt;
    private const HotKeyModifiers WindowsControl = HotKeyModifiers.Windows | HotKeyModifiers.Control;
    private const nint German = 0x04070407;

    private static readonly KnownShortcut HideApp = new(new TriggerInput(WindowsControl, "KeyH", null), "Hide Apps");
    private static readonly KnownShortcut LockOnF13 = new(new TriggerInput(HotKeyModifiers.None, "F13", null), "Lock");

    private readonly HashSet<(HotKeyModifiers, string)> _taken = [];

    [Fact]
    public void AFreeShortcutCanBeSaved()
    {
        var result = Check(new RecordedShortcut(ControlAlt, "KeyH", Taken: false));

        Assert.True(result.CanSave);
        Assert.False(result.UsedByWindows);
    }

    [Theory]
    [InlineData(HotKeyModifiers.Shift, "KeyH", ShortcutCheckProblem.NeedsModifier)]
    [InlineData(HotKeyModifiers.Control, "F12", ShortcutCheckProblem.UnknownKey)]
    [InlineData(HotKeyModifiers.Windows, "KeyL", ShortcutCheckProblem.Reserved)]
    public void TheRulesComeFirst(HotKeyModifiers modifiers, string key, ShortcutCheckProblem problem)
    {
        _taken.Add((modifiers, key));

        Assert.Equal(problem, Check(new RecordedShortcut(modifiers, key, Taken: true)).Problem);
    }

    [Fact]
    public void AKeyWithoutANameCantBeUsed()
    {
        Assert.Equal(ShortcutCheckProblem.UnknownKey, Check(new RecordedShortcut(HotKeyModifiers.Control, null, Taken: false)).Problem);
    }

    [Fact]
    public void AnotherFeaturesShortcutIsNamed()
    {
        var result = Check(new RecordedShortcut(WindowsControl, "KeyH", Taken: false));

        Assert.Equal((ShortcutCheckProblem.SameAsFeature, HideApp), (result.Problem, result.Other));
    }

    [Fact]
    public void AnotherShortcutOnThePageIsNamed()
    {
        var result = Check(new RecordedShortcut(HotKeyModifiers.None, "F13", Taken: false));

        Assert.Equal((ShortcutCheckProblem.SameAsTrigger, LockOnF13), (result.Problem, result.Other));
    }

    [Fact]
    public void ACtrlAltShortcutThatTypesWithAltGrNamesTheLayoutAndCharacter()
    {
        var result = Check(new RecordedShortcut(ControlAlt, "KeyE", Taken: false));

        Assert.Equal((ShortcutCheckProblem.AltGr, new AltGrCharacter(German, "€")), (result.Problem, result.AltGr));
    }

    [Fact]
    public void AShortcutWindowsRefusesOrThatArrivedAsAReleaseIsTaken()
    {
        _taken.Add((ControlAlt, "KeyJ"));

        Assert.Equal(ShortcutCheckProblem.Taken, Check(new RecordedShortcut(ControlAlt, "KeyJ", Taken: false)).Problem);
        Assert.Equal(ShortcutCheckProblem.Taken, Check(new RecordedShortcut(HotKeyModifiers.Windows, "KeyE", Taken: true)).Problem);
    }

    [Fact]
    public void AShortcutWindowsUsesAfterAllIsAWarningThatAllowsSaving()
    {
        var result = Check(new RecordedShortcut(HotKeyModifiers.Alt, "Space", Taken: false));

        Assert.True(result.CanSave);
        Assert.True(result.UsedByWindows);
    }

    private ShortcutCheckResult Check(RecordedShortcut shortcut) =>
        ShortcutCheck.Check(shortcut, [HideApp], [LockOnF13], [German], Typed, (modifiers, key) => !_taken.Contains((modifiers, key)));

    // German types € with AltGr + E.
    private static string? Typed(uint scanCode, bool shift, nint layout) => layout == German && scanCode == 0x12 && !shift ? "€" : null;
}
