using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class ProviderPipeTests : IDisposable
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string Id = "00000000000000000000000000000001";
    private readonly TemporaryFolder _data = new();
    private readonly FakeConnections _connections = new(new FakeRemoteServer());
    private readonly ServerFolder _folder;
    private readonly List<(RemoteServer Server, string? Secret, RemoteHostKey? Key)> _tests = [];
    private readonly ProviderPipe _pipe;
    private ServerKey _presented = new("ssh-ed25519", "current");

    public ProviderPipeTests()
    {
        var server = new RemoteServer
        {
            Id = Id,
            Name = "Home",
            Host = "nas.example.lan",
            UserName = "ana",
            RemoteFolder = "/home/ana",
            Folder = Path.Join(_data.FullPath, "Home"),
            HostKey = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "current" },
        };
        _folder = new ServerFolder(server, Sid, "icon.ico,0", new FakeSyncRootSystem(), new FakePlaceholderSystem(), _connections, Path.Join(_data.FullPath, "journal.json"), new ManualTimeProvider(), NullLogger.Instance, 1);
        _pipe = new ProviderPipe(
            () => [_folder],
            (server, secret, key, _) =>
            {
                _tests.Add((server, secret, key));
                return Task.FromResult(new RemoteTestResult { Outcome = RemoteTestOutcome.KeyToConfirm, PresentedKey = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "abc" } });
            },
            (_, _) => Task.FromResult(_presented),
            new ManualTimeProvider(),
            NullLogger.Instance);
    }

    public void Dispose()
    {
        _folder.Dispose();
        _data.Dispose();
    }

    private async Task<RemoteFoldersAnswer> AskAsync(RemoteFoldersRequest request) =>
        RemoteFoldersPipe.ReadAnswer(await _pipe.AnswerAsync(RemoteFoldersPipe.Write(request)))!;

    [Fact]
    public async Task TheStatusListsTheRunningServersWithTheirStates()
    {
        _connections.State.Failed(new RemoteServerException(RemoteError.KeyNotTrusted, "Another key.") { PresentedKey = new ServerKey("ssh-ed25519", "new") });

        var answer = await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.Status });

        var status = Assert.Single(answer.Servers);
        Assert.Equal(Id, status.Id);
        Assert.Equal(RemoteServerState.KeyChanged, status.State);
        Assert.Equal("new", status.PresentedKey!.Sha256);
        Assert.NotNull(status.LastFailure);
        Assert.Equal(0, status.ChangesWaiting);
    }

    [Fact]
    public async Task AMessageThatIsntARequestGetsAnEmptyAnswer()
    {
        var answer = RemoteFoldersPipe.ReadAnswer(await _pipe.AnswerAsync(Encoding.UTF8.GetBytes("{ not json")));

        Assert.NotNull(answer);
        Assert.False(answer.Done);
        Assert.Empty(answer.Servers);
    }

    [Fact]
    public async Task ATestGetsTheDetailsAndThePasswordFromTheDialog()
    {
        var server = new RemoteServer { Host = "new.example.lan", UserName = "ana" };

        var answer = await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.TestServer, Server = server, Secret = "typed" });

        Assert.Equal(RemoteTestOutcome.KeyToConfirm, answer.Test!.Outcome);
        var (tested, secret, key) = Assert.Single(_tests);
        Assert.Equal("new.example.lan", tested.Host);
        Assert.Equal("typed", secret);
        Assert.Null(key);
    }

    [Fact]
    public async Task SyncNowAndSignInAgainTryTheServerAgainWithoutClosingConnections()
    {
        _connections.State.Failed(new RemoteServerException(RemoteError.Unreachable, "Down."));
        Assert.True((await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.SyncNow })).Done);
        Assert.Null(_connections.State.FailFast());

        _connections.State.Failed(new RemoteServerException(RemoteError.SignInFailed, "Refused."));
        Assert.True((await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.SignInAgain, ServerId = Id })).Done);
        Assert.Null(_connections.State.FailFast());

        Assert.Equal(0, _connections.TriedAgain);
    }

    [Fact]
    public async Task ARequestForAServerThatIsntRunningIsntDone()
    {
        _connections.State.Failed(new RemoteServerException(RemoteError.SignInFailed, "Refused."));

        var answer = await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.SignInAgain, ServerId = "00000000000000000000000000000009" });

        Assert.False(answer.Done);
        Assert.NotNull(_connections.State.FailFast());
    }

    [Fact]
    public async Task ANewKeyIsTrustedOnlyIfTheServerPresentsItNow()
    {
        var ask = new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.CheckKey, ServerId = Id, Key = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "new" } };

        Assert.False((await AskAsync(ask)).KeyMatches);
        _presented = new ServerKey("ssh-ed25519", "new");
        Assert.True((await AskAsync(ask)).KeyMatches);
    }

    [Fact]
    public async Task ConflictsSeenAreNoLongerListed()
    {
        var answer = await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.ConflictsSeen, ServerId = Id });

        Assert.True(answer.Done);
        Assert.Empty(_folder.Uploads.Conflicts);
    }
}
