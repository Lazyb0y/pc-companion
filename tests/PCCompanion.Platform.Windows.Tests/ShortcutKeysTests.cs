using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Uses the user's own keyboard layout, and checks only what's the same in every layout.
public sealed class ShortcutKeysTests
{
    [Fact]
    public void AKeyByPositionStaysWhereItIs()
    {
        var keys = ShortcutKeys.OfPosition(new KeyPositionShortcut(HotKeyModifiers.Alt, ShortcutKeeper.KeyAboveTab));

        Assert.Equal(new KeyPositionShortcut(HotKeyModifiers.Alt, ShortcutKeeper.KeyAboveTab), keys.InLayout(KeyboardLayout.OfCurrentThread()));
    }

    [Fact]
    public void AVirtualKeyIsWhereTheLayoutHasIt()
    {
        var layout = KeyboardLayout.OfCurrentThread();

        var position = ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows, 0x48).InLayout(layout);

        Assert.Equal(HotKeyModifiers.Windows, position?.Modifiers);
        Assert.Equal(0x48u, KeyboardLayout.VirtualKeyOf(position!.Value.ScanCode, layout));
    }

    [Fact]
    public void EscIsWhereItIsInEveryLayout()
    {
        Assert.Equal(KeyCodes.ScanCodeOf("Escape"), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Control, 0x1B).InLayout(KeyboardLayout.OfCurrentThread())?.ScanCode);
    }

    [Fact]
    public void AVirtualKeyThatNoKeyMeansHasNoPosition()
    {
        // 0xE8 is unassigned.
        Assert.Null(ShortcutKeys.OfVirtualKey(HotKeyModifiers.Control, 0xE8).InLayout(KeyboardLayout.OfCurrentThread()));
    }
}
