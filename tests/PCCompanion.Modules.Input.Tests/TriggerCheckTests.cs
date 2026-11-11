using System.Linq;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class TriggerCheckTests
{
    private const HotKeyModifiers ControlAlt = HotKeyModifiers.Control | HotKeyModifiers.Alt;

    private static readonly TriggerAction HideOthers = new() { Feature = "hideApps", Id = "hideOthers" };

    [Fact]
    public void AShortcutOrAMouseButtonWithAnActionCanBeUsed()
    {
        Assert.Equal(TriggerProblem.None, TriggerCheck.Check(Shortcut(ControlAlt, "KeyH")));
        Assert.Equal(TriggerProblem.None, TriggerCheck.Check(Button(TriggerButton.Back, HotKeyModifiers.None)));
        Assert.Equal(TriggerProblem.None, TriggerCheck.Check(Button(TriggerButton.Middle, HotKeyModifiers.Windows | HotKeyModifiers.Shift)));
    }

    [Fact]
    public void ATriggerWithoutAnActionCantBeUsed()
    {
        Assert.Equal(TriggerProblem.NoAction, TriggerCheck.Check(Shortcut(ControlAlt, "KeyH") with { Action = new TriggerAction { Feature = "hideApps" } }));
    }

    [Fact]
    public void ATriggerNeedsExactlyOneShortcutOrMouseButton()
    {
        Assert.Equal(TriggerProblem.NoInput, TriggerCheck.Check(new Trigger { Action = HideOthers }));
        Assert.Equal(TriggerProblem.BothInputs, TriggerCheck.Check(Shortcut(ControlAlt, "KeyH") with { MouseButton = TriggerMouseButton.For(TriggerButton.Back, HotKeyModifiers.None) }));
    }

    [Theory]
    [InlineData(HotKeyModifiers.None, "KeyH", TriggerProblem.NeedsModifier)]
    [InlineData(HotKeyModifiers.Shift, "KeyH", TriggerProblem.NeedsModifier)]
    [InlineData(ControlAlt, "F12", TriggerProblem.UnknownKey)]
    [InlineData(HotKeyModifiers.Windows, "KeyL", TriggerProblem.Reserved)]
    [InlineData(HotKeyModifiers.None, "F13", TriggerProblem.None)]
    public void AShortcutFollowsTheRules(HotKeyModifiers modifiers, string key, TriggerProblem problem)
    {
        Assert.Equal(problem, TriggerCheck.Check(Shortcut(modifiers, key)));
    }

    [Fact]
    public void AnUnknownModifierOrButtonCantBeUsed()
    {
        Assert.Equal(TriggerProblem.UnknownModifier, TriggerCheck.Check(new Trigger { Action = HideOthers, Shortcut = new TriggerShortcut { Modifiers = ["Hyper"], Key = "KeyH" } }));
        Assert.Equal(TriggerProblem.UnknownButton, TriggerCheck.Check(new Trigger { Action = HideOthers, MouseButton = new TriggerMouseButton { Button = (TriggerButton)7 } }));
    }

    [Fact]
    public void ModifierNamesIgnoreCase()
    {
        Assert.Equal(TriggerProblem.None, TriggerCheck.Check(new Trigger { Action = HideOthers, Shortcut = new TriggerShortcut { Modifiers = ["control", "ALT"], Key = "KeyH" } }));
    }

    [Theory]
    [InlineData(HotKeyModifiers.None)]
    [InlineData(HotKeyModifiers.Control)]
    [InlineData(HotKeyModifiers.Shift)]
    public void TheMiddleButtonNeedsTheWindowsKey(HotKeyModifiers modifiers)
    {
        Assert.Equal(TriggerProblem.MiddleNeedsWindowsKey, TriggerCheck.Check(Button(TriggerButton.Middle, modifiers)));
    }

    [Fact]
    public void ALaterTriggerWithTheSameShortcutOrButtonCantBeUsed()
    {
        var checkedTriggers = TriggerCheck.CheckAll(
        [
            Shortcut(HotKeyModifiers.Windows, "KeyF13"),
            Shortcut(ControlAlt, "KeyH"),
            Shortcut(ControlAlt, "KeyH") with { Action = new TriggerAction { Feature = "endApp", Id = "chooseApp" } },
            Button(TriggerButton.Back, HotKeyModifiers.Alt),
            Button(TriggerButton.Back, HotKeyModifiers.Alt),
            Button(TriggerButton.Back, HotKeyModifiers.None),
        ]);

        Assert.Equal(
            [TriggerProblem.UnknownKey, TriggerProblem.None, TriggerProblem.SameAsEarlier, TriggerProblem.None, TriggerProblem.SameAsEarlier, TriggerProblem.None],
            [.. checkedTriggers.Select(trigger => trigger.Problem)]);
        Assert.Equal([0, 1, 2, 3, 4, 5], [.. checkedTriggers.Select(trigger => trigger.Index)]);
    }

    [Fact]
    public void AnUnusableTriggerDoesntKeepTheSameKeysFromALaterOne()
    {
        var checkedTriggers = TriggerCheck.CheckAll(
        [
            Shortcut(ControlAlt, "KeyH") with { Action = new TriggerAction() },
            Shortcut(ControlAlt, "KeyH"),
        ]);

        Assert.Equal(TriggerProblem.None, checkedTriggers[1].Problem);
    }

    private static Trigger Shortcut(HotKeyModifiers modifiers, string key) => new() { Action = HideOthers, Shortcut = TriggerShortcut.For(modifiers, key) };

    private static Trigger Button(TriggerButton button, HotKeyModifiers modifiers) => new() { Action = HideOthers, MouseButton = TriggerMouseButton.For(button, modifiers) };
}
