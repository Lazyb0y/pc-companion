using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class KeepingTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\Remote Folders\Home";
    private static readonly DateTime When = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly FakeRemoteServer _server = new();
    private readonly FakePlaceholderSystem _pc = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeConnections _connections;
    private readonly ServerListings _listings;
    private readonly ServerKeeping _keeping;

    public KeepingTests()
    {
        _connections = new FakeConnections(_server);
        _listings = new ServerListings(Root, "/home/ana", _connections, _pc, _time, NullLogger.Instance, 1);
        _keeping = new ServerKeeping(new RootPaths(Root, @"\Users\Ana\Remote Folders\Home"), _pc, _listings, _connections, _time, NullLogger.Instance, 1);
    }

    public void Dispose()
    {
        _listings.Dispose();
        _server.Dispose();
    }

    private static string Local(string relative) => Path.Join(Root, relative.Replace('/', '\\'));

    private static string Remote(string relative) => "/home/ana/" + relative;

    private void File(string relative, PinState pin = PinState.Unspecified, bool downloaded = false, bool inSync = true)
    {
        _pc.AddPlaceholder(Local(relative), new ItemIdentity(relative, false, 3, When), inSync, onlineOnly: !downloaded);
        _pc.Pin(Local(relative), pin);
    }

    private void Folder(string relative, PinState pin = PinState.Unspecified, bool isLink = false)
    {
        _pc.AddPlaceholder(Local(relative), new ItemIdentity(relative, true, 0, When) { IsLink = isLink });
        _pc.Pin(Local(relative), pin);
        _server.AddFolder(Remote(relative));
    }

    private Task<UploadOutcome> KeepAsync(string relative) => _keeping.KeepAsync(Local(relative), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AKeptFileIsDownloaded()
    {
        File("a.txt", PinState.Pinned);

        Assert.Equal(UploadOutcome.Done, await KeepAsync("a.txt"));

        Assert.Equal([Local("a.txt")], _pc.Hydrated);
    }

    [Fact]
    public async Task AFileInAKeptFolderIsDownloadedAndOneOutsideIsnt()
    {
        Folder("docs", PinState.Pinned);
        Folder("docs/sub");
        File("docs/sub/a.txt");
        File("b.txt");

        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs/sub/a.txt"));
        Assert.Equal(UploadOutcome.Done, await KeepAsync("b.txt"));

        Assert.Equal([Local("docs/sub/a.txt")], _pc.Hydrated);
    }

    [Fact]
    public async Task AKeptFolderIsListedAndWhatsNewOnTheServerIsKept()
    {
        Folder("docs", PinState.Pinned);
        _server.AddFile(Remote("docs/new.txt"), "new", When);

        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs"));

        Assert.NotNull(_pc.Read(Local("docs/new.txt")));
        Assert.Equal(1, _keeping.Waiting);
        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs/new.txt"));
        Assert.Equal([Local("docs/new.txt")], _pc.Hydrated);
    }

    [Fact]
    public async Task ALinkToAFolderIsntFollowed()
    {
        Folder("docs", PinState.Pinned);
        Folder("docs/link", isLink: true);
        File("docs/link/a.txt");

        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs/link"));
        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs/link/a.txt"));

        Assert.Empty(_pc.Hydrated);
        Assert.Equal(0, _server.Listings);
    }

    [Fact]
    public async Task FreeingUpSpaceRemovesACopyInSyncAndClearsThePin()
    {
        File("a.txt", PinState.Unpinned, downloaded: true);

        Assert.Equal(UploadOutcome.Done, await KeepAsync("a.txt"));

        Assert.Equal([Local("a.txt")], _pc.Dehydrated);
        Assert.Equal(PinState.Unspecified, _pc.Read(Local("a.txt"))!.Pin);
    }

    [Fact]
    public async Task FreeingUpSpaceNeverRemovesACopyWithChangesWaiting()
    {
        File("a.txt", PinState.Unpinned, downloaded: true, inSync: false);

        Assert.Equal(UploadOutcome.Done, await KeepAsync("a.txt"));

        Assert.Empty(_pc.Dehydrated);
        Assert.True(_pc.Read(Local("a.txt"))!.IsDownloaded);
        Assert.Equal(PinState.Unspecified, _pc.Read(Local("a.txt"))!.Pin);
    }

    [Fact]
    public async Task AFreedFolderFreesWhatsInIt()
    {
        Folder("docs", PinState.Unpinned);
        File("docs/a.txt", PinState.Unpinned, downloaded: true);

        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs"));
        Assert.Equal(1, _keeping.Waiting);
        Assert.Equal(UploadOutcome.Done, await KeepAsync("docs/a.txt"));

        Assert.Equal([Local("docs/a.txt")], _pc.Dehydrated);
    }

    [Fact]
    public async Task ACopyAProgramHasOpenIsFreedLater()
    {
        File("a.txt", PinState.Unpinned, downloaded: true);
        _pc.DehydrateResult = unchecked((int)0x80070020);

        Assert.Equal(UploadOutcome.Busy, await KeepAsync("a.txt"));

        Assert.Equal(PinState.Unpinned, _pc.Read(Local("a.txt"))!.Pin);
    }

    [Fact]
    public async Task AKeptFileWaitsWhileTheServerCantBeReached()
    {
        File("a.txt", PinState.Pinned);
        _pc.HydrateResult = unchecked((int)0x8007018C);
        _connections.State.Failed(new RemoteServerException(RemoteError.Unreachable, "Down."));

        Assert.Equal(UploadOutcome.Waiting, await KeepAsync("a.txt"));
    }

    [Fact]
    public void AtStartKeptAndFreedItemsAreFound()
    {
        Folder("docs", PinState.Pinned);
        File("docs/a.txt", PinState.Pinned);
        Folder("other");
        File("other/b.txt", PinState.Unpinned, downloaded: true);
        File("other/c.txt");

        Assert.Equal(2, _keeping.FindPinned(keptFoldersOnly: false));
        Assert.Equal(1, _keeping.FindPinned(keptFoldersOnly: true));
    }

    [Fact]
    public async Task TheWorkerKeepsWhatTheWatchReports()
    {
        File("a.txt", PinState.Pinned);
        using var stop = new CancellationTokenSource();
        var worker = _keeping.RunAsync(stop.Token);

        _keeping.Changed(Local("a.txt"));
        var until = DateTime.UtcNow.AddSeconds(10);
        while (_pc.Hydrated.Count == 0)
        {
            Assert.True(DateTime.UtcNow < until, "Timed out.");
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }
}
