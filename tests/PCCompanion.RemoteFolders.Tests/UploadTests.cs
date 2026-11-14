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

public sealed class UploadTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\Remote Folders\Home";
    private readonly FakeRemoteServer _server = new();
    private readonly FakePlaceholderSystem _pc = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeConnections _connections;
    private readonly ServerUploads _uploads;

    public UploadTests()
    {
        _connections = new FakeConnections(_server);
        _uploads = new ServerUploads(Root, "/home/ana", _connections, _pc, _time, NullLogger.Instance, 1);
    }

    public void Dispose() => _server.Dispose();

    private static string Local(string relative) => Path.Join(Root, relative.Replace('/', '\\'));

    private static string Remote(string relative) => "/home/ana/" + relative;

    private Task<UploadOutcome> UploadAsync(string relative) => _uploads.UploadAsync(Local(relative), TestContext.Current.CancellationToken);

    // A file downloaded from the server, then edited on the PC.
    private void Edited(string relative, string onServer, string onPc)
    {
        _server.AddFile(Remote(relative), onServer);
        var entry = _server.StatAsync(Remote(relative), CancellationToken.None).Result!;
        string folder = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
        _pc.AddPlaceholder(Local(relative), ItemIdentity.Of(folder, entry.Name, false, entry.Size, entry.LastWriteTimeUtc), inSync: false, onlineOnly: false);
        _pc.Contents[Local(relative)] = onPc;
    }

    private void AssertNoTemporaryFiles() => Assert.DoesNotContain(_server.Paths, path => path.Contains(RemoteNames.UploadPrefix, StringComparison.Ordinal));

    private ItemIdentity? IdentityOf(string relative) => _pc.Read(Local(relative)) is { } state ? ItemIdentity.FromBytes([.. state.Identity]) : null;

    [Fact]
    public async Task ANewFileIsUploadedAndBecomesAPlaceholderInSync()
    {
        _pc.AddNewFile(Local("notes.txt"), "from the PC");

        Assert.Equal(UploadOutcome.Done, await UploadAsync("notes.txt"));

        Assert.Equal("from the PC", _server.Content(Remote("notes.txt")));
        Assert.True(_pc.Read(Local("notes.txt"))!.InSync);
        var onServer = (await _server.StatAsync(Remote("notes.txt"), CancellationToken.None))!;
        Assert.Equal(new ItemIdentity("notes.txt", false, onServer.Size, onServer.LastWriteTimeUtc), IdentityOf("notes.txt"));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task AnEditedFileReplacesTheVersionItCameFrom()
    {
        Edited("docs/plan.txt", "old", "new on the PC");
        _pc.AddPlaceholder(Local("docs"), ItemIdentity.Of("", "docs", true, 0, DateTime.UnixEpoch));
        _server.AddFolder(Remote("docs"));

        Assert.Equal(UploadOutcome.Done, await UploadAsync("docs/plan.txt"));

        Assert.Equal("new on the PC", _server.Content(Remote("docs/plan.txt")));
        Assert.True(_pc.Read(Local("docs/plan.txt"))!.InSync);
        Assert.Equal(13, IdentityOf("docs/plan.txt")!.Size);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task AFileThatAlsoChangedOnTheServerIsKeptAsAConflictCopy()
    {
        Edited("plan.txt", "old", "new on the PC");
        _server.AddFile(Remote("plan.txt"), "new on the server", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        string? found = null;
        _uploads.ConflictFound += (_, e) => found = e.CopyPath;

        Assert.Equal(UploadOutcome.Conflict, await UploadAsync("plan.txt"));

        // The server's version keeps the name.
        Assert.Equal("new on the server", _server.Content(Remote("plan.txt")));
        var serverVersion = _pc.Read(Local("plan.txt"))!;
        Assert.True(serverVersion.InSync);
        Assert.False(serverVersion.IsDownloaded);
        Assert.Equal(17, IdentityOf("plan.txt")!.Size);
        Assert.Contains(Local("plan.txt"), _pc.Marked);

        // The PC's version is the conflict copy, on both sides.
        string copy = Assert.Single(_uploads.Conflicts);
        Assert.StartsWith("plan (", copy, StringComparison.Ordinal);
        Assert.EndsWith(").txt", copy, StringComparison.Ordinal);
        Assert.Equal("new on the PC", _server.Content(Remote(copy)));
        Assert.Equal("new on the PC", _pc.Contents[Local(copy)]);
        Assert.True(_pc.Read(Local(copy))!.InSync);
        Assert.Equal(copy, IdentityOf(copy)!.RelativePath);
        Assert.Equal(Local(copy), found);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ANewFileWhoseNameWasTakenOnTheServerIsKeptAsAConflictCopy()
    {
        _server.AddFile(Remote("report.txt"), "added on the server");
        _pc.AddNewFile(Local("report.txt"), "added on the PC");

        Assert.Equal(UploadOutcome.Conflict, await UploadAsync("report.txt"));

        Assert.Equal("added on the server", _server.Content(Remote("report.txt")));
        Assert.False(_pc.Read(Local("report.txt"))!.IsDownloaded);
        string copy = Assert.Single(_uploads.Conflicts);
        Assert.Equal("added on the PC", _server.Content(Remote(copy)));
        Assert.True(_pc.Read(Local(copy))!.InSync);
    }

    [Fact]
    public async Task AnotherConflictInTheSameMinuteGetsANumberedCopy()
    {
        Edited("plan.txt", "old", "first edit");
        _server.AddFile(Remote("plan.txt"), "changed on the server", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(UploadOutcome.Conflict, await UploadAsync("plan.txt"));
        string first = Assert.Single(_uploads.Conflicts);

        _pc.Local[Local("plan.txt")] = (false, FakePlaceholderSystem.NotInSync(_pc.Read(Local("plan.txt"))!));
        _pc.Contents[Local("plan.txt")] = "second edit";
        _server.AddFile(Remote("plan.txt"), "changed on the server again", new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(UploadOutcome.Conflict, await UploadAsync("plan.txt"));

        string second = Assert.Single(_uploads.Conflicts, copy => copy != first);
        Assert.Equal(first[..^".txt".Length] + " 2.txt", second);
        Assert.Equal("first edit", _server.Content(Remote(first)));
        Assert.Equal("second edit", _server.Content(Remote(second)));
    }

    [Fact]
    public async Task ACopyThatCouldntBeRenamedOnThePcIsntMadeTwice()
    {
        Edited("plan.txt", "old", "new on the PC");
        _server.AddFile(Remote("plan.txt"), "new on the server", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        _pc.OpenedBeforeRename = Local("plan.txt");

        Assert.Equal(UploadOutcome.Busy, await UploadAsync("plan.txt"));
        Assert.Empty(_uploads.Conflicts);
        Assert.Equal("new on the PC", _pc.Contents[Local("plan.txt")]);

        _pc.OpenedBeforeRename = null;
        Assert.Equal(UploadOutcome.Conflict, await UploadAsync("plan.txt"));

        string copy = Assert.Single(_uploads.Conflicts);
        Assert.Single(_server.Paths, path => path.StartsWith(Remote("plan ("), StringComparison.Ordinal));
        Assert.Equal("new on the PC", _server.Content(Remote(copy)));
        Assert.Equal("new on the server", _server.Content(Remote("plan.txt")));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task AnEditedFileDeletedOnTheServerIsUploadedAgain()
    {
        Edited("plan.txt", "old", "kept on the PC");
        _server.Remove(Remote("plan.txt"));

        Assert.Equal(UploadOutcome.Done, await UploadAsync("plan.txt"));

        Assert.Equal("kept on the PC", _server.Content(Remote("plan.txt")));
    }

    [Fact]
    public async Task AFileInAFolderDeletedOnTheServerMakesTheFolderAgain()
    {
        _pc.AddPlaceholder(Local("docs"), ItemIdentity.Of("", "docs", true, 0, DateTime.UnixEpoch));
        _pc.AddPlaceholder(Local("docs/sub"), ItemIdentity.Of("docs", "sub", true, 0, DateTime.UnixEpoch));
        _pc.AddNewFile(Local("docs/sub/new.txt"), "new");

        Assert.Equal(UploadOutcome.Done, await UploadAsync("docs/sub/new.txt"));

        Assert.Equal("new", _server.Content(Remote("docs/sub/new.txt")));
    }

    [Fact]
    public async Task AFileAProgramHasOpenWaits()
    {
        _pc.AddNewFile(Local("open.txt"), "being written");
        _pc.InUse.Add(Local("open.txt"));

        Assert.Equal(UploadOutcome.Busy, await UploadAsync("open.txt"));

        Assert.False(_server.Exists(Remote("open.txt")));
        Assert.Null(_pc.Read(Local("open.txt")));
    }

    [Fact]
    public async Task AFileAProgramOpensDuringItsUploadWaitsAndTheServerKeepsItsVersion()
    {
        Edited("plan.txt", "old", "new on the PC");
        _pc.OpenedDuringUpload = Local("plan.txt");

        Assert.Equal(UploadOutcome.Busy, await UploadAsync("plan.txt"));

        Assert.Equal("old", _server.Content(Remote("plan.txt")));
        Assert.False(_pc.Read(Local("plan.txt"))!.InSync);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task AFileAProgramOpensJustAfterItsUploadIsCheckedAgainWithoutAConflict()
    {
        Edited("plan.txt", "old", "new on the PC");
        _pc.OpenedBeforeMark = Local("plan.txt");

        Assert.Equal(UploadOutcome.Busy, await UploadAsync("plan.txt"));
        Assert.Equal("new on the PC", _server.Content(Remote("plan.txt")));
        Assert.False(_pc.Read(Local("plan.txt"))!.InSync);

        _pc.OpenedBeforeMark = null;
        _pc.Contents[Local("plan.txt")] = "edited again";
        Assert.Equal(UploadOutcome.Done, await UploadAsync("plan.txt"));
        Assert.Equal("edited again", _server.Content(Remote("plan.txt")));
        Assert.Empty(_uploads.Conflicts);
    }

    [Theory]
    [InlineData("~$report.docx")]
    [InlineData("Thumbs.db")]
    [InlineData("desktop.ini")]
    public async Task FilesThatStayOnThePcAreExcludedAndNeverUploaded(string name)
    {
        _pc.AddNewFile(Local(name), "temporary");

        Assert.Equal(UploadOutcome.Done, await UploadAsync(name));

        Assert.False(_server.Exists(Remote(name)));
        Assert.Contains(Local(name), _pc.Excluded);
    }

    [Fact]
    public async Task LinksAreNeitherUploadedNorFollowed()
    {
        _pc.AddNewFolder(Local("link"));
        _pc.Links.Add(Local("link"));
        _pc.AddNewFile(Local("link/inside.txt"), "through the link");

        Assert.Equal(UploadOutcome.Done, await UploadAsync("link"));
        Assert.Equal(UploadOutcome.Done, await UploadAsync("link/inside.txt"));

        Assert.False(_server.Exists(Remote("link")));
        Assert.False(_server.Exists(Remote("link/inside.txt")));
    }

    [Fact]
    public async Task ANewFolderIsMadeOnTheServerAndWhatsInItFollows()
    {
        _pc.AddNewFolder(Local("photos"));
        _pc.AddNewFile(Local("photos/a.jpg"), "a");

        Assert.Equal(UploadOutcome.Done, await UploadAsync("photos"));

        Assert.True(_server.Exists(Remote("photos")));
        Assert.Equal(ItemIdentity.Of("", "photos", true, 0, default).RelativePath, IdentityOf("photos")!.RelativePath);
        Assert.True(IdentityOf("photos")!.IsDirectory);
        Assert.Equal(1, _uploads.Waiting);
    }

    [Fact]
    public async Task AFileInANewFolderMakesTheFolderFirst()
    {
        _pc.AddNewFolder(Local("photos"));
        _pc.AddNewFolder(Local("photos/2026"));
        _pc.AddNewFile(Local("photos/2026/a.jpg"), "a");

        Assert.Equal(UploadOutcome.Done, await UploadAsync("photos/2026/a.jpg"));

        Assert.Equal("a", _server.Content(Remote("photos/2026/a.jpg")));
        Assert.NotNull(_pc.Read(Local("photos")));
        Assert.NotNull(_pc.Read(Local("photos/2026")));
    }

    [Fact]
    public async Task AFolderWhoseNameIsAFileOnTheServerFails()
    {
        _server.AddFile(Remote("photos"), "a file");
        _pc.AddNewFolder(Local("photos"));

        Assert.Equal(UploadOutcome.Failed, await UploadAsync("photos"));

        Assert.Null(_pc.Read(Local("photos")));
        Assert.Equal(RemoteError.Failed, _uploads.Failed["photos"]);
    }

    [Fact]
    public async Task AnUnreachableServerLeavesTheChangeWaiting()
    {
        _pc.AddNewFile(Local("notes.txt"), "from the PC");
        _server.Failure = RemoteError.Unreachable;

        Assert.Equal(UploadOutcome.Waiting, await UploadAsync("notes.txt"));

        Assert.Null(_pc.Read(Local("notes.txt")));
        Assert.Empty(_uploads.Failed);
    }

    [Fact]
    public async Task AFileTheServerRefusesIsListedAsFailed()
    {
        _pc.AddNewFile(Local("notes.txt"), "from the PC");
        _server.Failure = RemoteError.PermissionDenied;

        Assert.Equal(UploadOutcome.Failed, await UploadAsync("notes.txt"));

        Assert.Equal(RemoteError.PermissionDenied, _uploads.Failed["notes.txt"]);
        _server.Failure = null;
        Assert.Equal(UploadOutcome.Done, await UploadAsync("notes.txt"));
        Assert.Empty(_uploads.Failed);
    }

    [Fact]
    public async Task AFileInSyncOrGoneOrOutsideTheFolderIsLeftAlone()
    {
        _server.AddFile(Remote("same.txt"), "same");
        _pc.AddPlaceholder(Local("same.txt"), ItemIdentity.Of("", "same.txt", false, 4, DateTime.UnixEpoch), inSync: true);

        Assert.Equal(UploadOutcome.Done, await UploadAsync("same.txt"));
        Assert.Equal(UploadOutcome.Done, await UploadAsync("gone.txt"));
        Assert.Equal(UploadOutcome.Done, await _uploads.UploadAsync(@"C:\Users\Ana\elsewhere.txt", TestContext.Current.CancellationToken));
        Assert.Equal(UploadOutcome.Done, await _uploads.UploadAsync(Root, TestContext.Current.CancellationToken));

        Assert.Equal("same", _server.Content(Remote("same.txt")));
    }

    [Fact]
    public void ChangesFoundAtStartWait()
    {
        _pc.AddNewFile(Local("new.txt"), "new");
        Edited("edited.txt", "old", "new");
        _pc.AddPlaceholder(Local("same.txt"), ItemIdentity.Of("", "same.txt", false, 4, DateTime.UnixEpoch));

        _uploads.FindChanges();

        Assert.Equal(2, _uploads.Waiting);
    }

    [Fact]
    public void AFolderThatsOnTheServerIsntAChangeWaiting()
    {
        _pc.AddPlaceholder(Local("docs"), ItemIdentity.Of("", "docs", true, 0, default));
        _pc.AddNewFolder(Local("photos"));
        _pc.AddNewFile(Local("notes.txt"), "new");

        _uploads.Changed(Local("docs"));
        _uploads.Changed(Local("photos"));
        _uploads.Changed(Local("notes.txt"));
        _uploads.Changed(Local("gone.txt"));

        Assert.Equal(3, _uploads.Waiting);
    }

    [Fact]
    public async Task TheWorkerUploadsAChangeOnceItHasBeenQuiet()
    {
        _pc.AddNewFile(Local("notes.txt"), "from the PC");
        using var stop = new CancellationTokenSource();
        var worker = _uploads.RunAsync(stop.Token);
        var start = _time.Now;

        _uploads.Changed(Local("notes.txt"));
        _time.Advance(TimeSpan.FromSeconds(1));
        _uploads.Changed(Local("notes.txt"));
        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(_server.Exists(Remote("notes.txt")));

        await AdvanceUntilAsync(() => _server.Exists(Remote("notes.txt")));
        Assert.True(_time.Now >= start + TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => _uploads.Waiting == 0);
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheWorkerTriesAFileThatsOpenAgainLater()
    {
        _pc.AddNewFile(Local("open.txt"), "being written");
        _pc.InUse.Add(Local("open.txt"));
        using var stop = new CancellationTokenSource();
        var worker = _uploads.RunAsync(stop.Token);

        _uploads.Changed(Local("open.txt"));
        await AdvanceUntilAsync(() => _pc.UploadAttempts == 1);
        await WaitUntilAsync(() => _uploads.Waiting == 1);
        Assert.False(_server.Exists(Remote("open.txt")));

        _pc.InUse.Clear();
        await AdvanceUntilAsync(() => _server.Exists(Remote("open.txt")));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task QueuedChangesComeInTheOrderTheyBecomeDue()
    {
        var queue = new WaitingChanges(_time);
        queue.Changed("a");
        _time.Advance(TimeSpan.FromSeconds(1));
        queue.Changed("b");
        queue.Later("a", TimeSpan.FromSeconds(10));
        queue.Later("b", TimeSpan.Zero);

        var next = queue.NextAsync(TestContext.Current.CancellationToken);
        Assert.False(next.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("b", await next.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Equal(1, queue.Count);

        next = queue.NextAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal("a", await next.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Equal(0, queue.Count);
    }

    // The worker runs on its own thread: the clock moves a little at a time until it has done its part.
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

    [Fact]
    public async Task AWakeReturnsWithoutAPathWhenNothingIsDue()
    {
        var queue = new WaitingChanges(_time);
        queue.Changed("a");

        var next = queue.NextAsync(TestContext.Current.CancellationToken);
        queue.Wake();

        Assert.Null(await next.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.Equal(1, queue.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
