using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class ConnectionTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly List<FakeRemoteServer> _opened = [];
    private readonly ServerConnections _connections;
    private RemoteServerException? _connectFailure;
    private TaskCompletionSource? _connectGate;
    private int _attempts;

    public ConnectionTests()
    {
        _connections = new ServerConnections(ConnectAsync, _time, NullLogger.Instance, 1);
    }

    public void Dispose() => _connections.Dispose();

    [Fact]
    public async Task AConnectionOpensWhenFirstNeededAndIsUsedAgain()
    {
        Assert.Equal(ServerStatus.NotConnected, _connections.State.Status);

        await ListAsync();
        await ListAsync();

        Assert.Equal(1, _attempts);
        Assert.Equal(2, _opened[0].Listings);
        Assert.Equal(ServerStatus.Connected, _connections.State.Status);
    }

    [Fact]
    public async Task AConnectionThatDroppedIsOpenedAgain()
    {
        await ListAsync();
        _opened[0].IsConnected = false;

        await ListAsync();

        Assert.Equal(2, _attempts);
        Assert.Equal(1, _opened[1].Listings);
    }

    [Fact]
    public async Task AnUnreachableServerFailsRequestsAtOnceFor30Seconds()
    {
        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Refused.");

        await AssertFailsAsync(RemoteError.Unreachable);
        Assert.Equal(ServerStatus.Unreachable, _connections.State.Status);
        Assert.Equal(_time.Now, _connections.State.LastFailure);

        _time.Advance(TimeSpan.FromSeconds(29));
        await AssertFailsAsync(RemoteError.Unreachable);
        await Assert.ThrowsAsync<RemoteServerException>(() => _connections.UseForTransferAsync((server, _) => Task.FromResult(0), CancellationToken.None));
        Assert.Equal(1, _attempts);

        _time.Advance(TimeSpan.FromSeconds(1));
        _connectFailure = null;
        await ListAsync();
        Assert.Equal(2, _attempts);
        Assert.Equal(ServerStatus.Connected, _connections.State.Status);
        Assert.Null(_connections.State.LastFailure);
    }

    [Fact]
    public async Task RequestsThatWaitedForAConnectionThatFailedFailAtOnce()
    {
        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Timed out.");
        _connectGate = new TaskCompletionSource();

        var first = ListAsync();
        var second = ListAsync();
        _connectGate.SetResult();

        await Assert.ThrowsAsync<RemoteServerException>(() => first);
        await Assert.ThrowsAsync<RemoteServerException>(() => second);
        Assert.Equal(1, _attempts);
    }

    [Fact]
    public async Task AServerThatDropsDuringARequestIsUnreachableAndItsConnectionCloses()
    {
        await ListAsync();
        _opened[0].Failure = RemoteError.Unreachable;

        await AssertFailsAsync(RemoteError.Unreachable);

        Assert.False(_opened[0].IsConnected);
        Assert.Equal(ServerStatus.Unreachable, _connections.State.Status);
    }

    [Fact]
    public async Task ARefusedRequestLeavesTheServerConnected()
    {
        await ListAsync();
        _opened[0].Failure = RemoteError.NotFound;

        await AssertFailsAsync(RemoteError.NotFound);

        Assert.True(_opened[0].IsConnected);
        Assert.Equal(ServerStatus.Connected, _connections.State.Status);
        _opened[0].Failure = null;
        await ListAsync();
        Assert.Equal(1, _attempts);
    }

    [Theory]
    [InlineData(RemoteError.SignInFailed, ServerStatus.SignInFailed)]
    [InlineData(RemoteError.KeyFile, ServerStatus.SignInFailed)]
    [InlineData(RemoteError.KeyNotTrusted, ServerStatus.KeyChanged)]
    public async Task ARefusedSignInOrAChangedKeyIsntTriedAgainUntilTheUserActs(RemoteError error, ServerStatus status)
    {
        _connectFailure = new RemoteServerException(error, "Refused.");

        await AssertFailsAsync(error);
        _time.Advance(TimeSpan.FromHours(1));
        await AssertFailsAsync(error == RemoteError.KeyFile ? RemoteError.SignInFailed : error);

        Assert.Equal(1, _attempts);
        Assert.Equal(status, _connections.State.Status);
        Assert.Null(_connections.State.NextRetry);

        _connectFailure = null;
        _connections.TryAgainNow();
        Assert.Equal(ServerStatus.NotConnected, _connections.State.Status);
        await ListAsync();
        Assert.Equal(2, _attempts);
    }

    [Fact]
    public async Task AChangedKeyIsKeptForTheUserToCompare()
    {
        var presented = new ServerKey("ssh-ed25519", "bmV3IGtleQ");
        _connectFailure = new RemoteServerException(RemoteError.KeyNotTrusted, "Another key.") { PresentedKey = presented };

        await AssertFailsAsync(RemoteError.KeyNotTrusted);
        var again = await Assert.ThrowsAsync<RemoteServerException>(ListAsync);

        Assert.Equal(presented, _connections.State.PresentedKey);
        Assert.Equal(presented, again.PresentedKey);
    }

    [Fact]
    public async Task NoSafeAlgorithmIsAnErrorThatFailsFastLikeAnUnreachableServer()
    {
        _connectFailure = new RemoteServerException(RemoteError.NoSafeAlgorithm, "Only old ciphers.");

        await AssertFailsAsync(RemoteError.NoSafeAlgorithm);
        await AssertFailsAsync(RemoteError.NoSafeAlgorithm);

        Assert.Equal(ServerStatus.Error, _connections.State.Status);
        Assert.Equal(1, _attempts);
        Assert.NotNull(_connections.State.NextRetry);
    }

    [Fact]
    public async Task RetriesWaitLongerAfterEachFailureInARow()
    {
        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Refused.");
        var waits = new List<TimeSpan>();
        for (int i = 0; i < 6; i++)
        {
            await AssertFailsAsync(RemoteError.Unreachable);
            var next = _connections.State.NextRetry!.Value;
            waits.Add(next - _time.Now);
            _time.Now = next;
        }

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)],
            waits);

        _connectFailure = null;
        await ListAsync();
        Assert.Null(_connections.State.NextRetry);
    }

    [Fact]
    public async Task WaitingForARetryEndsAtTheNextTry()
    {
        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Refused.");
        await AssertFailsAsync(RemoteError.Unreachable);

        var waiting = _connections.State.WaitForRetryAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(waiting.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitingForARetryEndsAtOnceWhenTheUserLetsItTry()
    {
        _connectFailure = new RemoteServerException(RemoteError.SignInFailed, "Refused.");
        await AssertFailsAsync(RemoteError.SignInFailed);

        var waiting = _connections.State.WaitForRetryAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(1));
        Assert.False(waiting.IsCompleted);
        _connections.TryAgainNow();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitingForARetryReturnsAtOnceWhenTheServerIsntFailing()
    {
        await _connections.State.WaitForRetryAsync(CancellationToken.None);
        await ListAsync();
        await _connections.State.WaitForRetryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitingForARetryCanBeCancelled()
    {
        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Refused.");
        await AssertFailsAsync(RemoteError.Unreachable);
        using var cancel = new CancellationTokenSource();

        var waiting = _connections.State.WaitForRetryAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task AConnectionClosesAfterFiveMinutesWithoutUse()
    {
        await ListAsync();
        _time.Advance(TimeSpan.FromMinutes(4));
        await ListAsync();

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(_opened[0].IsConnected);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(_opened[0].IsConnected);
        Assert.Equal(ServerStatus.NotConnected, _connections.State.Status);

        await ListAsync();
        Assert.Equal(2, _attempts);
    }

    [Fact]
    public async Task TheServerStaysConnectedWhileADownloadConnectionIsOpen()
    {
        await ListAsync();
        _time.Advance(TimeSpan.FromMinutes(3));
        await _connections.UseForTransferAsync((server, _) => Task.FromResult(0), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(_opened[0].IsConnected);
        Assert.Equal(ServerStatus.Connected, _connections.State.Status);

        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(ServerStatus.NotConnected, _connections.State.Status);
    }

    [Fact]
    public async Task TwoDownloadsRunAtOnceOnConnectionsOfTheirOwnAndAThirdWaits()
    {
        await ListAsync();
        var release = new TaskCompletionSource();
        var used = new List<IRemoteServer>();
        Task<int> Download() => _connections.UseForTransferAsync(
            async (server, _) =>
            {
                lock (used)
                {
                    used.Add(server);
                }

                await release.Task;
                return 0;
            },
            CancellationToken.None);

        var first = Download();
        var second = Download();
        var third = Download();
        Assert.Equal(3, _attempts);
        Assert.False(third.IsCompleted);
        lock (used)
        {
            Assert.Equal(2, used.Count);
            Assert.NotSame(used[0], used[1]);
            Assert.DoesNotContain((IRemoteServer)_opened[0], used);
        }

        release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal(3, _attempts);
        Assert.Equal(3, used.Count);
    }

    [Fact]
    public async Task TryingAgainClosesConnectionsThatArentInUseAndClearsTheWait()
    {
        await ListAsync();
        _connections.TryAgainNow();
        Assert.False(_opened[0].IsConnected);

        _connectFailure = new RemoteServerException(RemoteError.Unreachable, "Refused.");
        await AssertFailsAsync(RemoteError.Unreachable);
        _connections.TryAgainNow();
        Assert.Null(_connections.State.NextRetry);
        _connectFailure = null;

        await ListAsync();
        Assert.Equal(3, _attempts);
    }

    [Fact]
    public async Task TryingAgainLeavesAConnectionInUseOpen()
    {
        await ListAsync();
        var release = new TaskCompletionSource();
        var listing = _connections.UseAsync(
            async (server, _) =>
            {
                await release.Task;
                return 0;
            },
            CancellationToken.None);

        _connections.TryAgainNow();
        Assert.True(_opened[0].IsConnected);

        release.SetResult();
        await listing;
    }

    [Fact]
    public async Task StateChangesAreReported()
    {
        var reported = new List<ServerStatus>();
        _connections.State.Changed += (_, _) => reported.Add(_connections.State.Status);

        await ListAsync();
        await ListAsync();
        _opened[0].Failure = RemoteError.Unreachable;
        await AssertFailsAsync(RemoteError.Unreachable);
        _connections.TryAgainNow();

        Assert.Equal([ServerStatus.Connected, ServerStatus.Unreachable, ServerStatus.NotConnected], reported);
    }

    [Fact]
    public async Task ADisposedConnectionRefusesWork()
    {
        await ListAsync();
        _connections.Dispose();

        Assert.False(_opened[0].IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(ListAsync);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _connections.UseForTransferAsync((server, _) => Task.FromResult(0), CancellationToken.None));
    }

    private Task<RemoteListing> ListAsync() => _connections.UseAsync((server, cancellation) => server.ListAsync("/home/ana", 10, cancellation), CancellationToken.None);

    private async Task AssertFailsAsync(RemoteError error)
    {
        var exception = await Assert.ThrowsAsync<RemoteServerException>(ListAsync);
        Assert.Equal(error, exception.Error);
    }

    private async Task<IRemoteServer> ConnectAsync(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _attempts);
        if (_connectGate is { } gate)
        {
            await gate.Task;
        }

        if (_connectFailure is { } failure)
        {
            throw failure;
        }

        var server = new FakeRemoteServer();
        lock (_opened)
        {
            _opened.Add(server);
        }

        return server;
    }
}
