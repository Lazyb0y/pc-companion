using System.Linq;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Uses the user's own keyboard layouts for keys that type, and checks only what's the same in all of them.
public sealed class TriggerTextTests
{
    private const string Plus = " + ";

    [Fact]
    public void TheModifiersComeInMicrosoftsOrder()
    {
        var all = HotKeyModifiers.Shift | HotKeyModifiers.Alt | HotKeyModifiers.Control | HotKeyModifiers.Windows;

        Assert.Equal(
            [Strings.ShortcutsAndMouseButtonsKeyWindows, Strings.ShortcutsAndMouseButtonsKeyControl, Strings.ShortcutsAndMouseButtonsKeyAlt, Strings.ShortcutsAndMouseButtonsKeyShift],
            TriggerText.ModifierNames(all));
    }

    [Fact]
    public void ANamedKeyHasItsName()
    {
        var layout = KeyboardLayout.OfCurrentThread();

        Assert.Equal(Strings.ShortcutsAndMouseButtonsKeyControl + Plus + Strings.ShortcutsAndMouseButtonsKeyAlt + Plus + Strings.ShortcutsAndMouseButtonsKeyEscape, TriggerText.Of(HotKeyModifiers.Control | HotKeyModifiers.Alt, "Escape", layout));
        Assert.Equal(Strings.ShortcutsAndMouseButtonsKeyArrowLeft, TriggerText.KeyName("ArrowLeft", layout));
    }

    [Fact]
    public void AFunctionKeyIsShownAsItIs()
    {
        Assert.Equal("F13", TriggerText.Of(HotKeyModifiers.None, "F13", KeyboardLayout.OfCurrentThread()));
    }

    [Fact]
    public void AKeyThatTypesIsShownAsTheLayoutWritesIt()
    {
        var layout = KeyboardLayout.OfCurrentThread();
        var typed = KeyboardLayout.CharacterOf(KeyCodes.ScanCodeOf("Digit1")!.Value, layout);

        Assert.Equal(typed?.ToString() ?? "Digit1", TriggerText.KeyName("Digit1", layout));
    }

    [Fact]
    public void AMouseButtonHasItsName()
    {
        Assert.Equal(Strings.ShortcutsAndMouseButtonsKeyControl + Plus + Strings.ShortcutsAndMouseButtonsButtonBack, TriggerText.Of(HotKeyModifiers.Control, TriggerButton.Back));
        Assert.Equal(Strings.ShortcutsAndMouseButtonsKeyWindows + Plus + Strings.ShortcutsAndMouseButtonsButtonMiddle, TriggerText.Of(HotKeyModifiers.Windows, TriggerButton.Middle));
    }

    [Fact]
    public void ATriggerShowsItsShortcutOrButtonOrNothingWhenItHasNeither()
    {
        var layout = KeyboardLayout.OfCurrentThread();
        var action = new TriggerAction { Feature = "hideApps", Id = "hideApp" };

        Assert.Equal("F13", TriggerText.Of(new Trigger { Action = action, Shortcut = TriggerShortcut.For(HotKeyModifiers.None, "F13") }, layout));
        Assert.Equal(Strings.ShortcutsAndMouseButtonsButtonForward, TriggerText.Of(new Trigger { Action = action, MouseButton = TriggerMouseButton.For(TriggerButton.Forward, HotKeyModifiers.None) }, layout));
        Assert.Null(TriggerText.Of(new Trigger { Action = action }, layout));
        Assert.Null(TriggerText.Of(new Trigger { Action = action, Shortcut = new TriggerShortcut { Key = "F12" } }, layout));
    }

    [Fact]
    public void EveryNamedKeyHasAName()
    {
        string[] named = ["Escape", "Tab", "Space", "Enter", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "PrintScreen", "Pause", "ContextMenu"];

        Assert.All(named, key => Assert.False(string.IsNullOrWhiteSpace(TriggerText.KeyName(key, KeyboardLayout.OfCurrentThread()))));
        Assert.Equal(Strings.ShortcutsAndMouseButtonsKeyMenu, TriggerText.KeyName("ContextMenu", KeyboardLayout.OfCurrentThread()));
        Assert.Equal(named.Length, named.Select(key => TriggerText.KeyName(key, KeyboardLayout.OfCurrentThread())).Distinct().Count());
    }
}
