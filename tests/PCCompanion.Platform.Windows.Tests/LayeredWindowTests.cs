using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own windows with unique class names. Shown windows are fully transparent
// and far off the screen, and pointer input is posted to them, so nothing shows or moves.
public sealed class LayeredWindowTests
{
    private const uint MouseMove = 0x0200;
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private static readonly Rectangle OffScreen = new(-30000, -30000, 4, 2);

    [Fact]
    public void ShowAndHideTrackWhetherTheWindowIsShown()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");

        Assert.False(window.IsShown);
        window.Show(new uint[8], OffScreen);
        Assert.True(window.IsShown);
        Assert.True(IsWindowVisible(window.Handle));
        window.Hide();
        Assert.False(window.IsShown);
        Assert.False(IsWindowVisible(window.Handle));
    }

    [Fact]
    public void PixelsMustFillTheBounds()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");

        Assert.Throws<ArgumentOutOfRangeException>(() => window.Show(new uint[7], OffScreen));
    }

    [Fact]
    public void TheWindowIsATopmostToolWindowThatDoesNotTakeTheFocus()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");

        var style = (long)GetWindowLongPtr(window.Handle, -20); // GWL_EXSTYLE
        Assert.Equal(0x08080088L, style & 0x08080088L); // WS_EX_NOACTIVATE, WS_EX_LAYERED, WS_EX_TOOLWINDOW, WS_EX_TOPMOST
        Assert.True(TopLevelWindows.IsTopmost(window.Handle));
        Assert.False(TopLevelWindows.MayHaveTaskbarButton(window.Handle));
    }

    [Fact]
    public void TheTitleIsTheWindowsName()
    {
        using var window = new LayeredWindow(UniqueClassName(), "First");

        window.SetTitle("CPU: 42%, Memory: 61%");

        var text = new char[64];
        var length = GetWindowText(window.Handle, text, text.Length);
        Assert.Equal("CPU: 42%, Memory: 61%", new string(text, 0, length));
    }

    [Fact]
    public void TheToolTipCanBeSetAndChanged()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");

        window.SetToolTip("CPU: 42%");
        window.SetToolTip("CPU: 43%\nMemory: 61%");
        window.SetToolTip("CPU: 43%\nMemory: 61%");
    }

    [Fact]
    public void PointerInputRaisesTheWindowsEvents()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        window.Show(new uint[8], OffScreen);
        var over = new List<bool>();
        int clicks = 0, menus = 0;
        window.PointerOverChanged += (_, _) => over.Add(window.IsPointerOver);
        window.Clicked += (_, _) => clicks++;
        window.MenuRequested += (_, _) => menus++;

        PostMessage(window.Handle, MouseMove, 0, 0);
        PostMessage(window.Handle, LeftButtonUp, 0, 0);
        PostMessage(window.Handle, RightButtonUp, 0, 0);
        RunPostedMessages();

        // The pointer isn't really there, so Windows reports at once that it left.
        Assert.True(over[0]);
        Assert.Equal((1, 1), (clicks, menus));
    }

    [Fact]
    public void KeepInFrontOfRaisesTheWindowOnlyWhenTheOtherIsInFront()
    {
        using var strip = new LayeredWindow(UniqueClassName(), "Strip");
        using var other = new LayeredWindow(UniqueClassName(), "Other");
        strip.Show(new uint[8], OffScreen);
        other.Show(new uint[8], OffScreen);

        // The window shown last is in front.
        Assert.True(TopLevelWindows.IsInFrontOf(other.Handle, strip.Handle));
        Assert.True(strip.KeepInFrontOf(other.Handle));
        Assert.True(TopLevelWindows.IsInFrontOf(strip.Handle, other.Handle));
        Assert.False(strip.KeepInFrontOf(other.Handle));
    }

    [Fact]
    public void AHiddenWindowStaysWhereItIs()
    {
        using var strip = new LayeredWindow(UniqueClassName(), "Strip");
        using var other = new LayeredWindow(UniqueClassName(), "Other");
        other.Show(new uint[8], OffScreen);

        Assert.False(strip.KeepInFrontOf(other.Handle));
    }

    [Fact]
    public void DisposeTwiceIsFine()
    {
        var window = new LayeredWindow(UniqueClassName(), "Test");

        window.Dispose();
        window.Dispose();

        Assert.False(window.IsShown);
    }

    [Fact]
    public void AWinEventHookReportsThisProcesssWindowsBeingShown()
    {
        using var hook = new WinEventHook(WinEventHook.ObjectShowEvent, WinEventHook.ObjectHideEvent, (uint)Environment.ProcessId);
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        var shown = new List<nint>();
        hook.Raised += (_, e) =>
        {
            if (e.IsAboutWindow && e.EventType == WinEventHook.ObjectShowEvent)
            {
                shown.Add(e.Window);
            }
        };

        window.Show(new uint[8], OffScreen);
        using var messages = new MessageWindow(UniqueClassName());
        RunFor(messages, TimeSpan.FromMilliseconds(500));

        Assert.Contains(window.Handle, shown);
    }

    [Fact]
    public void AWinEventHookNeedsEventsInOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WinEventHook(WinEventHook.ObjectHideEvent, WinEventHook.ObjectShowEvent));
    }

    [Fact]
    public void AWinEventHookCanBeDisposedTwiceAndCreatedAgain()
    {
        var hook = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
        hook.Dispose();
        hook.Dispose();

        using var again = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] text, int maximumLength);
}
