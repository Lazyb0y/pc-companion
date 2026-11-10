using System.IO;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// Each test uses files of its own, which are never read: only their names and attributes are.
public sealed class WindowsPictureFilesTests
{
    private readonly WindowsPictureFiles _files = new();

    [Fact]
    public void APictureOnThePcCanBeUsed()
    {
        using var folder = new TemporaryFolder();
        var picture = Create(folder, "Day.jpg");

        Assert.Equal(PictureState.Usable, _files.Check(picture));
        Assert.Equal(PictureState.Usable, _files.CheckFolder(folder.FullPath));
    }

    [Fact]
    public void APictureThatsGoneCant()
    {
        using var folder = new TemporaryFolder();

        Assert.Equal(PictureState.Missing, _files.Check(Path.Combine(folder.FullPath, "Gone.jpg")));
        Assert.Equal(PictureState.Missing, _files.Check(folder.FullPath));
        Assert.Equal(PictureState.Missing, _files.CheckFolder(Path.Combine(folder.FullPath, "Gone")));
    }

    [Fact]
    public void APictureKeptOnlineCant()
    {
        using var folder = new TemporaryFolder();
        var picture = Create(folder, "Day.jpg");
        File.SetAttributes(picture, FileAttributes.Offline);

        Assert.Equal(PictureState.OnlineOnly, _files.Check(picture));
        Assert.Empty(_files.ListFolder(folder.FullPath));
    }

    [Fact]
    public void AFolderListsItsPicturesByName()
    {
        using var folder = new TemporaryFolder();
        Create(folder, "b.png");
        Create(folder, "A.jpg");
        Create(folder, "notes.txt");
        Create(folder, "c.heic");
        File.SetAttributes(Create(folder, "hidden.jpg"), FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(folder.FullPath, "sub.jpg"));
        Create(folder, Path.Combine("sub.jpg", "deeper.jpg"));

        Assert.Equal(["A.jpg", "b.png"], _files.ListFolder(folder.FullPath).Select(Path.GetFileName));
    }

    [Fact]
    public void AFolderThatsGoneHasNoPictures()
    {
        using var folder = new TemporaryFolder();

        Assert.Empty(_files.ListFolder(Path.Combine(folder.FullPath, "Gone")));
    }

    private static string Create(TemporaryFolder folder, string name)
    {
        var path = Path.Combine(folder.FullPath, name);
        File.WriteAllText(path, "");
        return path;
    }
}
