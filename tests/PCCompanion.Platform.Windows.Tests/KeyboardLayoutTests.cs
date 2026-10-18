using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the user's layouts without loading or switching any.
public sealed class KeyboardLayoutTests
{
    [Fact]
    public void TheUserHasALayout()
    {
        Assert.NotEmpty(KeyboardLayout.Installed());
    }

    [Theory]
    [InlineData(0x01u, 0x1Bu)] // Esc
    [InlineData(0x0Fu, 0x09u)] // Tab
    [InlineData(0x1Cu, 0x0Du)] // Enter
    public void KeysThatAreTheSameInEveryLayoutMapToTheirVirtualKey(uint scanCode, uint virtualKey)
    {
        Assert.All(KeyboardLayout.Installed(), layout => Assert.Equal(virtualKey, KeyboardLayout.VirtualKeyOf(scanCode, layout)));
    }

    [Fact]
    public void AScanCodeOfNoKeyMapsToNone()
    {
        Assert.All(KeyboardLayout.Installed(), layout => Assert.Null(KeyboardLayout.VirtualKeyOf(0, layout)));
    }
}
