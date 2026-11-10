using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// These tests only read: changing the wallpaper would change the desktop of whoever runs them.
// Windows Sandbox's DynamicWallpaper group changes it.
public sealed class WindowsWallpaperTests
{
    [Fact]
    public void TheWallpaperFunctionListsTheConnectedDisplaysByTheirDevicePaths()
    {
        SkipWithoutExplorer();
        using var wallpaper = WindowsWallpaper.Connect();

        var connected = wallpaper.Displays().Where(display => display.Area is not null).Select(display => display.Id);
        var active = DisplayConfiguration.Read().Displays.Select(display => display.DevicePath);

        Assert.NotEmpty(connected);
        Assert.Subset(active.ToHashSet(StringComparer.OrdinalIgnoreCase), connected.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheBackgroundCanBeRead()
    {
        SkipWithoutExplorer();
        using var wallpaper = WindowsWallpaper.Connect();

        var status = wallpaper.Status();
        var picture = wallpaper.PictureOf(null);

        Assert.InRange((int)wallpaper.Position(), (int)WallpaperPosition.Center, (int)WallpaperPosition.Span);
        Assert.InRange(wallpaper.BackgroundColor(), 0u, 0xFFFFFFu);
        Assert.True(picture is not null || status.HasFlag(WallpaperStatus.Slideshow) || wallpaper.Displays().Count > 1);
        Assert.Equal(status.HasFlag(WallpaperStatus.Slideshow), wallpaper.Slideshow() is not null);
    }

    [Fact]
    public void TheWallpaperFunctionsFailuresWhenExplorerIsGoneAreKnown()
    {
        Assert.True(WindowsWallpaper.IsExplorerGone(unchecked((int)0x800706BA)));
        Assert.True(WindowsWallpaper.IsExplorerGone(unchecked((int)0x80040154)));
        Assert.False(WindowsWallpaper.IsExplorerGone(unchecked((int)0x80070002)));
        Assert.False(WindowsWallpaper.IsExplorerGone(0));
    }

    [Fact]
    public void AThreadWaitingForNoCallHasNothingToCancel()
    {
        var id = 0u;
        using var allowed = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            CallCancellation.Allow();
            id = CallCancellation.CurrentThreadId;
            allowed.Set();
            done.Wait();
        });
        thread.Start();
        allowed.Wait(TestContext.Current.CancellationToken);

        Assert.False(CallCancellation.Cancel(id));
        done.Set();
        thread.Join();
        Assert.True(CallCancellation.IsCancelled(unchecked((int)0x80010002)));
    }

    private static void SkipWithoutExplorer() =>
        Assert.SkipWhen(Taskbar.Read() is null, "Explorer isn't running, so Windows' wallpaper function isn't there.");
}
