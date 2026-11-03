using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Changes only hidden windows the tests create.
public sealed class LayeredStyleTests
{
    [Fact]
    public void AWindowGetsTheLayeredStyleAndLosesIt()
    {
        using var window = new MessageWindow(UniqueClassName());
        Assert.False(LayeredStyle.IsLayered(window.Handle));

        Assert.True(LayeredStyle.SetLayered(window.Handle, true));
        Assert.True(LayeredStyle.IsLayered(window.Handle));
        Assert.True(LayeredStyle.SetLayered(window.Handle, true));

        Assert.True(LayeredStyle.SetLayered(window.Handle, false));
        Assert.False(LayeredStyle.IsLayered(window.Handle));
    }

    [Fact]
    public void ALayeredWindowsAttributesAreReadBack()
    {
        using var window = new MessageWindow(UniqueClassName());
        Assert.Null(LayeredStyle.Read(window.Handle));
        LayeredStyle.SetLayered(window.Handle, true);
        var attributes = new LayeredAttributes(0x00FF00, 128, LayeredEffects.Alpha | LayeredEffects.ColorKey);

        Assert.True(LayeredStyle.Set(window.Handle, attributes));

        Assert.Equal(attributes, LayeredStyle.Read(window.Handle));
    }

    [Fact]
    public void AWindowWithoutTheLayeredStyleTakesNoAttributes()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.False(LayeredStyle.Set(window.Handle, new LayeredAttributes(0, 128, LayeredEffects.Alpha)));
        Assert.Null(LayeredStyle.Read(window.Handle));
    }
}
