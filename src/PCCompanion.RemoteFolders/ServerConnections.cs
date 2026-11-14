using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.RemoteFolders;

/// <summary>A server's state, as the page and the PC Companion icon's menu show it.</summary>
public enum ServerStatus
{
    /// <summary>Nothing to do: no connection is open.</summary>
    NotConnected,

    Connected,

    /// <summary>The server couldn't be reached; requests fail at once for a while.</summary>
    Unreachable,

    /// <summary>The password, key or passphrase was refused; nothing is tried again until the user signs in again.</summary>
    SignInFailed,

    /// <summary>The server presented another key; nothing connects until the user trusts it.</summary>
    KeyChanged,

    /// <summary>The server offers none of the algorithms PC Companion allows.</summary>
    Error,
}

/// <summary>
/// A server's state, and when it's tried again. After it couldn't be reached, requests fail at
/// once for 30 seconds; while changes wait, it's tried on its own after 30 seconds, then 1, 2 and
/// every 5 minutes. A refused sign-in or a changed key isn't tried again until the user acts. See
/// "Offline and reconnecting" in docs/features/files/remote-folders.md.
/// </summary>
public sealed class ConnectionState(TimeProvider time)
{
    /// <summary>How long requests fail at once after the server couldn't be reached.</summary>
    public static readonly TimeSpan FailFastFor = TimeSpan.FromSeconds(30);

    /// <summary>When the server is tried again on its own while changes wait, after the first, second, third and later failures in a row.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    private readonly Lock _gate = new();
    private TaskCompletionSource _updated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ServerStatus _status = ServerStatus.NotConnected;
    private DateTimeOffset? _lastFailure;
    private ServerKey? _presentedKey;
    private int _failuresInARow;

    /// <summary>Raised when <see cref="Status"/> changes, on the thread that changed it.</summary>
    public event EventHandler? Changed;

    public ServerStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>When the server was last tried and couldn't be reached, refused the sign-in, or presented another key.</summary>
    public DateTimeOffset? LastFailure
    {
        get
        {
            lock (_gate)
            {
                return _lastFailure;
            }
        }
    }

    /// <summary>For <see cref="ServerStatus.KeyChanged"/>: the key the server presented.</summary>
    public ServerKey? PresentedKey
    {
        get
        {
            lock (_gate)
            {
                return _presentedKey;
            }
        }
    }

    /// <summary>When the server is tried again on its own while changes wait; <see langword="null"/> when it isn't failing, or waits for the user.</summary>
    public DateTimeOffset? NextRetry
    {
        get
        {
            lock (_gate)
            {
                return NextRetryLocked();
            }
        }
    }

    /// <summary>
    /// The error a request fails with at once, without connecting, or <see langword="null"/> when
    /// it may try: the sign-in was refused, the key changed, or the server couldn't be reached less
    /// than 30 seconds ago.
    /// </summary>
    public RemoteServerException? FailFast()
    {
        lock (_gate)
        {
            return _status switch
            {
                ServerStatus.SignInFailed => new RemoteServerException(RemoteError.SignInFailed, "The sign-in was refused; the user signs in again."),
                ServerStatus.KeyChanged => new RemoteServerException(RemoteError.KeyNotTrusted, "The server presented another key.") { PresentedKey = _presentedKey },
                ServerStatus.Unreachable or ServerStatus.Error when _lastFailure is { } last && time.GetUtcNow() - last < FailFastFor => new RemoteServerException(
                    _status == ServerStatus.Error ? RemoteError.NoSafeAlgorithm : RemoteError.Unreachable,
                    "The server couldn't be reached a moment ago."),
                _ => null,
            };
        }
    }

    /// <summary>Records a connection that opened, or a request that worked.</summary>
    public void Succeeded() => Set(ServerStatus.Connected, wake: false, () =>
    {
        _failuresInARow = 0;
        _lastFailure = null;
        _presentedKey = null;
    });

    /// <summary>Records that every connection closed, with nothing wrong.</summary>
    public void Closed()
    {
        lock (_gate)
        {
            if (_status != ServerStatus.Connected)
            {
                return;
            }
        }

        Set(ServerStatus.NotConnected, wake: false, () => { });
    }

    /// <summary>
    /// Records a failure. One that's about the server, such as a refused sign-in, changes the
    /// state; one that's about a request, such as for a missing file, doesn't.
    /// </summary>
    public void Failed(RemoteServerException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ServerStatus? status = exception.Error switch
        {
            RemoteError.Unreachable => ServerStatus.Unreachable,
            RemoteError.SignInFailed or RemoteError.KeyFile => ServerStatus.SignInFailed,
            RemoteError.KeyNotTrusted => ServerStatus.KeyChanged,
            RemoteError.NoSafeAlgorithm => ServerStatus.Error,
            _ => null,
        };
        if (status is not { } known)
        {
            return;
        }

        Set(known, wake: true, () =>
        {
            _failuresInARow = known is ServerStatus.Unreachable or ServerStatus.Error ? _failuresInARow + 1 : 0;
            _lastFailure = time.GetUtcNow();
            _presentedKey = exception.PresentedKey;
        });
    }

    /// <summary>Lets the server be tried again at once: the network changed, the PC woke from sleep, or the user signed in again.</summary>
    public void TryAgainNow()
    {
        ServerStatus status;
        lock (_gate)
        {
            status = _status == ServerStatus.Connected ? ServerStatus.Connected : ServerStatus.NotConnected;
        }

        Set(status, wake: true, () =>
        {
            _failuresInARow = 0;
            _lastFailure = null;
            _presentedKey = null;
        });
    }

    /// <summary>
    /// Waits until the server may be tried again on its own, for work that waits for it, such as
    /// uploads: until <see cref="NextRetry"/>, or until the state changes. Returns at once when it
    /// may be tried now.
    /// </summary>
    public async Task WaitForRetryAsync(CancellationToken cancellation)
    {
        Task updated;
        TimeSpan? wait;
        lock (_gate)
        {
            updated = _updated.Task;
            wait = _status is ServerStatus.SignInFailed or ServerStatus.KeyChanged
                ? null
                : NextRetryLocked() is { } next ? next - time.GetUtcNow() : TimeSpan.Zero;
        }

        if (wait is null)
        {
            await updated.WaitAsync(cancellation).ConfigureAwait(false);
        }
        else if (wait > TimeSpan.Zero)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            await Task.WhenAny(updated, Task.Delay(wait.Value, time, stop.Token)).ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
        }
    }

    private DateTimeOffset? NextRetryLocked() =>
        _status is ServerStatus.Unreachable or ServerStatus.Error && _lastFailure is { } last && _failuresInARow > 0
            ? last + RetryDelays[Math.Min(_failuresInARow, RetryDelays.Count) - 1]
            : null;

    // Waiters for a retry wake when a failure moves the next try, or the user lets it try now.
    private void Set(ServerStatus status, bool wake, Action update)
    {
        bool changed;
        TaskCompletionSource? woken = null;
        lock (_gate)
        {
            changed = _status != status;
            _status = status;
            update();
            if (changed || wake)
            {
                woken = _updated;
                _updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        woken?.TrySetResult();
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// The connections to one server: one for listings and changes and two for downloads, each opened
/// when it's first needed, again after it drops, and closed after 5 minutes without use. Requests
/// fail at once while <see cref="ConnectionState"/> says so.
/// </summary>
public sealed partial class ServerConnections : IServerConnections
{
    /// <summary>How long a connection stays open without use.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How many downloads run at once, each on a connection of its own.</summary>
    public const int TransferConnections = 2;

    private readonly Func<CancellationToken, Task<IRemoteServer>> _connect;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly int _number;
    private readonly Slot _control;
    private readonly Slot[] _transfers;
    private readonly Channel<Slot> _freeTransfers = Channel.CreateUnbounded<Slot>();

    /// <param name="connect">Opens a connection, and throws <see cref="RemoteServerException"/> when it can't.</param>
    public ServerConnections(Func<CancellationToken, Task<IRemoteServer>> connect, TimeProvider time, ILogger logger, int number)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _connect = connect;
        _time = time;
        _logger = logger;
        _number = number;
        State = new ConnectionState(time);
        State.Changed += (_, _) => LogStatus(_logger, _number, State.Status);
        _control = new Slot(this);
        _transfers = new Slot[TransferConnections];
        for (int i = 0; i < _transfers.Length; i++)
        {
            _transfers[i] = new Slot(this);
            _freeTransfers.Writer.TryWrite(_transfers[i]);
        }
    }

    public ConnectionState State { get; }

    /// <summary>The connections to an SFTP server, with the password or passphrase from Credential Manager.</summary>
    public static ServerConnections ForSftp(RemoteServer server, Func<string, string?> readSecret, TimeProvider time, ILogger logger, int number)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(readSecret);

        return new ServerConnections(ConnectAsync, time, logger, number);

        async Task<IRemoteServer> ConnectAsync(CancellationToken cancellation)
        {
            string? secret = readSecret(RemoteFoldersProcess.CredentialTarget(server.Id));
            var signIn = server.KnownSignIn == RemoteSignIn.Key
                ? new SftpSignIn(server.Host, server.Port, server.UserName, null, server.KeyFile, secret)
                : new SftpSignIn(server.Host, server.Port, server.UserName, secret, null, null);
            var key = new ServerKey(server.HostKey!.Type, server.HostKey.Sha256);
            return await SftpServer.ConnectAsync(signIn, key, cancellation).ConfigureAwait(false);
        }
    }

    public Task<T> UseAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation) => UseAsync(_control, work, cancellation);

    public async Task<T> UseForTransferAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation)
    {
        ThrowIfFailing();
        Slot slot;
        try
        {
            slot = await _freeTransfers.Reader.ReadAsync(cancellation).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new ObjectDisposedException(nameof(ServerConnections));
        }

        try
        {
            return await UseAsync(slot, work, cancellation).ConfigureAwait(false);
        }
        finally
        {
            _freeTransfers.Writer.TryWrite(slot);
        }
    }

    public void TryAgainNow()
    {
        _control.CloseIfIdle();
        foreach (var slot in _transfers)
        {
            slot.CloseIfIdle();
        }

        State.TryAgainNow();
    }

    public void Dispose()
    {
        _freeTransfers.Writer.TryComplete();
        _control.Dispose();
        foreach (var slot in _transfers)
        {
            slot.Dispose();
        }
    }

    private void ThrowIfFailing()
    {
        if (State.FailFast() is { } failure)
        {
            throw failure;
        }
    }

    private async Task<T> UseAsync<T>(Slot slot, Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation)
    {
        ThrowIfFailing();
        await slot.Gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(slot.IsDisposed, this);
            if (slot.Connection is not { IsConnected: true })
            {
                slot.Close();

                // Requests that waited for a connection that failed fail at once too.
                ThrowIfFailing();
                slot.Connection = await ConnectAsync(cancellation).ConfigureAwait(false);
            }

            try
            {
                var result = await work(slot.Connection, cancellation).ConfigureAwait(false);
                State.Succeeded();
                return result;
            }
            catch (RemoteServerException exception)
            {
                State.Failed(exception);
                if (exception.Error == RemoteError.Unreachable)
                {
                    slot.Close();
                }

                throw;
            }
        }
        finally
        {
            slot.CloseLater();
            slot.Gate.Release();
        }
    }

    private async Task<IRemoteServer> ConnectAsync(CancellationToken cancellation)
    {
        try
        {
            var connection = await _connect(cancellation).ConfigureAwait(false);
            State.Succeeded();
            return connection;
        }
        catch (RemoteServerException exception)
        {
            string kind = exception.InnerException?.GetType().Name ?? "none";
            LogConnectFailed(_logger, _number, exception.Error, kind);
            State.Failed(exception);
            throw;
        }
    }

    // Once every connection closed for being idle, the server is no longer connected.
    private void ClosedOne()
    {
        if (_control.Connection is null && Array.TrueForAll(_transfers, slot => slot.Connection is null))
        {
            State.Closed();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: {Status}.")]
    private static partial void LogStatus(ILogger logger, int server, ServerStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: connecting failed ({Error}, {Kind}).")]
    private static partial void LogConnectFailed(ILogger logger, int server, RemoteError error, string kind);

    // One connection, used by one request at a time, and closed after a while without use.
    private sealed class Slot : IDisposable
    {
        private readonly ServerConnections _owner;
        private readonly ITimer _idle;

        public Slot(ServerConnections owner)
        {
            _owner = owner;
            _idle = owner._time.CreateTimer(_ => CloseIfIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public IRemoteServer? Connection { get; set; }

        public bool IsDisposed { get; private set; }

        public void CloseLater()
        {
            if (!IsDisposed && Connection is not null)
            {
                _idle.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        public void Close()
        {
            Connection?.Dispose();
            Connection = null;
        }

        // A connection that's in use stays: its request ends, or fails, on its own.
        public void CloseIfIdle()
        {
            if (!Gate.Wait(0))
            {
                return;
            }

            try
            {
                if (!IsDisposed && Connection is not null)
                {
                    Close();
                    _owner.ClosedOne();
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        public void Dispose()
        {
            Gate.Wait();
            try
            {
                IsDisposed = true;
                _idle.Dispose();
                Close();
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
