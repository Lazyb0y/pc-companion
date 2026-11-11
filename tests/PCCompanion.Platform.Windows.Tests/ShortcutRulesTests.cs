using System.Collections.Generic;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ShortcutRulesTests
{
    private const HotKeyModifiers ControlAlt = HotKeyModifiers.Control | HotKeyModifiers.Alt;
    private const nint English = 0x04090409;
    private const nint German = 0x04070407;
    private const nint Polish = 0x04150415;

    // What some keys type with AltGr, as the German and Polish (Programmers) layouts have it.
    private static readonly Dictionary<(nint Layout, uint ScanCode, bool Shift), string> AltGr = new()
    {
        [(German, 0x12, false)] = "€",
        [(German, 0x10, false)] = "@",
        [(Polish, 0x1E, false)] = "ą",
        [(Polish, 0x1E, true)] = "Ą",
    };

    [Theory]
    [InlineData(ControlAlt, "KeyH")]
    [InlineData(HotKeyModifiers.Windows, "KeyH")]
    [InlineData(HotKeyModifiers.Alt | HotKeyModifiers.Shift, "Backquote")]
    [InlineData(HotKeyModifiers.Control, "F1")]
    [InlineData(HotKeyModifiers.None, "F13")]
    [InlineData(HotKeyModifiers.Shift, "F24")]
    [InlineData(HotKeyModifiers.Windows | HotKeyModifiers.Shift, "KeyL")]
    public void AShortcutWithCtrlAltOrTheWindowsKeyCanBeUsed(HotKeyModifiers modifiers, string key)
    {
        Assert.Equal(ShortcutRuleProblem.None, ShortcutRules.Check(modifiers, key));
    }

    [Theory]
    [InlineData(HotKeyModifiers.None, "KeyH")]
    [InlineData(HotKeyModifiers.Shift, "KeyH")]
    [InlineData(HotKeyModifiers.None, "F1")]
    [InlineData(HotKeyModifiers.Shift, "Escape")]
    public void AKeyThatTypesNeedsCtrlAltOrTheWindowsKey(HotKeyModifiers modifiers, string key)
    {
        Assert.Equal(ShortcutRuleProblem.NeedsModifier, ShortcutRules.Check(modifiers, key));
    }

    [Theory]
    [InlineData("F12")]
    [InlineData("Numpad1")]
    [InlineData("CapsLock")]
    public void KeysAShortcutCantUseAreUnknown(string key)
    {
        Assert.Equal(ShortcutRuleProblem.UnknownKey, ShortcutRules.Check(ControlAlt, key));
    }

    [Theory]
    [InlineData(HotKeyModifiers.Windows, "KeyL")]
    [InlineData(ControlAlt, "Delete")]
    [InlineData(ControlAlt | HotKeyModifiers.Shift, "Delete")]
    public void ShortcutsWindowsNeverGivesAreReserved(HotKeyModifiers modifiers, string key)
    {
        Assert.Equal(ShortcutRuleProblem.Reserved, ShortcutRules.Check(modifiers, key));
    }

    [Fact]
    public void CtrlAltWithAKeyThatTypesWithAltGrInALayoutConflicts()
    {
        Assert.Equal(new AltGrCharacter(German, "€"), ShortcutRules.AltGrConflict(ControlAlt, "KeyE", [English, German], Typed));
        Assert.Equal(new AltGrCharacter(Polish, "ą"), ShortcutRules.AltGrConflict(ControlAlt, "KeyA", [English, German, Polish], Typed));
    }

    [Fact]
    public void ShiftInTheShortcutIsShiftWithAltGr()
    {
        Assert.Equal(new AltGrCharacter(Polish, "Ą"), ShortcutRules.AltGrConflict(ControlAlt | HotKeyModifiers.Shift, "KeyA", [Polish], Typed));
    }

    [Theory]
    [InlineData(ControlAlt, "KeyH")]
    [InlineData(ControlAlt | HotKeyModifiers.Windows, "KeyE")]
    [InlineData(HotKeyModifiers.Control, "KeyE")]
    [InlineData(HotKeyModifiers.Alt, "KeyE")]
    [InlineData(ControlAlt, "F12")]
    public void OnlyCtrlAltWithoutTheWindowsKeyIsAltGr(HotKeyModifiers modifiers, string key)
    {
        Assert.Null(ShortcutRules.AltGrConflict(modifiers, key, [English, German, Polish], Typed));
    }

    [Theory]
    [InlineData(HotKeyModifiers.Alt, "Space", true)]
    [InlineData(HotKeyModifiers.Windows, "KeyJ", true)]
    [InlineData(HotKeyModifiers.Windows | HotKeyModifiers.Alt, "Enter", true)]
    [InlineData(ControlAlt, "Space", false)]
    [InlineData(HotKeyModifiers.Windows | HotKeyModifiers.Shift, "KeyJ", false)]
    [InlineData(ControlAlt, "KeyH", false)]
    public void WindowsOrAppsUseSomeShortcutsTheyLetProgramsRegister(HotKeyModifiers modifiers, string key, bool used)
    {
        Assert.Equal(used, ShortcutRules.UsedByWindows(modifiers, key));
    }

    [Fact]
    public void WithoutLayoutsThatTypeWithAltGrNothingConflicts()
    {
        Assert.Null(ShortcutRules.AltGrConflict(ControlAlt, "KeyE", [English], Typed));
    }

    private static string? Typed(uint scanCode, bool shift, nint layout) => AltGr.GetValueOrDefault((layout, scanCode, shift));
}
