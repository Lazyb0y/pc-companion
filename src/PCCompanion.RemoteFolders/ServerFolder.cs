using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// One server's folder: registered with Windows as a sync root and connected, so File Explorer
/// shows it, and Windows' requests about it are answered here. See docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerFolder : ICloudFileRequests, IDisposable
{
    private readonly RemoteServer _server;
    private readonly string _iconResource;
    private readonly ISyncRootSystem _sync;
    private readonly IServerConnections _connections;
    private readonly ILogger _logger;
    private readonly int _number;
    private IDisposable? _connection;

    public ServerFolder(RemoteServer server, string userSid, string iconResource, ISyncRootSystem sync, IPlaceholderSystem pc, IServerConnections connections, TimeProvider time, ILogger logger, int number)
    {
        _server = server;
        _iconResource = iconResource;
        _sync = sync;
        _connections = connections;
        _logger = logger;
        _number = number;
        Id = SyncRoots.MakeId(RemoteFoldersProcess.ProviderName, userSid, server.Id);
        Listings = new ServerListings(server.Folder, server.RemoteFolder, connections, pc, time, logger, number);
    }

    /// <summary>The sync root's ID.</summary>
    public string Id { get; }

    /// <summary>The server's settings it was made with.</summary>
    public RemoteServer Server => _server;

    public ServerListings Listings { get; }

    /// <summary>Makes the folder if it's missing, registers it, again when it was, and connects to it.</summary>
    /// <exception cref="Exception">Windows refused, for example for a folder that isn't on an NTFS drive.</exception>
    public void Start()
    {
        Directory.CreateDirectory(_server.Folder);
        _sync.Register(new SyncRootRegistration(Id, _server.Folder, _server.Name, _iconResource));
        _connection = _sync.Connect(_server.Folder, this, exception => LogRequestFailed(_logger, _number, exception));
        LogStarted(_logger, _number);
    }

    /// <summary>Registers the folder again with a new name; its files stay.</summary>
    public void Rename(string name) => _sync.Register(new SyncRootRegistration(Id, _server.Folder, name, _iconResource));

    /// <summary>
    /// Disconnects and unregisters the folder, as removing the server does: online-only files
    /// disappear, and files on the PC stay in what's now an ordinary folder.
    /// </summary>
    public void Remove()
    {
        Dispose();
        _sync.Unregister(Id);
        LogRemoved(_logger, _number);
    }

    /// <summary>Disconnects; the folder stays registered.</summary>
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _connections.Dispose();
    }

    public void FetchPlaceholders(FetchPlaceholdersRequest request) => _ = Task.Run(() => Listings.HandleAsync(new ListingRequest(request)));

    public void FetchData(FetchDataRequest request) => request.Refuse(CloudFileStatus.Unsuccessful);

    public void CancelFetchData(CancelFetchDataNotice notice)
    {
    }

    // PC Companion's own changes, such as removing a file deleted on the server, go ahead.
    public void Delete(DeleteRequest request)
    {
        if (IsOwn(request))
        {
            request.Allow();
        }
        else
        {
            request.Refuse(CloudFileStatus.AccessDenied);
        }
    }

    public void Rename(RenameRequest request)
    {
        if (IsOwn(request))
        {
            request.Allow();
        }
        else
        {
            request.Refuse(CloudFileStatus.AccessDenied);
        }
    }

    // Windows removes only copies that are in sync, so nothing waiting for the server is lost.
    public void Dehydrate(DehydrateRequest request) => request.Allow();

    public void FileClosed(FileClosedNotice notice)
    {
    }

    private static bool IsOwn(CloudFileRequest request) => request.Program?.ProcessId == Environment.ProcessId;

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: its folder is connected.")]
    private static partial void LogStarted(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: its folder is unregistered.")]
    private static partial void LogRemoved(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a request from Windows failed, and was refused.")]
    private static partial void LogRequestFailed(ILogger logger, int server, Exception exception);
}

/// <summary>
/// The connection to one SFTP server, opened when it's first needed and again after it drops, with
/// the password or passphrase from Credential Manager.
/// </summary>
public sealed class SftpConnections(RemoteServer server, Func<string, string?> readSecret) : IServerConnections
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRemoteServer? _connection;
    private bool _disposed;

    public async Task<T> UseAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not { IsConnected: true })
            {
                _connection?.Dispose();
                _connection = null;
                _connection = await ConnectAsync(cancellation).ConfigureAwait(false);
            }

            try
            {
                return await work(_connection, cancellation).ConfigureAwait(false);
            }
            catch (RemoteServerException exception) when (exception.Error == RemoteError.Unreachable)
            {
                _connection.Dispose();
                _connection = null;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IRemoteServer> ConnectAsync(CancellationToken cancellation)
    {
        string? secret = readSecret(RemoteFoldersProcess.CredentialTarget(server.Id));
        var signIn = server.KnownSignIn == RemoteSignIn.Key
            ? new SftpSignIn(server.Host, server.Port, server.UserName, null, server.KeyFile, secret)
            : new SftpSignIn(server.Host, server.Port, server.UserName, secret, null, null);
        var key = new ServerKey(server.HostKey!.Type, server.HostKey.Sha256);
        return await SftpServer.ConnectAsync(signIn, key, cancellation).ConfigureAwait(false);
    }
}

/// <summary>
/// The folders of the servers in the settings: started, renamed, connected again after their
/// details change, and removed with their servers. See docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ProviderService(ISyncRootSystem sync, string userSid, Func<RemoteServer, int, ServerFolder> createFolder, ILogger logger) : IDisposable
{
    private readonly Dictionary<string, ServerFolder> _folders = new(StringComparer.OrdinalIgnoreCase);
    private bool _cleanedUp;

    /// <summary>The connected folders, by server ID.</summary>
    public IReadOnlyDictionary<string, ServerFolder> Folders => _folders;

    /// <summary>Applies the settings: each usable server's folder runs, a server removed from them is unregistered.</summary>
    public void Apply(RemoteFoldersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var listed = settings.Servers.Select(server => server.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usable = settings.UsableServers.ToDictionary(server => server.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (id, folder) in _folders.ToList())
        {
            if (!listed.Contains(id))
            {
                Run(() => folder.Remove());
                _folders.Remove(id);
            }
            else if (!usable.TryGetValue(id, out var now) || !SameConnection(folder.Server, now))
            {
                Run(folder.Dispose);
                _folders.Remove(id);
            }
            else if (now.Name != folder.Server.Name)
            {
                Run(() => folder.Rename(now.Name));
            }
        }

        if (!_cleanedUp)
        {
            _cleanedUp = true;
            RemoveLeftovers(listed);
        }

        int number = 0;
        foreach (var server in settings.Servers)
        {
            number++;
            if (!usable.ContainsKey(server.Id) || _folders.ContainsKey(server.Id))
            {
                continue;
            }

            var folder = createFolder(server, number);
            try
            {
                folder.Start();
                _folders[server.Id] = folder;
            }
            catch (Exception exception)
            {
                LogStartFailed(logger, number, exception);
                folder.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var folder in _folders.Values)
        {
            Run(folder.Dispose);
        }

        _folders.Clear();
    }

    // The same server, signing in the same way, to the same folder on both sides.
    private static bool SameConnection(RemoteServer before, RemoteServer now) =>
        before.Host == now.Host && before.Port == now.Port && before.UserName == now.UserName && before.SignIn == now.SignIn
        && before.KeyFile == now.KeyFile && before.RemoteFolder == now.RemoteFolder && before.Folder == now.Folder && before.HostKey == now.HostKey;

    // A server removed while PC Companion wasn't running left its folder registered.
    private void RemoveLeftovers(HashSet<string> listed)
    {
        string prefix = RemoteFoldersProcess.ProviderName + "!" + userSid + "!";
        foreach (var (id, _) in sync.Current(prefix))
        {
            if (!listed.Contains(id[prefix.Length..]))
            {
                Run(() => sync.Unregister(id));
                LogLeftoverRemoved(logger);
            }
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogChangeFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: its folder couldn't be started.")]
    private static partial void LogStartFailed(ILogger logger, int server, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "A folder of a server that's no longer in the settings was unregistered.")]
    private static partial void LogLeftoverRemoved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A server's folder couldn't be changed.")]
    private static partial void LogChangeFailed(ILogger logger, Exception exception);
}
