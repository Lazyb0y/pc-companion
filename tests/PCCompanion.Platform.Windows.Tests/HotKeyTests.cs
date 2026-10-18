using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Registers Ctrl + Alt + Shift + F24, which keyboards don't have, for its own hidden windows, and
// posts the shortcut's message to those windows only; nothing presses keys.
public sealed class HotKeyTests
{
    private const HotKeyModifiers Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift;
    private const uint F24 = 0x87;
    private const uint HotKeyMessage = 0x0312; // WM_HOTKEY

    [Fact]
    public void ReportsItsOwnShortcutOnly()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var hotKey = HotKey.Register(window, Modifiers, F24);
        Assert.NotNull(hotKey);
        var count = 0;
        hotKey.Pressed += (_, _) => count++;

        window.Post(HotKeyMessage, (nuint)hotKey.Id);
        window.Post(HotKeyMessage, (nuint)hotKey.Id + 1);
        RunPostedMessages();

        Assert.Equal(1, count);
    }

    [Fact]
    public void AShortcutThatIsTakenIsRefused()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var other = new MessageWindow(UniqueClassName());
        using var hotKey = HotKey.Register(window, Modifiers, F24);
        Assert.NotNull(hotKey);

        Assert.Null(HotKey.Register(other, Modifiers, F24));
    }

    [Fact]
    public void ADisposedShortcutReportsNothingAndCanBeRegisteredAgain()
    {
        using var window = new MessageWindow(UniqueClassName());
        var hotKey = HotKey.Register(window, Modifiers, F24);
        Assert.NotNull(hotKey);
        var count = 0;
        hotKey.Pressed += (_, _) => count++;

        hotKey.Dispose();
        hotKey.Dispose();
        window.Post(HotKeyMessage, (nuint)hotKey.Id);
        RunPostedMessages();
        using var again = HotKey.Register(window, Modifiers, F24);

        Assert.Equal(0, count);
        Assert.NotNull(again);
        Assert.NotEqual(hotKey.Id, again.Id);
    }

    private static string UniqueClassName() => "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    // Messages posted before Quit are delivered before the loop ends.
    private static void RunPostedMessages()
    {
        MessageLoop.Quit();
        MessageLoop.Run();
    }
}
