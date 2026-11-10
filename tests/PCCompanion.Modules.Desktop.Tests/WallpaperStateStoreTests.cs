using System.IO;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class WallpaperStateStoreTests
{
    [Fact]
    public void WithoutAFileTheStateIsNew()
    {
        using var folder = new TemporaryFolder();
        var store = new WallpaperStateStore(Path.Combine(folder.FullPath, WallpaperStateStore.FileName));

        Assert.Equal(new WallpaperState(), store.Load(out var readable));
        Assert.True(readable);
    }

    [Fact]
    public void TheStateIsSavedAndReadAgain()
    {
        using var folder = new TemporaryFolder();
        var store = new WallpaperStateStore(Path.Combine(folder.FullPath, "data", WallpaperStateStore.FileName));
        var state = new WallpaperState
        {
            Remembered = new RememberedBackground
            {
                Kind = BackgroundKind.Slideshow,
                Pictures = [new DisplayPicture(null, @"C:\Old.jpg")],
                Fit = WallpaperPosition.Center,
                Color = 0x008080,
                Slideshow = [@"C:\Pictures\Slides"],
                SlideshowShuffle = true,
                SlideshowInterval = 600_000,
            },
            SetLast = [new DisplayPicture(@"\\?\DISPLAY#DELF11B#5&2a1f3e84&0&UID4352", @"C:\Day.jpg")],
            SteppedAside = true,
            Folders = [new FolderPlace(@"C:\Pictures\Night", "0123456789abcdef", 42, 3)],
        };

        store.Save(state);

        Assert.Equal(state, store.Load(out var readable));
        Assert.True(readable);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "steppedAside": "yes" }""")]
    public void AFileThatCantBeReadGivesANewState(string text)
    {
        using var folder = new TemporaryFolder();
        var store = new WallpaperStateStore(Path.Combine(folder.FullPath, WallpaperStateStore.FileName));
        File.WriteAllText(store.FilePath, text);

        Assert.Equal(new WallpaperState(), store.Load(out var readable));
        Assert.False(readable);
    }

    [Fact]
    public void AFileTooLargeIsntRead()
    {
        using var folder = new TemporaryFolder();
        var store = new WallpaperStateStore(Path.Combine(folder.FullPath, WallpaperStateStore.FileName));
        File.WriteAllText(store.FilePath, new string(' ', WallpaperStateStore.MaximumFileSize + 1));

        store.Load(out var readable);

        Assert.False(readable);
    }

    [Fact]
    public void DeletingRemovesTheFile()
    {
        using var folder = new TemporaryFolder();
        var store = new WallpaperStateStore(Path.Combine(folder.FullPath, WallpaperStateStore.FileName));
        store.Save(new WallpaperState { SteppedAside = true });

        store.Delete();
        store.Delete();

        Assert.False(File.Exists(store.FilePath));
        new WallpaperStateStore(Path.Combine(folder.FullPath, "missing", WallpaperStateStore.FileName)).Delete();
    }
}
