using System.IO;
using System.Threading;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class FileTransferTests
{
    private const string Mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/report.pdf\r\n";

    private static string NewFile(TemporaryFolder folder, string name, string text = "report")
    {
        var path = Path.Combine(folder.FullPath, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AMoveKeepsTheFileAndItsMark()
    {
        using var folder = new TemporaryFolder();
        var source = NewFile(folder, "report.pdf");
        File.WriteAllText(source + ":Zone.Identifier", Mark);
        var destination = Path.Combine(Directory.CreateDirectory(Path.Combine(folder.FullPath, "Sorted")).FullName, "report.pdf");

        Assert.Equal(0, FileTransfer.Move(source, destination, CancellationToken.None));

        Assert.False(File.Exists(source));
        Assert.Equal("report", File.ReadAllText(destination));
        Assert.Equal(Mark, File.ReadAllText(destination + ":Zone.Identifier"));
    }

    [Fact]
    public void ACopyKeepsTheOriginalAndCopiesTheMark()
    {
        using var folder = new TemporaryFolder();
        var source = NewFile(folder, "report.pdf");
        File.WriteAllText(source + ":Zone.Identifier", Mark);
        var destination = Path.Combine(folder.FullPath, "copy.pdf");

        Assert.Equal(0, FileTransfer.Copy(source, destination, CancellationToken.None));

        Assert.Equal("report", File.ReadAllText(source));
        Assert.Equal("report", File.ReadAllText(destination));
        Assert.Equal(Mark, File.ReadAllText(destination + ":Zone.Identifier"));
    }

    [Fact]
    public void AMoveNeverReplacesAFile()
    {
        using var folder = new TemporaryFolder();
        var source = NewFile(folder, "report.pdf", "new");
        var existing = NewFile(folder, "taken.pdf", "old");

        Assert.Equal(FileTransfer.AlreadyExists, FileTransfer.Move(source, existing, CancellationToken.None));

        Assert.Equal("new", File.ReadAllText(source));
        Assert.Equal("old", File.ReadAllText(existing));
    }

    [Fact]
    public void ACopyNeverReplacesAFile()
    {
        using var folder = new TemporaryFolder();
        var source = NewFile(folder, "report.pdf", "new");
        var existing = NewFile(folder, "taken.pdf", "old");

        Assert.Equal(FileTransfer.FileExists, FileTransfer.Copy(source, existing, CancellationToken.None));

        Assert.Equal("old", File.ReadAllText(existing));
    }

    [Fact]
    public void ARenameMayChangeOnlyTheCase()
    {
        using var folder = new TemporaryFolder();
        var source = NewFile(folder, "report.pdf");
        var renamed = Path.Combine(folder.FullPath, "Report.pdf");

        Assert.Equal(0, FileTransfer.Move(source, renamed, CancellationToken.None));

        Assert.Equal("Report.pdf", Path.GetFileName(Directory.GetFiles(folder.FullPath)[0]));
    }

    [Fact]
    public void AFileThatsGoneIsReported()
    {
        using var folder = new TemporaryFolder();
        var missing = Path.Combine(folder.FullPath, "missing.pdf");

        Assert.Equal(2, FileTransfer.Move(missing, Path.Combine(folder.FullPath, "a.pdf"), CancellationToken.None));
        Assert.Equal(2, FileTransfer.Copy(missing, Path.Combine(folder.FullPath, "a.pdf"), CancellationToken.None));
    }

    [Fact]
    public void ACancelledCopyLeavesNoCopy()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.FullPath, "large.bin");
        File.WriteAllBytes(source, new byte[16 * 1024 * 1024]);
        var destination = Path.Combine(folder.FullPath, "copy.bin");

        Assert.Equal(FileTransfer.Cancelled, FileTransfer.Copy(source, destination, new CancellationToken(canceled: true)));

        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(source));
    }
}
