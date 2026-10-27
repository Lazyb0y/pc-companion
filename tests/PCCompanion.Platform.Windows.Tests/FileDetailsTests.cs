using System;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class FileDetailsTests
{
    private static readonly DateTime Modified = new(2026, 3, 5, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void AFilesDetailsAreRead()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.FullPath, "report.pdf");
        File.WriteAllBytes(path, new byte[1234]);
        File.SetLastWriteTimeUtc(path, Modified);

        var details = FileDetails.Read(path)!;

        Assert.Equal("report.pdf", details.Name);
        Assert.Equal(1234, details.Size);
        Assert.Equal(Modified, details.Modified.UtcDateTime);
        Assert.True(details.Changed > details.Modified);
        Assert.NotNull(details.FileId);
        Assert.False(details.IsFolder);
        Assert.False(details.IsLink);
        Assert.False(details.IsOnlineOnly);
    }

    [Fact]
    public void AFileThatDoesntExistHasNoDetails()
    {
        using var folder = new TemporaryFolder();

        Assert.Null(FileDetails.Read(Path.Combine(folder.FullPath, "missing.pdf")));
        Assert.Null(FileDetails.Read(Path.Combine(folder.FullPath, "Missing", "missing.pdf")));
    }

    [Fact]
    public void AFileKeepsItsIdWhenRenamed()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.FullPath, "a.txt");
        File.WriteAllText(path, "a");
        var before = FileDetails.Read(path)!;

        var renamed = Path.Combine(folder.FullPath, "b.txt");
        File.Move(path, renamed);

        Assert.Equal(before.FileId, FileDetails.Read(renamed)!.FileId);
    }

    [Fact]
    public void AFoldersFilesAndFoldersAreListedWithTheirDetails()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.FullPath, "report.pdf");
        File.WriteAllBytes(path, new byte[1234]);
        File.SetLastWriteTimeUtc(path, Modified);
        File.WriteAllText(Path.Combine(folder.FullPath, "notes.txt"), "notes");
        Directory.CreateDirectory(Path.Combine(folder.FullPath, "Sub"));

        var items = FileDetails.List(folder.FullPath);

        Assert.Equal(["notes.txt", "report.pdf", "Sub"], items.Select(item => item.Name).Order(StringComparer.OrdinalIgnoreCase));
        var report = items.Single(item => item.Name == "report.pdf");
        Assert.Equal(FileDetails.Read(path), report);
        Assert.True(items.Single(item => item.Name == "Sub").IsFolder);
    }

    [Fact]
    public void AFolderThatDoesntExistCantBeListed()
    {
        using var folder = new TemporaryFolder();

        Assert.Throws<System.ComponentModel.Win32Exception>(() => FileDetails.List(Path.Combine(folder.FullPath, "Missing")));
    }

    [Fact]
    public void ALinkIsAReparsePointThatStandsForSomethingElse()
    {
        static FileDetails With(FileAttributes attributes, uint tag) => new("a", attributes, tag, 0, default, default, default, null);

        Assert.True(With(FileAttributes.ReparsePoint, 0xA000000C).IsLink);
        Assert.True(With(FileAttributes.ReparsePoint | FileAttributes.Directory, 0xA0000003).IsLink);
        Assert.False(With(FileAttributes.ReparsePoint, 0x9000701A).IsLink);
        Assert.False(With(FileAttributes.Normal, 0xA000000C).IsLink);
    }

    [Fact]
    public void AFileKeptOnlineOnlyIsToldByItsAttributes()
    {
        static FileDetails With(int attributes) => new("a", (FileAttributes)attributes, 0, 0, default, default, default, null);

        Assert.True(With(0x401620).IsOnlineOnly);
        Assert.True(With(0x40000).IsOnlineOnly);
        Assert.True(With(0x1000).IsOnlineOnly);
        Assert.False(With(0x420).IsOnlineOnly);
    }

    [Fact]
    public void AFileOpenForWritingIsntAlone()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.FullPath, "download.bin");
        using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            writer.Write(new byte[10]);
            Assert.False(FileDetails.CanOpenAlone(path));
        }

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.True(FileDetails.CanOpenAlone(path));
        }

        Assert.True(FileDetails.CanOpenAlone(path));
    }

    [Fact]
    public void TheTempFoldersDriveKeepsStreams()
    {
        using var folder = new TemporaryFolder();

        var volume = VolumeDetails.Read(folder.FullPath);

        Assert.Equal(DriveKind.Fixed, volume.Kind);
        Assert.Equal("NTFS", volume.FileSystem);
        Assert.True(volume.NamedStreams);
    }
}
