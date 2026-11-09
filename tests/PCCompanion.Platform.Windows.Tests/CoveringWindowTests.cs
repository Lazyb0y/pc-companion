using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own window with a unique class name and never shows it, since a shown
// window would cover the screen and take the focus; input is posted to it. The Sandbox checks show it.
public sealed class CoveringWindowTests
{
    private const uint MouseMove = 0x0200;
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint KeyDown = 0x0100;
    private const uint SystemKeyDown = 0x0104;
    private const uint Activate = 0x0006;
    private const uint DisplayChange = 0x007E;

    [Fact]
    public void TheWindowDrawsNothingStaysOnTopAndIsOffTheTaskbar()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");

        var style = (long)GetWindowLongPtr(window.Handle, -20); // GWL_EXSTYLE
        Assert.Equal(0x00200088L, style & 0x00200088L); // WS_EX_NOREDIRECTIONBITMAP, WS_EX_TOOLWINDOW, WS_EX_TOPMOST
        Assert.False(TopLevelWindows.MayHaveTaskbarButton(window.Handle));
        Assert.False(window.IsShown);
        Assert.False(IsWindowVisible(window.Handle));
        Assert.Equal(CoveringPointer.Cross, window.PointerShape);
    }

    [Fact]
    public void PointerInputRaisesMovesAndClicksWithTheirPlaces()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");
        var moves = new List<Point>();
        var clicks = new List<(Point, bool)>();
        window.PointerMoved += (_, e) => moves.Add(e.Point);
        window.Clicked += (_, e) => clicks.Add((e.Point, e.IsRight));

        PostMessage(window.Handle, MouseMove, 0, MakePoint(10, 20));
        PostMessage(window.Handle, LeftButtonUp, 0, MakePoint(30, 40));
        PostMessage(window.Handle, RightButtonUp, 0, MakePoint(50, 60));
        RunPostedMessages();

        Assert.Equal([new Point(10, 20)], moves);
        Assert.Equal([(new Point(30, 40), false), (new Point(50, 60), true)], clicks);
    }

    [Fact]
    public void KeysRaiseKeyPressedWithOrWithoutAlt()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");
        var keys = new List<uint>();
        window.KeyPressed += (_, e) => keys.Add(e.VirtualKey);

        PostMessage(window.Handle, KeyDown, 0x09, 0); // Tab
        PostMessage(window.Handle, SystemKeyDown, 0x0D, 0); // Alt + Enter
        RunPostedMessages();

        Assert.Equal([0x09u, 0x0Du], keys);
    }

    [Fact]
    public void AHiddenWindowLosingTheFrontRaisesNothing()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");
        var deactivated = 0;
        window.Deactivated += (_, _) => deactivated++;

        PostMessage(window.Handle, Activate, 0, 0); // WA_INACTIVE
        RunPostedMessages();

        Assert.Equal(0, deactivated);
    }

    [Fact]
    public void AChangeOfDisplaysIsRaised()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");
        var changes = 0;
        window.DisplaysChanged += (_, _) => changes++;

        PostMessage(window.Handle, DisplayChange, 32, 0);
        RunPostedMessages();

        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheTitleIsTheWindowsName()
    {
        using var window = new CoveringWindow(UniqueClassName(), "First");

        window.SetTitle("End App: choose an app to end. Notepad, not responding");

        var text = new char[128];
        var length = GetWindowText(window.Handle, text, text.Length);
        Assert.Equal("End App: choose an app to end. Notepad, not responding", new string(text, 0, length));
    }

    [Fact]
    public void ThePointerShapeCanChangeWhileHidden()
    {
        using var window = new CoveringWindow(UniqueClassName(), "Test");

        window.PointerShape = CoveringPointer.Unavailable;

        Assert.Equal(CoveringPointer.Unavailable, window.PointerShape);
    }

    [Fact]
    public void ADisposedWindowCantBeShown()
    {
        var window = new CoveringWindow(UniqueClassName(), "Test");
        window.Dispose();
        window.Dispose();

        Assert.Throws<ObjectDisposedException>(() => window.Show());
    }

    private static nint MakePoint(int x, int y) => (y << 16) | (x & 0xFFFF);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] text, int maximumLength);
}
