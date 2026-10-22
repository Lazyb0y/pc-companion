using System;
using System.Collections.Generic;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// No menu opens: a menu takes the keyboard and the front until it's closed.
public sealed class PopupMenuTests
{
    private const uint KeyDown = 0x0100; // WM_KEYDOWN
    private const uint SystemKeyDown = 0x0104; // WM_SYSKEYDOWN
    private const uint SystemKeyUp = 0x0105; // WM_SYSKEYUP
    private const uint TestMessage = 0x8001; // WM_APP + 1

    [Theory]
    [InlineData("Track the parcel", "Track the parcel")]
    [InlineData("Search & copy", "Search && copy")]
    [InlineData("&&", "&&&&")]
    public void EscapeShowsAmpersandsAsWritten(string text, string expected)
    {
        Assert.Equal(expected, PopupMenu.Escape(text));
    }

    [Fact]
    public void ASeparatorHasNoIdOrText()
    {
        Assert.True(PopupMenuItem.Separator.IsSeparator);
        Assert.Equal(0u, PopupMenuItem.Separator.Id);
        Assert.Empty(PopupMenuItem.Separator.Text);
        Assert.False(new PopupMenuItem(1, "Settings").IsSeparator);
        Assert.True(new PopupMenuItem(1, "Settings").IsEnabled);
        Assert.False(new PopupMenuItem(1, "Settings").IsChecked);
        Assert.Null(new PopupMenuItem(1, "Settings").Items);
    }

    [Fact]
    public void ASubmenuHoldsItsItemsAndHasNoId()
    {
        PopupMenuItem[] profiles = [new(10, "Office") { IsChecked = true }, new(11, "Home")];

        var submenu = PopupMenuItem.Submenu("Network profile", profiles);

        Assert.Equal(0u, submenu.Id);
        Assert.Equal("Network profile", submenu.Text);
        Assert.Same(profiles, submenu.Items);
        Assert.False(submenu.IsSeparator);
        Assert.True(submenu.Items![0].IsChecked);
    }

    [Fact]
    public void ClosingWithoutAnOpenMenuDoesNothing()
    {
        PopupMenu.CloseOpenMenu();
    }

    [Fact]
    public void DropWaitingKeysDropsOnlyKeyboardMessages()
    {
        using var window = new MessageWindow("PCCompanion.Tests." + Guid.NewGuid().ToString("N"));
        var received = new List<uint>();
        window.MessageReceived += (_, e) => received.Add(e.Message);

        window.Post(KeyDown, 0x56);
        window.Post(SystemKeyDown, 0x12);
        window.Post(TestMessage);
        window.Post(SystemKeyUp, 0x12);

        Assert.Equal(3, PopupMenu.DropWaitingKeys());
        MessageLoop.Quit();
        MessageLoop.Run();

        Assert.Contains(TestMessage, received);
        Assert.DoesNotContain(KeyDown, received);
        Assert.DoesNotContain(SystemKeyDown, received);
        Assert.DoesNotContain(SystemKeyUp, received);
    }
}
