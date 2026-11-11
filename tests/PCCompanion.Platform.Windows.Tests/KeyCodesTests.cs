using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class KeyCodesTests
{
    [Fact]
    public void EachKeyHasAScanCodeOfItsOwn()
    {
        var scanCodes = KeyCodes.All.Select(code => KeyCodes.ScanCodeOf(code)).ToList();

        Assert.DoesNotContain(null, scanCodes);
        Assert.Equal(scanCodes.Count, scanCodes.Distinct().Count());
        Assert.All(KeyCodes.All, code => Assert.Equal(code, KeyCodes.NameOf(KeyCodes.ScanCodeOf(code)!.Value)));
    }

    [Theory]
    [InlineData("KeyH", 0x23u)]
    [InlineData("Digit1", 0x02u)]
    [InlineData("Backquote", 0x29u)]
    [InlineData("F13", 0x64u)]
    [InlineData("F24", 0x76u)]
    [InlineData("ArrowLeft", 0xE04Bu)]
    [InlineData("Pause", 0xE11Du)]
    public void KeysAreFoundByTheirPosition(string code, uint scanCode)
    {
        Assert.Equal(scanCode, KeyCodes.ScanCodeOf(code));
    }

    [Theory]
    [InlineData("F12")]
    [InlineData("Numpad1")]
    [InlineData("ShiftLeft")]
    [InlineData("CapsLock")]
    [InlineData("MediaPlayPause")]
    [InlineData("keyh")]
    [InlineData("")]
    public void KeysAShortcutCantUseAreNone(string code)
    {
        Assert.Null(KeyCodes.ScanCodeOf(code));
    }

    [Theory]
    [InlineData("F13", true)]
    [InlineData("F24", true)]
    [InlineData("F1", false)]
    [InlineData("F11", false)]
    [InlineData("KeyF", false)]
    [InlineData("Escape", false)]
    public void OnlyF13ToF24StandAlone(string code, bool alone)
    {
        Assert.Equal(alone, KeyCodes.StandsAlone(code));
    }

    // Keys that mean the same virtual key in every layout, including the extended ones and Pause.
    [Theory]
    [InlineData("Escape", 0x1Bu)]
    [InlineData("F13", 0x7Cu)]
    [InlineData("F24", 0x87u)]
    [InlineData("Insert", 0x2Du)]
    [InlineData("Delete", 0x2Eu)]
    [InlineData("Home", 0x24u)]
    [InlineData("End", 0x23u)]
    [InlineData("PageUp", 0x21u)]
    [InlineData("PageDown", 0x22u)]
    [InlineData("ArrowLeft", 0x25u)]
    [InlineData("ArrowUp", 0x26u)]
    [InlineData("ArrowRight", 0x27u)]
    [InlineData("ArrowDown", 0x28u)]
    [InlineData("PrintScreen", 0x2Cu)]
    [InlineData("Pause", 0x13u)]
    [InlineData("ContextMenu", 0x5Du)]
    public void NamedKeysMeanTheirVirtualKeyInEveryLayout(string code, uint virtualKey)
    {
        var scanCode = KeyCodes.ScanCodeOf(code)!.Value;

        Assert.All(KeyboardLayout.Installed(), layout => Assert.Equal(virtualKey, KeyboardLayout.VirtualKeyOf(scanCode, layout)));
    }
}
