using System;
using System.Diagnostics;
using System.IO;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Opens ordinary files of the test's own for an upload. Windows' oplock handle works on any
/// file, so stepping aside for programs is checked here; marking in sync needs a sync root, so
/// that is checked in Windows Sandbox only. Where Windows' Cloud Files filter doesn't answer, as on
/// the build machines, the checks of its answers are skipped; see <see cref="CloudFilesFilter"/>.
/// </summary>
public sealed class UploadFileTests : IDisposable
{
    // HRESULT_FROM_WIN32(ERROR_SHARING_VIOLATION)
    private const int SharingViolation = unchecked((int)0x80070020);

    // HRESULT_FROM_WIN32(ERROR_NOT_A_CLOUD_FILE)
    private const int NotACloudFile = unchecked((int)0x80070178);

    // HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)
    private const int AlreadyExists = unchecked((int)0x800700B7);

    // HRESULT_FROM_WIN32(ERROR_CLOUD_FILE_NOT_UNDER_SYNC_ROOT)
    private const int NotUnderASyncRoot = unchecked((int)0x80070186);

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string File(string name, string content)
    {
        string path = Path.Join(_folder.FullPath, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AFileIsReadThroughItsUploadHandle()
    {
        string path = File("notes.txt", "from the PC");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            using var reader = new StreamReader(file!.OpenRead());
            Assert.Equal(11, file.Length);
            Assert.Equal("from the PC", reader.ReadToEnd());
            Assert.False(file.IsClosed);
        }

        // Nothing of it stays open.
        System.IO.File.WriteAllText(path, "changed");
    }

    [Fact]
    public void AFileAProgramHasOpenIsntOpened()
    {
        string path = File("notes.txt", "being written");
        using var program = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        Assert.Equal(SharingViolation, UploadFile.Open(path, out var file));
        Assert.Null(file);
    }

    [Fact]
    public void AProgramThatOpensTheFileClosesTheUploadsHandleWithoutWaiting()
    {
        string path = File("notes.txt", "from the PC");
        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            using var stream = file!.OpenRead();
            Assert.Equal((int)'f', stream.ReadByte());

            var opening = Stopwatch.StartNew();
            using (var program = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                program.WriteByte((byte)'F');
            }

            Assert.True(opening.ElapsedMilliseconds < 5000, $"The program waited {opening.ElapsedMilliseconds} ms.");
            var error = Assert.Throws<IOException>(() => stream.ReadByte());
            Assert.Equal(UploadFile.Closed, error.HResult);
            Assert.True(file.IsClosed);
            Assert.Equal(UploadFile.Closed, file.MarkInSync([1, 2, 3], isPlaceholder: false));
        }

        Assert.Equal("From the PC", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void AReadOnlyFileCanBeOpened()
    {
        string path = File("read-only.txt", "kept");
        System.IO.File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(0, UploadFile.Open(path, out var file));
            using (file)
            {
                using var reader = new StreamReader(file!.OpenRead());
                Assert.Equal("kept", reader.ReadToEnd());
            }
        }
        finally
        {
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void MarkingAFileOutsideASyncRootIsRefused()
    {
        string path = File("notes.txt", "from the PC");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            CloudFilesFilter.AssertAnswer(NotUnderASyncRoot, file!.MarkInSync([1, 2, 3], isPlaceholder: false));
            CloudFilesFilter.AssertAnswer(NotACloudFile, file.MarkInSync([1, 2, 3], isPlaceholder: true));
        }

        Assert.Null(CloudFilesFilter.Read(path));
    }

    [Fact]
    public void AFolderOutsideASyncRootIsntConverted()
    {
        Assert.NotEqual(0, Placeholders.ConvertFolder(_folder.FullPath, [1, 2, 3]));
        Assert.Null(CloudFilesFilter.Read(_folder.FullPath));
    }

    [Fact]
    public void AFileIsRenamedThroughItsUploadHandle()
    {
        string path = File("Report.docx", "the PC's version");
        string copy = Path.Join(_folder.FullPath, "Report (conflict).docx");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            Assert.Equal(0, file!.Rename(copy));
            using var reader = new StreamReader(file.OpenRead());
            Assert.Equal("the PC's version", reader.ReadToEnd());
        }

        Assert.False(System.IO.File.Exists(path));
        Assert.Equal("the PC's version", System.IO.File.ReadAllText(copy));
    }

    [Fact]
    public void ARenameToATakenNameFailsAndChangesNothing()
    {
        string path = File("Report.docx", "the PC's version");
        string taken = File("Taken.docx", "another file");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            Assert.Equal(AlreadyExists, file!.Rename(taken));
        }

        Assert.Equal("the PC's version", System.IO.File.ReadAllText(path));
        Assert.Equal("another file", System.IO.File.ReadAllText(taken));
    }

    [Fact]
    public void ARenameAfterAProgramOpenedTheFileIsRefused()
    {
        string path = File("Report.docx", "the PC's version");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
            }

            Assert.Equal(UploadFile.Closed, file!.Rename(Path.Join(_folder.FullPath, "Copy.docx")));
            Assert.True(file.IsClosed);
        }

        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public void TheStreamSeeksAndReadsFromAnyOffset()
    {
        string path = File("notes.txt", "0123456789");

        Assert.Equal(0, UploadFile.Open(path, out var file));
        using (file)
        {
            using var stream = file!.OpenRead();
            stream.Seek(-3, SeekOrigin.End);
            var buffer = new byte[10];
            Assert.Equal(3, stream.Read(buffer, 0, 10));
            Assert.Equal("789", System.Text.Encoding.ASCII.GetString(buffer, 0, 3));
            Assert.Equal(0, stream.Read(buffer, 0, 10));
            Assert.False(stream.CanWrite);
        }
    }
}
