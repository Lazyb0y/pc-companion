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
    private const uint LeftButtonDown = 0x0201;
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
    public void OnlyAClickThroughWindowLetsClicksThroughAllOfIt()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        using var notice = new LayeredWindow(UniqueClassName(), "Notice", clickThrough: true);

        const long Transparent = 0x20; // WS_EX_TRANSPARENT
        Assert.Equal(0L, (long)GetWindowLongPtr(window.Handle, -20) & Transparent);
        Assert.Equal(Transparent, (long)GetWindowLongPtr(notice.Handle, -20) & Transparent);
        Assert.True(TopLevelWindows.IsTopmost(notice.Handle));
    }

    [Fact]
    public void OpacityChangesWhileShownAndIsKeptWhileHidden()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        Assert.Equal(255, window.Opacity);

        window.SetOpacity(0);
        window.Show(new uint[8], OffScreen);
        Assert.Equal(0, window.Opacity);

        foreach (var step in new byte[] { 64, 128, 255 })
        {
            window.SetOpacity(step);
            Assert.Equal(step, window.Opacity);
        }

        window.Hide();
        window.SetOpacity(32);
        window.Show(new uint[8], OffScreen);
        Assert.Equal(32, window.Opacity);
    }

    [Fact]
    public void ADisposedWindowsOpacityCantChange()
    {
        var window = new LayeredWindow(UniqueClassName(), "Test");
        window.Dispose();

        Assert.Throws<ObjectDisposedException>(() => window.SetOpacity(128));
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
    public void TheToolTipCanReadRightToLeftAndBack()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");

        window.SetToolTip("Memory: 61%", rightToLeft: true);
        window.SetToolTip("Memory: 62%", rightToLeft: true);
        window.SetToolTip("Memory: 62%");
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

    [Fact]
    public void AWindowThatDoesntStayOnTopIsStillAToolWindowThatDoesNotTakeTheFocus()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test", topmost: false);

        var style = (long)GetWindowLongPtr(window.Handle, -20); // GWL_EXSTYLE
        Assert.Equal(0x08080080L, style & 0x08080088L); // WS_EX_NOACTIVATE, WS_EX_LAYERED, WS_EX_TOOLWINDOW, not WS_EX_TOPMOST
        Assert.False(TopLevelWindows.MayHaveTaskbarButton(window.Handle));
    }

    [Fact]
    public void PlaceAboveTakesAPlaceRightAboveTheWindowAndBelowTheOnesInFront()
    {
        using var strip = new LayeredWindow(UniqueClassName(), "Strip", topmost: false);
        using var below = new LayeredWindow(UniqueClassName(), "Below", topmost: false);
        using var front = new LayeredWindow(UniqueClassName(), "Front", topmost: false);
        strip.Show(new uint[8], OffScreen);
        below.Show(new uint[8], OffScreen);
        front.Show(new uint[8], OffScreen);

        Assert.True(strip.PlaceAbove(below.Handle));
        Assert.True(TopLevelWindows.IsInFrontOf(strip.Handle, below.Handle));
        Assert.True(TopLevelWindows.IsInFrontOf(front.Handle, strip.Handle));
        Assert.True(strip.PlaceAbove(below.Handle));
    }

    [Fact]
    public void SetTopmostPutsTheWindowOnTopAndTakesItOff()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test", topmost: false);
        window.Show(new uint[8], OffScreen);

        // Windows puts a window on top only while its process may bring windows to the front, and
        // otherwise reports success and leaves it, as the topmost test of TopLevelWindows says.
        Assert.True(window.SetTopmost(true));
        if (!TopLevelWindows.IsTopmost(window.Handle))
        {
            Assert.Skip("Windows doesn't let this process put windows on top now, as while the user works in another app.");
        }

        Assert.True(window.SetTopmost(false));
        Assert.False(TopLevelWindows.IsTopmost(window.Handle));
    }

    [Fact]
    public void PointerInputReportsEachActionWithItsPosition()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        window.Show(new uint[8], OffScreen);
        var input = new List<(WindowPointerAction, Point)>();
        window.PointerInput += (_, e) => input.Add((e.Action, e.Position));

        PostMessage(window.Handle, LeftButtonDown, 0, Position(3, 1));
        PostMessage(window.Handle, MouseMove, 0, Position(-2, 5));
        PostMessage(window.Handle, LeftButtonUp, 0, Position(7, 0));
        PostMessage(window.Handle, RightButtonUp, 0, Position(1, 1));
        RunPostedMessages();

        Assert.Equal(
            [
                (WindowPointerAction.LeftDown, new Point(3, 1)),
                (WindowPointerAction.Moved, new Point(-2, 5)),
                (WindowPointerAction.LeftUp, new Point(7, 0)),
                (WindowPointerAction.RightUp, new Point(1, 1)),
            ],
            input);
    }

    [Fact]
    public void ReleasingThePointerReportsTheCaptureLost()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        window.Show(new uint[8], OffScreen);
        var actions = new List<WindowPointerAction>();
        window.PointerInput += (_, e) => actions.Add(e.Action);

        window.CapturePointer();
        Assert.Equal(window.Handle, GetCapture());
        window.ReleasePointer();

        Assert.NotEqual(window.Handle, GetCapture());
        Assert.Equal([WindowPointerAction.CaptureLost], actions);
    }

    [Fact]
    public void AHandledMessageReturnsTheHandlersResult()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Test");
        const uint Question = 0x0400 + 7; // WM_USER + 7
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == Question)
            {
                e.Handled = true;
                e.Result = 42;
            }
        };

        Assert.Equal(42, SendMessage(window.Handle, Question, 0, 0));
    }

    // A mouse message's position: x in the low 16 bits and y in the high 16 bits, each signed.
    private static nint Position(int x, int y) => (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] text, int maximumLength);
}
