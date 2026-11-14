using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class ListingDiffTests
{
    private static readonly DateTime When = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static LocalItem Placeholder(string relative, long size = 3, bool inSync = true) =>
        new(relative[(relative.LastIndexOf('/') + 1)..], @"C:\Remote\" + relative.Replace('/', '\\'), false,
            new PlaceholderState(inSync, PinState.Unspecified, 0, 0, [], System.IO.FileAttributes.Normal),
            new ItemIdentity(relative, false, size, When));

    [Fact]
    public void EntriesWithoutALocalItemAreNew()
    {
        var remote = new RemoteListing([new RemoteEntry("a.txt", false, 3, When), new RemoteEntry(".profile", false, 1, When), new RemoteEntry("docs", true, 0, When)], false);

        var changes = ListingDiff.Compute("work", remote, []);

        Assert.Equal(["a.txt", ".profile", "docs"], changes.New.Select(added => added.Entry.Name));
        Assert.Equal("work/a.txt", changes.New[0].Identity.RelativePath);
        Assert.Equal(changes.New[0].Identity, ItemIdentity.FromBytes(changes.New[0].Entry.Identity));
        Assert.True(changes.New[1].Entry.Hidden);
        Assert.True(changes.New[2].Entry.IsDirectory);
    }

    [Fact]
    public void AFileWithAnotherSizeOrTimeChanged()
    {
        var remote = new RemoteListing([new RemoteEntry("a.txt", false, 5, When), new RemoteEntry("b.txt", false, 3, When)], false);

        var changes = ListingDiff.Compute("", remote, [Placeholder("a.txt"), Placeholder("b.txt")]);

        var changed = Assert.Single(changes.Changed);
        Assert.Equal("a.txt", changed.Local.Name);
        Assert.Equal(5, changed.NewIdentity.Size);
        Assert.Empty(changes.New);
        Assert.Empty(changes.Gone);
    }

    [Fact]
    public void APlaceholderMissingOnTheServerIsGone()
    {
        var changes = ListingDiff.Compute("", new RemoteListing([], false), [Placeholder("a.txt")]);

        Assert.Equal("a.txt", Assert.Single(changes.Gone).Name);
    }

    [Fact]
    public void NothingIsGoneWhenTheListingWasCutShort()
    {
        var changes = ListingDiff.Compute("", new RemoteListing([], IsTruncated: true), [Placeholder("a.txt")]);

        Assert.Empty(changes.Gone);
    }

    [Fact]
    public void APlaceholderRenamedOnThePcIsntGone()
    {
        var renamed = Placeholder("a.txt") with { Name = "renamed.txt" };

        var changes = ListingDiff.Compute("", new RemoteListing([new RemoteEntry("a.txt", false, 3, When)], false), [renamed]);

        Assert.Empty(changes.Gone);
    }

    [Fact]
    public void NewFilesOnThePcAreLeftToTheUpload()
    {
        var plain = new LocalItem("new.txt", @"C:\Remote\new.txt", false, null, null);

        var changes = ListingDiff.Compute("", new RemoteListing([new RemoteEntry("new.txt", false, 9, When)], false), [plain]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void NamesWindowsCantShowAreCountedNotGone()
    {
        var remote = new RemoteListing([new RemoteEntry("a:b", false, 1, When), new RemoteEntry("A.txt", false, 3, When), new RemoteEntry("a.txt", false, 3, When)], false);

        var changes = ListingDiff.Compute("", remote, [Placeholder("a.txt")]);

        Assert.Equal(1, changes.Hidden[HiddenReason.InvalidName]);
        Assert.Equal(1, changes.Hidden[HiddenReason.SameNameOtherCase]);
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void LocalPathsUseTheServersFolderOnThePc()
    {
        Assert.Equal(@"C:\Remote\docs\a.txt", ListingDiff.LocalPath(@"C:\Remote", "docs/a.txt"));
        Assert.Equal(@"C:\Remote", ListingDiff.LocalPath(@"C:\Remote", ""));
    }
}

public sealed class ServerListingsTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\Remote Folders\Home";
    private readonly FakeRemoteServer _server = new();
    private readonly FakePlaceholderSystem _pc = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ServerListings _listings;

    public ServerListingsTests()
    {
        _listings = new ServerListings(Root, "/home/ana", new FakeConnections(_server), _pc, _time, NullLogger.Instance, 1);
        _server.AddFile("/home/ana/a.txt", "abc");
        _server.AddFolder("/home/ana/docs");
    }

    public void Dispose() => _server.Dispose();

    private static FakeListingRequest RootRequest() => new([]);

    [Fact]
    public async Task TheFirstListingAnswersWithTheServersEntriesAndMarksFiles()
    {
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.Equal(["a.txt", "docs"], request.Answered!.Select(entry => entry.Name).Order());
        Assert.Equal([System.IO.Path.Join(Root, "a.txt")], _pc.Marked);
    }

    [Fact]
    public async Task AListingTheProviderAsksForItselfIsAnsweredWithoutTheServer()
    {
        var request = new FakeListingRequest([]) { IsFromProvider = true };

        await _listings.HandleAsync(request);

        Assert.Empty(request.Answered!);
        Assert.False(request.Complete);
        Assert.Equal(0, _server.Listings);
        Assert.Empty(_listings.Listed);
        _time.Advance(ServerListings.ReuseFor);
        Assert.Empty(_pc.ListedAgain);
    }

    // Windows can't create a file in a folder it doesn't know is complete.
    [Fact]
    public async Task AnAnsweredFolderIsCompleteAndListedAgainWhenItsOpenedAfter30Seconds()
    {
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.True(request.Complete);
        _time.Advance(ServerListings.ReuseFor - TimeSpan.FromSeconds(1));
        Assert.Empty(_pc.ListedAgain);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([Root], _pc.ListedAgain);
    }

    [Fact]
    public async Task AFolderAnsweredAgainWaitsAnother30Seconds()
    {
        var first = RootRequest();
        await _listings.HandleAsync(first);
        _pc.Created(Root, first.Answered!);
        _time.Advance(TimeSpan.FromSeconds(20));

        var again = RootRequest();
        await _listings.HandleAsync(again);
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.True(again.Complete);
        Assert.Empty(_pc.ListedAgain);
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal([Root], _pc.ListedAgain);
    }

    [Fact]
    public async Task ARefusedListingLeavesTheFolderToBeListedAgain()
    {
        _server.Failure = RemoteError.Unreachable;
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.NotNull(request.Refused);
        Assert.Null(request.Complete);
        _time.Advance(ServerListings.ReuseFor);
        Assert.Empty(_pc.ListedAgain);
    }

    [Fact]
    public async Task NothingIsListedAgainOnceTheFolderIsDisconnected()
    {
        await _listings.HandleAsync(RootRequest());

        _listings.Dispose();
        _time.Advance(ServerListings.ReuseFor);

        Assert.Empty(_pc.ListedAgain);
    }

    [Fact]
    public async Task AListingIsReusedFor30Seconds()
    {
        var first = RootRequest();
        await _listings.HandleAsync(first);
        _pc.Created(Root, first.Answered!);

        var again = RootRequest();
        _time.Advance(TimeSpan.FromSeconds(20));
        await _listings.HandleAsync(again);

        Assert.Equal(1, _server.Listings);
        Assert.Empty(again.Answered!);
    }

    [Fact]
    public async Task ALaterListingShowsWhatsNewOnTheServer()
    {
        var first = RootRequest();
        await _listings.HandleAsync(first);
        _pc.Created(Root, first.Answered!);
        _server.AddFile("/home/ana/b.txt", "new");
        _time.Advance(ServerListings.ReuseFor);

        var later = RootRequest();
        await _listings.HandleAsync(later);

        Assert.Equal("b.txt", Assert.Single(later.Answered!).Name);
    }

    [Fact]
    public async Task AFolderNeverListedFailsWhenTheServerCantBeReached()
    {
        _server.Failure = RemoteError.Unreachable;
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.Equal(CloudFileStatus.NetworkUnavailable, request.Refused);
    }

    [Fact]
    public async Task AFolderListedBeforeKeepsItsEntriesWhenTheServerCantBeReached()
    {
        _pc.AddPlaceholder(System.IO.Path.Join(Root, "a.txt"), ItemIdentity.Of("", "a.txt", false, 3, DateTime.UnixEpoch));
        _server.Failure = RemoteError.Unreachable;
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.Null(request.Refused);
        Assert.Empty(request.Answered!);
        Assert.Empty(_pc.Deleted);
    }

    [Fact]
    public async Task ARefusedSignInFailsAsAuthentication()
    {
        _server.Failure = RemoteError.SignInFailed;
        var request = RootRequest();

        await _listings.HandleAsync(request);

        Assert.Equal(CloudFileStatus.AuthenticationFailed, request.Refused);
    }

    [Fact]
    public async Task ASlowListingIsAnsweredEmptyAndAppliedWhenItFinishes()
    {
        _server.ListingGate = new TaskCompletionSource();
        var request = RootRequest();

        var handling = _listings.HandleAsync(request);
        await WaitUntil(() => _server.Listings == 1);
        _time.Advance(ServerListings.FirstWait);
        await handling;

        Assert.Empty(request.Answered!);
        _server.ListingGate.SetResult();
        await WaitUntil(() => _pc.Local.Count == 2);
        Assert.True(_pc.Local.ContainsKey(System.IO.Path.Join(Root, "docs")));
    }

    [Fact]
    public async Task AChangedFileInSyncIsUpdatedAndOneWithChangesIsnt()
    {
        string a = System.IO.Path.Join(Root, "a.txt");
        string b = System.IO.Path.Join(Root, "b.txt");
        _pc.AddPlaceholder(a, ItemIdentity.Of("", "a.txt", false, 1, DateTime.UnixEpoch), onlineOnly: false);
        _pc.AddPlaceholder(b, ItemIdentity.Of("", "b.txt", false, 1, DateTime.UnixEpoch), inSync: false, onlineOnly: false);
        _server.AddFile("/home/ana/b.txt", "changed on the server");

        await _listings.HandleAsync(RootRequest());

        Assert.Equal([(a, PlaceholderUpdate.VerifyInSync | PlaceholderUpdate.MarkInSync | PlaceholderUpdate.Dehydrate)], _pc.Updates);
        Assert.Equal(3, ItemIdentity.FromBytes([.. _pc.Local[a].State!.Identity])!.Size);
    }

    [Fact]
    public async Task FilesDeletedOnTheServerLeaveThePcUnlessTheyHaveChanges()
    {
        string online = System.IO.Path.Join(Root, "online.txt");
        string downloaded = System.IO.Path.Join(Root, "downloaded.txt");
        string edited = System.IO.Path.Join(Root, "edited.txt");
        _pc.AddPlaceholder(online, ItemIdentity.Of("", "online.txt", false, 1, DateTime.UnixEpoch));
        _pc.AddPlaceholder(downloaded, ItemIdentity.Of("", "downloaded.txt", false, 1, DateTime.UnixEpoch), onlineOnly: false);
        _pc.AddPlaceholder(edited, ItemIdentity.Of("", "edited.txt", false, 1, DateTime.UnixEpoch), inSync: false, onlineOnly: false);

        await _listings.HandleAsync(RootRequest());

        Assert.Equal([online], _pc.Deleted);
        Assert.Equal([downloaded], _pc.Recycled);
        Assert.True(_pc.Local.ContainsKey(edited));
    }

    [Fact]
    public async Task APlaceholderRemoteFoldersDidntMakeIsRefused()
    {
        var request = new FakeListingRequest([9, 9, 9]);

        await _listings.HandleAsync(request);

        Assert.Equal(CloudFileStatus.Unsuccessful, request.Refused);
    }

    [Fact]
    public async Task ASubfolderIsListedByItsIdentity()
    {
        _server.AddFile("/home/ana/docs/readme.md", "read me");
        var request = new FakeListingRequest(ItemIdentity.Of("", "docs", true, 0, DateTime.UnixEpoch).ToBytes());

        await _listings.HandleAsync(request);

        var entry = Assert.Single(request.Answered!);
        Assert.Equal("readme.md", entry.Name);
        Assert.Equal("docs/readme.md", ItemIdentity.FromBytes(entry.Identity)!.RelativePath);
        Assert.Equal([System.IO.Path.Join(Root, @"docs\readme.md")], _pc.Marked);
    }

    [Fact]
    public async Task RefreshingCreatesTheNewEntriesItself()
    {
        Assert.True(await _listings.RefreshAsync(""));

        Assert.Equal(2, _pc.Local.Count);
        Assert.Contains("", _listings.Listed.Keys);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}

public sealed class ProviderServiceTests : IDisposable
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private readonly FakeSyncRootSystem _sync = new();
    private readonly ProviderService _service;

    public ProviderServiceTests()
    {
        _service = new ProviderService(_sync, Sid, (server, number) =>
            new ServerFolder(server, Sid, "icon.ico,0", _sync, new FakePlaceholderSystem(), new FakeConnections(new FakeRemoteServer()), System.IO.Path.Join(server.Folder, "..", "pcc-tests-journal-" + server.Id + ".json"), new ManualTimeProvider(), NullLogger.Instance, number), NullLogger.Instance);
    }

    public void Dispose() => _service.Dispose();

    private static RemoteServer Server(string id, string name = "Home") => new()
    {
        Id = id,
        Name = name,
        Host = "nas.example.lan",
        UserName = "ana",
        RemoteFolder = "/home/ana",
        Folder = System.IO.Path.Join(System.IO.Path.GetTempPath(), "pcc-tests-" + id),
        HostKey = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "key" },
    };

    private static string Id(string server) => $"PCCompanion!{Sid}!{server}";

    private static RemoteFoldersSettings Settings(params RemoteServer[] servers) => new() { Enabled = true, Servers = servers };

    [Fact]
    public void UsableServersAreRegisteredAndConnected()
    {
        var home = Server("00000000000000000000000000000001");
        try
        {
            _service.Apply(Settings(home, Server("00000000000000000000000000000002") with { HostKey = null }));

            Assert.Equal([Id(home.Id)], _sync.Registered.Keys);
            Assert.Equal("Home", _sync.Registered[Id(home.Id)].DisplayName);
            Assert.Equal([home.Folder], _sync.Connected);
        }
        finally
        {
            _service.Dispose();
            System.IO.Directory.Delete(home.Folder);
        }
    }

    [Fact]
    public void ANewNameRegistersTheFolderAgain()
    {
        var home = Server("00000000000000000000000000000001");
        try
        {
            _service.Apply(Settings(home));
            _service.Apply(Settings(home with { Name = "NAS" }));

            Assert.Equal("NAS", _sync.Registered[Id(home.Id)].DisplayName);
            Assert.Single(_sync.Connected);
        }
        finally
        {
            _service.Dispose();
            System.IO.Directory.Delete(home.Folder);
        }
    }

    [Fact]
    public void ARemovedServersFolderIsUnregistered()
    {
        var home = Server("00000000000000000000000000000001");
        try
        {
            _service.Apply(Settings(home));
            _service.Apply(Settings());

            Assert.Equal([Id(home.Id)], _sync.Unregistered);
            Assert.Empty(_sync.Connected);
        }
        finally
        {
            _service.Dispose();
            System.IO.Directory.Delete(home.Folder);
        }
    }

    [Fact]
    public void ChangedConnectionDetailsConnectAgain()
    {
        var home = Server("00000000000000000000000000000001");
        try
        {
            _service.Apply(Settings(home));
            var before = _service.Folders[home.Id];
            _service.Apply(Settings(home with { Port = 2222 }));

            Assert.NotSame(before, _service.Folders[home.Id]);
            Assert.Single(_sync.Connected);
            Assert.Empty(_sync.Unregistered);
        }
        finally
        {
            _service.Dispose();
            System.IO.Directory.Delete(home.Folder);
        }
    }

    [Fact]
    public void FoldersOfServersNoLongerInTheSettingsAreUnregisteredAtStart()
    {
        _sync.Registered[Id("0000000000000000000000000000dead")] = new SyncRootRegistration(Id("0000000000000000000000000000dead"), @"C:\old", "Old", "");
        _sync.Registered["OtherApp!" + Sid + "!x"] = new SyncRootRegistration("OtherApp!" + Sid + "!x", @"C:\other", "Other", "");

        _service.Apply(Settings());

        Assert.Equal([Id("0000000000000000000000000000dead")], _sync.Unregistered);
    }

    [Fact]
    public void AFolderThatCantStartLeavesTheOthersRunning()
    {
        var bad = Server("00000000000000000000000000000001");
        var good = Server("00000000000000000000000000000002");
        _sync.Refusal = registration => registration.Id == Id(bad.Id) ? new InvalidOperationException("not NTFS") : null;
        try
        {
            _service.Apply(Settings(bad, good));

            Assert.Equal([good.Folder], _sync.Connected);
            Assert.False(_service.Folders.ContainsKey(bad.Id));
        }
        finally
        {
            _service.Dispose();
            System.IO.Directory.Delete(bad.Folder);
            System.IO.Directory.Delete(good.Folder);
        }
    }
}
