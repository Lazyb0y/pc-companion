using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Asks only a hidden window the test creates, which answers as each test sets.
public sealed class WindowHitTestTests
{
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtTransparent = -1;

    [Theory]
    [InlineData(HtCaption, HitTestAnswer.Caption)]
    [InlineData(HtTransparent, HitTestAnswer.Transparent)]
    [InlineData(HtClient, HitTestAnswer.Elsewhere)]
    [InlineData(9, HitTestAnswer.Elsewhere)]
    public void AWindowsAnswerIsPassedOn(int code, HitTestAnswer expected)
    {
        using var window = new HitTestWindow(code);
        HitTestAnswer? answer = null;

        Assert.True(WindowHitTest.Request(window.Handle, new Point(10, 20), received => answer = received));

        // A window of this thread answers at once.
        Assert.Equal(expected, answer);
    }

    [Fact]
    public void TheWindowIsAskedAboutThePoint()
    {
        using var window = new HitTestWindow(HtCaption);
        if (TopLevelWindows.Dpi(window.Handle) != 96)
        {
            Assert.Skip("A display at another scale may convert the point for this test's window.");
        }

        WindowHitTest.Request(window.Handle, new Point(-50, 1234), _ => { });

        Assert.Equal(new Point(-50, 1234), window.AskedAbout);
    }

    [Fact]
    public void AWindowThatDoesntExistIsntAsked()
    {
        var answered = false;

        Assert.False(WindowHitTest.Request(0, new Point(10, 20), _ => answered = true));
        Assert.False(answered);
    }

    [Fact]
    public void ATopLevelWindowIsItsOwnRootAndHasNoParent()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Equal(window.Handle, WindowHitTest.Root(window.Handle));
        Assert.Equal(0, WindowHitTest.Parent(window.Handle));
    }

    // A hidden window that answers WM_NCHITTEST with a given code and keeps the point it was asked about.
    private sealed class HitTestWindow : IDisposable
    {
        private const uint WmNcHitTest = 0x0084;

        private readonly WndProc _procedure;
        private readonly string _className = UniqueClassName();
        private readonly int _code;

        public HitTestWindow(int code)
        {
            _code = code;
            _procedure = Procedure;
            var windowClass = new WndClassEx
            {
                Size = Marshal.SizeOf<WndClassEx>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
                Instance = GetModuleHandle(null),
                ClassName = _className,
            };
            Assert.NotEqual(0, RegisterClassEx(ref windowClass));
            Handle = CreateWindowEx(0, _className, string.Empty, 0x80000000, 0, 0, 100, 100, 0, 0, windowClass.Instance, 0);
            Assert.NotEqual(0, Handle);
        }

        private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

        public nint Handle { get; }

        public Point? AskedAbout { get; private set; }

        public void Dispose()
        {
            DestroyWindow(Handle);
            UnregisterClass(_className, GetModuleHandle(null));
            GC.KeepAlive(_procedure);
        }

        private nint Procedure(nint window, uint message, nint wParam, nint lParam)
        {
            if (message != WmNcHitTest)
            {
                return DefWindowProc(window, message, wParam, lParam);
            }

            AskedAbout = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            return _code;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassEx
        {
            public int Size;
            public uint Style;
            public nint Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public nint Instance;
            public nint Icon;
            public nint Cursor;
            public nint Background;
            public string? MenuName;
            public string ClassName;
            public nint SmallIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string className, nint instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? name);
    }
}
