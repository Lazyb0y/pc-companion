using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Creates its own windows and shows one only while it is cloaked and without activating it, so
// nothing appears and focus stays where it is.
public sealed class TopLevelWindowsTests
{
    private const int ShowWithoutActivating = 4; // SW_SHOWNOACTIVATE
    private const uint MoveWithoutActivating = 0x0014; // SWP_NOZORDER | SWP_NOACTIVATE

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
    public void FindsAWindowWithItsPlaceAndStyles()
    {
        using var window = new MessageWindow(UniqueClassName());
        Show(window.Handle);
        Assert.True(SetWindowPos(window.Handle, 0, 10, 20, 400, 300, MoveWithoutActivating));

        WindowPlace? found = null;
        var decision = TopLevelWindows.FindFrontToBack(place =>
        {
            if (place.Handle != window.Handle)
            {
                return null;
            }

            found = place;
            return true;
        });

        Assert.True(decision);

        // Windows gives every overlapped window a title bar.
        Assert.Equal(
            new WindowPlace(
                window.Handle,
                new Rectangle(10, 20, 400, 300),
                IsCloaked: true,
                IsMinimized: false,
                IsClickThrough: false,
                IsNoActivate: false,
                IsToolWindow: false,
                IsTopmost: false,
                HasFrame: true,
                IsShell: false),
            found);
    }

    [Fact]
    public void FindingStopsAtTheFirstWindowThatDecides()
    {
        using var window = new MessageWindow(UniqueClassName());
        Show(window.Handle);
        var visited = 0;

        Assert.False(TopLevelWindows.FindFrontToBack(_ =>
        {
            visited++;
            return false;
        }));
        Assert.Equal(1, visited);
    }

    [Fact]
    public void FindingWithoutADecisionReturnsNull()
    {
        using var window = new MessageWindow(UniqueClassName());
        Show(window.Handle);

        Assert.Null(TopLevelWindows.FindFrontToBack(_ => null));
    }

    [Fact]
    public void TheDesktopBelongsToTheShell()
    {
        WindowPlace? desktop = null;
        var desktopHandle = TopLevelWindows.List().FirstOrDefault(listed => listed.IsDesktop)?.Handle;
        _ = TopLevelWindows.FindFrontToBack(place =>
        {
            if (place.Handle != desktopHandle)
            {
                return null;
            }

            desktop = place;
            return true;
        });

        // Without a shell, as on some build servers, there's no desktop window.
        Assert.True(desktopHandle is null || desktop is { IsShell: true });
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
    public void SendToBackDoesNotWaitForTheWindowsThread()
    {
        // The window's thread handles no messages until it's released or 30 seconds pass, like a
        // program that stopped responding.
        using var shown = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var responding = new ManualResetEventSlim();
        nint handle = 0;
        var thread = new Thread(() =>
        {
            using var window = new MessageWindow(UniqueClassName());
            Show(window.Handle);
            handle = window.Handle;
            shown.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
            responding.Set();
        });
        thread.Start();

        try
        {
            Assert.True(shown.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            Assert.True(TopLevelWindows.SendToBack(handle));
            Assert.False(responding.IsSet);
        }
        finally
        {
            release.Set();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
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

    [Fact]
    public void ReadsAWindowsTitleAndClassName()
    {
        var className = UniqueClassName();
        using var window = new MessageWindow(className);
        Assert.True(SetWindowText(window.Handle, "Spec & notes"));

        Assert.Equal("Spec & notes", TopLevelWindows.Title(window.Handle));
        Assert.Equal(className, TopLevelWindows.ClassName(window.Handle));
    }

    [Fact]
    public void AWindowWithoutATitleHasAnEmptyOne()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Equal(string.Empty, TopLevelWindows.Title(window.Handle));
    }

    [Fact]
    public void ADisabledWindowIsNotEnabled()
    {
        using var window = new MessageWindow(UniqueClassName());
        Assert.True(TopLevelWindows.IsEnabled(window.Handle));

        _ = EnableWindow(window.Handle, false);

        Assert.False(TopLevelWindows.IsEnabled(window.Handle));
    }

    [Fact]
    public void AWindowExistsUntilItIsDestroyed()
    {
        var window = new MessageWindow(UniqueClassName());
        var handle = window.Handle;
        Assert.True(TopLevelWindows.Exists(handle));

        window.Dispose();

        Assert.False(TopLevelWindows.Exists(handle));
        Assert.Equal(string.Empty, TopLevelWindows.ClassName(handle));
    }

    [Fact]
    public void HideTakesAWindowOutOfSightWithoutActivatingAnything()
    {
        using var window = new MessageWindow(UniqueClassName());
        Show(window.Handle);
        var front = Foreground.Window;
        Assert.True(TopLevelWindows.IsVisible(window.Handle));

        // Windows passes the request to the window's thread as an event that its message loop handles.
        Assert.True(TopLevelWindows.Hide(window.Handle));
        RunFor(window, TimeSpan.FromMilliseconds(200));

        Assert.False(TopLevelWindows.IsVisible(window.Handle));
        Assert.Equal(front, Foreground.Window);
    }

    [Fact]
    public void ShowMinimizedShowsAHiddenWindowMinimizedWithoutActivatingIt()
    {
        using var window = new MessageWindow(UniqueClassName());
        Assert.True(WindowCloak.Set(window.Handle, cloaked: true));
        var front = Foreground.Window;

        Assert.True(TopLevelWindows.ShowMinimized(window.Handle));
        RunFor(window, TimeSpan.FromMilliseconds(200));

        Assert.True(TopLevelWindows.IsVisible(window.Handle));
        Assert.True(IsIconic(window.Handle));
        Assert.Equal(front, Foreground.Window);
    }

    [Fact]
    public void HideDoesNotWaitForTheWindowsThread()
    {
        // The window's thread handles no messages until it's released or 30 seconds pass, like a
        // program that stopped responding; then it hides the window.
        using var shown = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var responding = new ManualResetEventSlim();
        nint handle = 0;
        var hidden = false;
        var thread = new Thread(() =>
        {
            using var window = new MessageWindow(UniqueClassName());
            Show(window.Handle);
            handle = window.Handle;
            shown.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
            responding.Set();
            RunFor(window, TimeSpan.FromMilliseconds(200));
            hidden = !TopLevelWindows.IsVisible(window.Handle);
        });
        thread.Start();

        try
        {
            Assert.True(shown.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            Assert.True(TopLevelWindows.Hide(handle));
            Assert.False(responding.IsSet);
        }
        finally
        {
            release.Set();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        }

        Assert.True(hidden);
    }

    // Cloaked first, so it never appears.
    private static void Show(nint window)
    {
        Assert.True(WindowCloak.Set(window, cloaked: true));
        ShowWindow(window, ShowWithoutActivating);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(nint window, bool enable);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
