using System;
using System.IO;
using System.Runtime.InteropServices;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Calls the Cloud Files API on ordinary files and folders of the test's own, outside any sync
/// root, so Windows refuses every request and nothing on this PC changes. Registering a sync root
/// adds a folder to File Explorer, so that is checked in Windows Sandbox only.
/// </summary>
public sealed class CloudFilesTests : IDisposable
{
    // HRESULT_FROM_WIN32(ERROR_NOT_A_CLOUD_FILE)
    private const int NotACloudFile = unchecked((int)0x80070178);

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string File(string name)
    {
        string path = Path.Join(_folder.FullPath, name);
        System.IO.File.WriteAllText(path, "content");
        return path;
    }

    [Fact]
    public void AnOrdinaryFileIsNotAPlaceholder()
    {
        Assert.Null(Placeholders.Read(File("plain.txt")));
        Assert.Null(Placeholders.Read(_folder.FullPath));
    }

    [Fact]
    public void ReadingAMissingFileThrows()
    {
        Assert.Throws<IOException>(() => Placeholders.Read(Path.Join(_folder.FullPath, "missing.txt")));
    }

    [Fact]
    public void ChangingAnOrdinaryFilesStateIsRefused()
    {
        string path = File("plain.txt");

        Assert.Equal(NotACloudFile, Placeholders.SetInSync(path, inSync: true));
        Assert.Equal(NotACloudFile, Placeholders.Dehydrate(path));
        Assert.Equal("content", System.IO.File.ReadAllText(path));
    }

    // Windows sets the pin attribute on any file; only a sync provider acts on it.
    [Fact]
    public void AnyFileCanBePinned()
    {
        string path = File("plain.txt");

        Assert.Equal(0, Placeholders.SetPinState(path, PinState.Pinned));
        Assert.True(System.IO.File.GetAttributes(path).HasFlag((FileAttributes)0x00080000));
        Assert.Equal(0, Placeholders.SetPinState(path, PinState.Unspecified));
        Assert.False(System.IO.File.GetAttributes(path).HasFlag((FileAttributes)0x00080000));
    }

    [Fact]
    public void PlaceholdersOutsideASyncRootAreRefused()
    {
        var entries = new[]
        {
            new PlaceholderEntry("a.txt", [1, 2, 3], IsDirectory: false, 100, DateTime.UtcNow),
            new PlaceholderEntry("folder", [4], IsDirectory: true, 0, DateTime.UtcNow) { Hidden = true },
        };
        var results = new int[entries.Length];

        int result = Placeholders.Create(_folder.FullPath, entries, results);

        Assert.NotEqual(0, result);
        Assert.Empty(Directory.GetFileSystemEntries(_folder.FullPath));
        Assert.Equal(0, Placeholders.Create(_folder.FullPath, [], []));
    }

    [Fact]
    public void ConvertingAFileOutsideASyncRootIsRefused()
    {
        string path = File("plain.txt");

        Assert.NotEqual(0, Placeholders.Convert(path, [1, 2, 3], markInSync: true));
        Assert.Null(Placeholders.Read(path));
    }

    [Fact]
    public void AnIdentityLargerThanWindowsKeepsIsRejected()
    {
        var entry = new PlaceholderEntry("a.txt", new byte[PlaceholderEntry.MaximumIdentityLength + 1], IsDirectory: false, 1, DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => Placeholders.Create(_folder.FullPath, [entry], new int[1]));
    }

    [Fact]
    public void ConnectingToAFolderThatIsntASyncRootThrows()
    {
        var requests = new NoRequests();

        Assert.ThrowsAny<COMException>(() => SyncRootConnection.Connect(_folder.FullPath, requests, _ => { }));
        Assert.Empty(Directory.GetFileSystemEntries(_folder.FullPath));
    }

    [Fact]
    public void OnlineOnlyFilesAreRecognizedByTheirAttributes()
    {
        Assert.True(Placeholders.IsOnlineOnly((FileAttributes)0x00400020));
        Assert.False(Placeholders.IsOnlineOnly(FileAttributes.Archive | FileAttributes.ReparsePoint));
    }

    private sealed class NoRequests : ICloudFileRequests
    {
        public void FetchData(FetchDataRequest request) => throw new InvalidOperationException();

        public void CancelFetchData(CancelFetchDataNotice notice) => throw new InvalidOperationException();

        public void FetchPlaceholders(FetchPlaceholdersRequest request) => throw new InvalidOperationException();

        public void Delete(DeleteRequest request) => throw new InvalidOperationException();

        public void Rename(RenameRequest request) => throw new InvalidOperationException();

        public void Dehydrate(DehydrateRequest request) => throw new InvalidOperationException();

        public void FileClosed(FileClosedNotice notice) => throw new InvalidOperationException();
    }
}
