using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class ChangesTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\Remote Folders\Home";
    private static readonly DateTime When = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TemporaryFolder _data = new();
    private readonly FakeRemoteServer _server = new();
    private readonly FakePlaceholderSystem _pc = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeConnections _connections;
    private readonly ServerUploads _uploads;
    private readonly ServerChanges _changes;

    public ChangesTests()
    {
        _connections = new FakeConnections(_server);
        _uploads = new ServerUploads(Root, "/home/ana", _connections, _pc, _time, NullLogger.Instance, 1);
        _changes = NewChanges();
        _uploads.Changes = _changes;
    }

    private string JournalFile => Path.Join(_data.FullPath, "remote-folders", "server.json");

    public void Dispose()
    {
        _server.Dispose();
        _data.Dispose();
    }

    private static string Remote(string relative) => "/home/ana/" + relative;

    private static string Local(string relative) => Path.Join(Root, relative.Replace('/', '\\'));

    private ServerChanges NewChanges() => new("/home/ana", _connections, new ChangeJournal(JournalFile), _uploads, NullLogger.Instance, 1);

    private static JournalEntry Delete(string path, long size = 3) => new() { Kind = JournalKind.Delete, Path = path, Size = size, LastWriteTimeUtc = When };

    private static JournalEntry Rename(string path, string target, bool isDirectory = false) => new() { Kind = JournalKind.Rename, Path = path, Target = target, IsDirectory = isDirectory };

    private Task<int> MakeAsync(JournalEntry change) => _changes.MakeAsync(change, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ADeletedFileIsDeletedOnTheServer()
    {
        _server.AddFile(Remote("a.txt"), "abc", When);

        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Delete("a.txt")));

        Assert.False(_server.Exists(Remote("a.txt")));
    }

    [Fact]
    public async Task AFileThatChangedOnTheServerIsntDeleted()
    {
        _server.AddFile(Remote("a.txt"), "changed", When.AddMinutes(1));

        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Delete("a.txt")));

        Assert.Equal("changed", _server.Content(Remote("a.txt")));
    }

    [Fact]
    public async Task AFileAlreadyGoneFromTheServerIsDeletedOnThePc()
    {
        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Delete("gone.txt")));
    }

    [Fact]
    public async Task AFolderLosesTheFilesThePcKnewAndThenItself()
    {
        _server.AddFolder(Remote("docs"));
        _server.AddFolder(Remote("docs/sub"));
        _server.AddFile(Remote("docs/a.txt"), "abc", When);
        _server.AddFile(Remote("docs/sub/b.txt"), "abc", When);

        var change = new JournalEntry { Kind = JournalKind.Delete, Path = "docs", IsDirectory = true, Files = [new("docs/a.txt", 3, When), new("docs/sub/b.txt", 3, When)] };
        Assert.Equal(CloudFileStatus.Success, await MakeAsync(change));

        Assert.False(_server.Exists(Remote("docs")));
        Assert.False(_server.Exists(Remote("docs/sub")));
    }

    [Fact]
    public async Task AFolderWithAFileThePcDidntKnowStaysWithIt()
    {
        _server.AddFolder(Remote("docs"));
        _server.AddFile(Remote("docs/a.txt"), "abc", When);
        _server.AddFile(Remote("docs/new.txt"), "added on the server", When);

        var change = new JournalEntry { Kind = JournalKind.Delete, Path = "docs", IsDirectory = true, Files = [new("docs/a.txt", 3, When)] };
        Assert.Equal(CloudFileStatus.Success, await MakeAsync(change));

        Assert.False(_server.Exists(Remote("docs/a.txt")));
        Assert.True(_server.Exists(Remote("docs/new.txt")));
    }

    [Fact]
    public async Task ARenameIsMadeOnTheServer()
    {
        _server.AddFolder(Remote("docs"));
        _server.AddFile(Remote("docs/a.txt"), "abc", When);

        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Rename("docs", "papers", isDirectory: true)));

        Assert.Equal("abc", _server.Content(Remote("papers/a.txt")));
        Assert.False(_server.Exists(Remote("docs")));
    }

    [Fact]
    public async Task ARenameToANameTakenOnTheServerIsRefused()
    {
        _server.AddFile(Remote("a.txt"), "a", When);
        _server.AddFile(Remote("b.txt"), "b", When);

        Assert.Equal(CloudFileStatus.Unsuccessful, await MakeAsync(Rename("a.txt", "b.txt")));

        Assert.Equal("a", _server.Content(Remote("a.txt")));
        Assert.Equal("b", _server.Content(Remote("b.txt")));
    }

    [Fact]
    public async Task AChangeTheServerRefusesIsRefusedOnThePc()
    {
        _server.AddFile(Remote("a.txt"), "abc", When);
        _server.Failure = RemoteError.PermissionDenied;

        Assert.Equal(CloudFileStatus.AccessDenied, await MakeAsync(Delete("a.txt")));
        Assert.Equal(0, _changes.Waiting);
    }

    [Fact]
    public async Task WhileTheServerCantBeReachedChangesWaitInTheJournalInOrder()
    {
        _server.AddFile(Remote("a.txt"), "abc", When);
        _server.Failure = RemoteError.Unreachable;

        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Rename("a.txt", "b.txt")));
        _server.Failure = null;
        Assert.Equal(CloudFileStatus.Success, await MakeAsync(Delete("b.txt")));

        // The delete waits behind the rename, though the server is back.
        Assert.Equal(2, _changes.Waiting);
        Assert.True(_server.Exists(Remote("a.txt")));
        Assert.True(File.Exists(JournalFile));

        Assert.True(await _changes.ReplayAsync(TestContext.Current.CancellationToken));

        Assert.False(_server.Exists(Remote("a.txt")));
        Assert.False(_server.Exists(Remote("b.txt")));
        Assert.Equal(0, _changes.Waiting);
        Assert.False(File.Exists(JournalFile));
    }

    [Fact]
    public async Task TheJournalLastsAcrossRestarts()
    {
        _server.Failure = RemoteError.Unreachable;
        await MakeAsync(Rename("a.txt", "b.txt"));
        await MakeAsync(Delete("c.txt", 7));

        var again = new ChangeJournal(JournalFile);

        Assert.Equal(2, again.Count);
        Assert.Equal(Rename("a.txt", "b.txt"), again.First);
        Assert.False(again.WasUnreadable);
    }

    [Fact]
    public void AnUnreadableJournalStartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalFile)!);
        File.WriteAllText(JournalFile, "{ not json");

        var journal = new ChangeJournal(JournalFile);

        Assert.Equal(0, journal.Count);
        Assert.True(journal.WasUnreadable);
    }

    [Fact]
    public async Task ReplayingStopsWhenTheServerCantBeReachedAndKeepsTheRest()
    {
        _server.AddFile(Remote("a.txt"), "abc", When);
        _server.Failure = RemoteError.Unreachable;
        await MakeAsync(Rename("a.txt", "b.txt"));

        Assert.False(await _changes.ReplayAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, _changes.Waiting);
        Assert.True(_server.Exists(Remote("a.txt")));
    }

    [Fact]
    public async Task AJournalChangeTheServerRefusesIsSkippedAndListed()
    {
        _server.AddFile(Remote("a.txt"), "a", When);
        _server.Failure = RemoteError.Unreachable;
        await MakeAsync(Rename("a.txt", "b.txt"));
        _server.Failure = null;
        _server.AddFile(Remote("b.txt"), "taken meanwhile", When);

        Assert.True(await _changes.ReplayAsync(TestContext.Current.CancellationToken));

        var (entry, error) = Assert.Single(_changes.Skipped);
        Assert.Equal("b.txt", entry.Target);
        Assert.Equal(RemoteError.Failed, error);
        Assert.Equal("taken meanwhile", _server.Content(Remote("b.txt")));
    }

    [Fact]
    public async Task TheUploadWorkerMakesTheJournalsChangesBeforeUploading()
    {
        _server.AddFile(Remote("a.txt"), "abc", When);
        _server.Failure = RemoteError.Unreachable;
        await MakeAsync(Rename("a.txt", "b.txt"));
        _pc.AddPlaceholder(Local("b.txt"), new ItemIdentity("a.txt", false, 3, When), inSync: false, onlineOnly: false);
        _pc.Contents[Local("b.txt")] = "edited";
        _server.Failure = null;
        using var stop = new CancellationTokenSource();

        var worker = _uploads.RunAsync(stop.Token);
        _uploads.Changed(Local("b.txt"));
        await AdvanceUntilAsync(() => _server.Content(Remote("b.txt")) == "edited");

        Assert.False(_server.Exists(Remote("a.txt")));
        Assert.Empty(_uploads.Conflicts);
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMovedFileThatsUnchangedLearnsItsPathWithoutAnUpload()
    {
        _server.AddFile(Remote("papers/a.txt"), "abc", When);
        _pc.AddPlaceholder(Local("papers/a.txt"), new ItemIdentity("docs/a.txt", false, 3, When), inSync: false, onlineOnly: true);

        // A rename clears the in-sync mark without changing any data.
        _pc.Local[Local("papers/a.txt")] = (false, _pc.Read(Local("papers/a.txt"))! with { ModifiedSize = 0 });

        Assert.Equal(UploadOutcome.Done, await _uploads.UploadAsync(Local("papers/a.txt"), TestContext.Current.CancellationToken));

        Assert.Equal(0, _pc.UploadAttempts);
        Assert.Equal("papers/a.txt", ItemIdentity.FromBytes([.. _pc.Read(Local("papers/a.txt"))!.Identity])!.RelativePath);
        Assert.True(_pc.Read(Local("papers/a.txt"))!.InSync);
    }

    [Fact]
    public async Task AMovedFolderLearnsItsPath()
    {
        _pc.AddPlaceholder(Local("papers"), new ItemIdentity("docs", true, 0, When), inSync: false);

        Assert.Equal(UploadOutcome.Done, await _uploads.UploadAsync(Local("papers"), TestContext.Current.CancellationToken));

        Assert.Equal("papers", ItemIdentity.FromBytes([.. _pc.Read(Local("papers"))!.Identity])!.RelativePath);
    }

    [Fact]
    public async Task AMovedFileTheServerDoesntHaveThereIsUploaded()
    {
        _pc.AddPlaceholder(Local("papers/a.txt"), new ItemIdentity("docs/a.txt", false, 3, When), inSync: false, onlineOnly: false);
        _pc.AddPlaceholder(Local("papers"), new ItemIdentity("papers", true, 0, When));
        _server.AddFolder(Remote("papers"));
        _pc.Contents[Local("papers/a.txt")] = "abc";

        Assert.Equal(UploadOutcome.Done, await _uploads.UploadAsync(Local("papers/a.txt"), TestContext.Current.CancellationToken));

        Assert.Equal("abc", _server.Content(Remote("papers/a.txt")));
    }

    [Fact]
    public void RenamesMadeWhileTheProviderWasStoppedGoToTheJournalOnceForAFolder()
    {
        _pc.AddPlaceholder(Local("papers"), new ItemIdentity("docs", true, 0, When), inSync: false);
        _pc.AddPlaceholder(Local("papers/a.txt"), new ItemIdentity("docs/a.txt", false, 3, When));
        _pc.AddPlaceholder(Local("papers/sub"), new ItemIdentity("docs/sub", true, 0, When));
        _pc.AddPlaceholder(Local("papers/sub/b.txt"), new ItemIdentity("docs/sub/b.txt", false, 3, When));
        _pc.AddPlaceholder(Local("renamed.txt"), new ItemIdentity("c.txt", false, 3, When), inSync: false);

        _uploads.FindChanges();

        var journal = new ChangeJournal(JournalFile);
        Assert.Equal(2, journal.Count);
        Assert.Equal(Rename("docs", "papers", isDirectory: true), journal.First);

        // The files wait to be checked, and the moved folders to learn their new paths.
        Assert.Equal(5, _uploads.Waiting);
    }

    [Fact]
    public void PathsOnTheVolumeMapToTheFolder()
    {
        var paths = new RootPaths(@"C:\Users\Ana\Remote Folders\Home", @"\Home");

        Assert.Equal(@"C:\Users\Ana\Remote Folders\Home\docs\a.txt", paths.FromVolume(@"\Home\docs\a.txt"));
        Assert.Equal(@"C:\Users\Ana\Remote Folders\Home", paths.FromVolume(@"\home"));
        Assert.Null(paths.FromVolume(@"\Homework\a.txt"));
        Assert.Equal("docs/a.txt", paths.Relative(@"C:\Users\Ana\Remote Folders\Home\docs\a.txt"));
        Assert.Equal(string.Empty, paths.Relative(@"C:\Users\Ana\Remote Folders\Home\"));
        Assert.Null(paths.Relative(@"C:\Users\Ana\Remote Folders\Homework\a.txt"));
    }

    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out.");
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
