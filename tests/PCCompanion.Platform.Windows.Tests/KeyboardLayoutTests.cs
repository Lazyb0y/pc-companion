using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the user's layouts without loading or switching any. Requests go only to a hidden window of
// the test's own, which handles them itself, so its thread's layout doesn't change either.
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
        Assert.All(KeyboardLayout.Installed(), layout =>
        {
            Assert.Null(KeyboardLayout.VirtualKeyOf(0, layout));
            Assert.Null(KeyboardLayout.CharacterOf(0, layout));
        });
    }

    [Fact]
    public void TheSpaceBarTypesASpaceInEveryLayout()
    {
        Assert.All(KeyboardLayout.Installed(), layout => Assert.Equal(' ', KeyboardLayout.CharacterOf(0x39, layout)));
    }

    [Fact]
    public void ThisThreadHasOneOfTheUsersLayouts()
    {
        Assert.Contains(KeyboardLayout.OfCurrentThread(), KeyboardLayout.Installed());
    }

    [Fact]
    public void TheWindowInFrontTypesInItsFocusThreadsLayout()
    {
        if (KeyboardLayout.FocusInFront() is { } focus)
        {
            Assert.NotEqual(0, focus.Window);
            Assert.NotEqual(0u, focus.ProcessId);
            Assert.Equal(KeyboardLayout.OfThread(focus.Thread), KeyboardLayout.OfForegroundWindow());
        }
    }

    [Fact]
    public void NoThreadHasNoLayout()
    {
        Assert.Null(KeyboardLayout.OfThread(0));
    }

    [Fact]
    public void ASwitchRequestReachesTheWindowWithTheLayout()
    {
        using var window = new MessageWindow(UniqueClassName());
        var layout = KeyboardLayout.OfCurrentThread();
        nint received = 0;
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == 0x0050) // WM_INPUTLANGCHANGEREQUEST
            {
                received = e.LParam;
                e.Handled = true;
            }
        };

        Assert.Equal(LayoutRequest.Posted, KeyboardLayout.RequestSwitch(window.Handle, layout));
        RunPostedMessages();

        Assert.Equal(layout, received);
        Assert.Equal(layout, KeyboardLayout.OfCurrentThread());
    }

    [Fact]
    public void ASwitchRequestToAClosedWindowFails()
    {
        nint closed;
        using (var window = new MessageWindow(UniqueClassName()))
        {
            closed = window.Handle;
        }

        Assert.Equal(LayoutRequest.Failed, KeyboardLayout.RequestSwitch(closed, KeyboardLayout.OfCurrentThread()));
    }
}
