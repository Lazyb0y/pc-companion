using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Creates its own windows and shows one only while it is cloaked and without activating it, so
// nothing appears and focus stays where it is.
public sealed class TopLevelWindowsTests
{
    private const int ShowWithoutActivating = 4; // SW_SHOWNOACTIVATE

    [Fact]
    public void ListsAVisibleWindowWithWhatItIsLike()
    {
        using var window = new MessageWindow(UniqueClassName());
        Show(window.Handle);

        var listed = Assert.Single(TopLevelWindows.List(), listed => listed.Handle == window.Handle);

        Assert.Equal(
            new TopLevelWindow(
                window.Handle,
                (uint)Environment.ProcessId,
                IsMinimized: false,
                IsCloaked: true,
                IsToolWindow: false,
                IsAppWindow: false,
                IsNoActivate: false,
                IsTopmost: false,
                HasOwner: false,
                IsDesktop: false),
            listed);
    }

    [Fact]
    public void SendToBackPutsAWindowBehindTheOthersButInFrontOfTheDesktop()
    {
        using var back = new MessageWindow(UniqueClassName());
        using var front = new MessageWindow(UniqueClassName());
        Show(back.Handle);
        Show(front.Handle);
        Assert.True(TopLevelWindows.SendToBack(front.Handle));

        var order = TopLevelWindows.List().Select(listed => listed.Handle).ToList();
        Assert.True(order.IndexOf(front.Handle) > order.IndexOf(back.Handle));
        if (TopLevelWindows.List().FirstOrDefault(listed => listed.IsDesktop) is { } desktop)
        {
            Assert.True(order.IndexOf(front.Handle) < order.IndexOf(desktop.Handle));
        }
    }

    [Fact]
    public void HiddenWindowsAreNotListed()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.DoesNotContain(TopLevelWindows.List(), listed => listed.Handle == window.Handle);
    }

    [Fact]
    public void AtMostOneWindowIsTheDesktop()
    {
        Assert.True(TopLevelWindows.List().Count(listed => listed.IsDesktop) <= 1);
    }

    [Fact]
    public void AWindowThatNothingOwnsIsItsOwnRootOwner()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Equal(window.Handle, TopLevelWindows.RootOwner(window.Handle));
    }

    private static string UniqueClassName() => "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    // Cloaked first, so it never appears.
    private static void Show(nint window)
    {
        Assert.True(WindowCloak.Set(window, cloaked: true));
        ShowWindow(window, ShowWithoutActivating);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
}
