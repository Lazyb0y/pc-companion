using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Uses icons the tests make, Windows' own application icon, and hidden windows the tests create.
public sealed class WindowIconTests
{
    private const uint Red = 0xFFFF0000;

    [Fact]
    public void ARequestGetsTheIconTheWindowShows()
    {
        var icon = HalfRedIcon();
        try
        {
            using var other = new IconWindowThread(icon, stuck: false);
            using var pump = new MessageWindow(UniqueClassName());
            nint? answer = null;

            Assert.True(WindowIcon.Request(other.Handle, large: false, given => answer = given));
            RunFor(pump, TimeSpan.FromMilliseconds(300));

            Assert.Equal(icon, answer);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [Fact]
    public void ARequestDoesNotWaitForAWindowWhoseThreadIsStuck()
    {
        var icon = HalfRedIcon();
        try
        {
            using var other = new IconWindowThread(icon, stuck: true);
            using var pump = new MessageWindow(UniqueClassName());
            nint? answer = null;

            Assert.True(WindowIcon.Request(other.Handle, large: false, given => answer = given));
            RunFor(pump, TimeSpan.FromMilliseconds(200));
            Assert.Null(answer);

            other.Release();
            RunFor(pump, TimeSpan.FromMilliseconds(300));

            Assert.Equal(icon, answer);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [Fact]
    public void AClassWithoutAnIconHasNone()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Equal(0, WindowIcon.OfClass(window.Handle, large: false));
        Assert.Equal(0, WindowIcon.OfClass(window.Handle, large: true));
    }

    [Fact]
    public void AnIconWithAMaskIsOpaqueWhereTheMaskShowsIt()
    {
        var icon = HalfRedIcon();
        try
        {
            var pixels = WindowIcon.Pixels(icon, 16);

            Assert.NotNull(pixels);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    Assert.Equal(x < 8 ? 0u : Red, pixels[(y * 16) + x]);
                }
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [Fact]
    public void AnIconIsDrawnAtTheSizeAskedFor()
    {
        var icon = HalfRedIcon();
        try
        {
            var pixels = WindowIcon.Pixels(icon, 32);

            Assert.NotNull(pixels);
            Assert.Equal(32 * 32, pixels.Length);
            Assert.Equal(0u, pixels[(4 * 32) + 4]);
            Assert.Equal(Red, pixels[(28 * 32) + 28]);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [Fact]
    public void AnIconWithAnAlphaChannelKeepsItPremultiplied()
    {
        // Windows' application icon has transparent and opaque parts; it's shared, so it isn't destroyed.
        var icon = LoadIcon(0, 32512);
        Assert.NotEqual(0, icon);

        var pixels = WindowIcon.Pixels(icon, 32);

        Assert.NotNull(pixels);
        Assert.Contains(pixels, pixel => pixel >> 24 == 0);
        Assert.Contains(pixels, pixel => pixel >> 24 == 0xFF);
        Assert.All(pixels, pixel =>
        {
            var alpha = pixel >> 24;
            Assert.True(((pixel >> 16) & 0xFF) <= alpha && ((pixel >> 8) & 0xFF) <= alpha && (pixel & 0xFF) <= alpha);
        });
    }

    [Fact]
    public void AnIconThatWasDestroyedHasNoPixels()
    {
        var icon = HalfRedIcon();
        DestroyIcon(icon);

        Assert.Null(WindowIcon.Pixels(icon, 16));
    }

    // A 16-pixel icon without an alpha channel: its left half is masked out, its right half red.
    private static nint HalfRedIcon()
    {
        var mask = Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 0xFF, 0x00 }).ToArray();
        var colors = new byte[16 * 16 * 4];
        for (var at = 0; at < 16 * 16; at++)
        {
            if (at % 16 >= 8)
            {
                colors[(at * 4) + 2] = 0xFF;
            }
        }

        var icon = CreateIcon(0, 16, 16, 1, 32, mask, colors);
        Assert.NotEqual(0, icon);
        return icon;
    }

    // A window on a thread of its own that shows an icon and runs its message loop, or first waits
    // to be released, like a program that stopped responding.
    private sealed class IconWindowThread : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private MessageWindow? _window;

        public IconWindowThread(nint icon, bool stuck)
        {
            _thread = new Thread(() =>
            {
                using var window = new MessageWindow(UniqueClassName());
                _ = SendMessage(window.Handle, 0x0080, 0, icon); // WM_SETICON, ICON_SMALL
                _window = window;
                Handle = window.Handle;
                _ready.Set();
                if (stuck)
                {
                    _ = _release.Wait(TimeSpan.FromSeconds(30));
                }

                MessageLoop.Run();
            });
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(30)));
        }

        public nint Handle { get; private set; }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _window!.BeginInvoke(MessageLoop.Quit);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(30)));
            _ready.Dispose();
            _release.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIcon(nint instance, int width, int height, byte planes, byte bitsPerPixel, byte[] andBits, byte[] xorBits);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint instance, nint name);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
