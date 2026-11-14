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
    private readonly IPlaceholderSystem _pc;
    private readonly IServerConnections _connections;
    private readonly ILogger _logger;
    private readonly int _number;
    private readonly ChangeJournal _journal;
    private IDisposable? _connection;
    private IDisposable? _watch;
    private RootPaths? _paths;
    private CancellationTokenSource? _uploading;

    /// <param name="journalFile">Where the renames and deletes wait while the server can't be reached.</param>
    public ServerFolder(RemoteServer server, string userSid, string iconResource, ISyncRootSystem sync, IPlaceholderSystem pc, IServerConnections connections, string journalFile, TimeProvider time, ILogger logger, int number)
    {
        _server = server;
        _iconResource = iconResource;
        _sync = sync;
        _pc = pc;
        _connections = connections;
        _logger = logger;
        _number = number;
        Id = SyncRoots.MakeId(RemoteFoldersProcess.ProviderName, userSid, server.Id);
        Listings = new ServerListings(server.Folder, server.RemoteFolder, connections, pc, time, logger, number);
        Downloads = new ServerDownloads(server.RemoteFolder, connections, Listings, time, logger, number);
        Uploads = new ServerUploads(server.Folder, server.RemoteFolder, connections, pc, time, logger, number);
        _journal = new ChangeJournal(journalFile);
        if (_journal.WasUnreadable)
        {
            LogJournalUnreadable(logger, number);
        }

        Changes = new ServerChanges(server.RemoteFolder, connections, _journal, Uploads, logger, number);
        Uploads.Changes = Changes;
    }

    /// <summary>The sync root's ID.</summary>
    public string Id { get; }

    /// <summary>The server's settings it was made with.</summary>
    public RemoteServer Server => _server;

    public ServerListings Listings { get; }

    public ServerDownloads Downloads { get; }

    public ServerUploads Uploads { get; }

    public ServerChanges Changes { get; }

    /// <summary>The server's state, and when it's tried again.</summary>
    public ConnectionState State => _connections.State;

    /// <summary>Makes the folder if it's missing, registers it, again when it was, and connects to it.</summary>
    /// <exception cref="Exception">Windows refused, for example for a folder that isn't on an NTFS drive.</exception>
    public void Start()
    {
        Directory.CreateDirectory(_server.Folder);
        _paths = new RootPaths(_server.Folder, VolumePaths.OnVolume(_server.Folder));
        _sync.Register(new SyncRootRegistration(Id, _server.Folder, _server.Name, _iconResource));
        _connection = _sync.Connect(_server.Folder, this, exception => LogRequestFailed(_logger, _number, exception));

        // Changes made while the provider wasn't running are found first, then the watch adds new ones.
        _watch = _pc.Watch(_server.Folder, Uploads.Changed, () => _ = Task.Run(Uploads.FindChanges));
        _uploading = new CancellationTokenSource();
        var stop = _uploading.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    // Folders a listing marked complete before the provider stopped are listed again.
                    Listings.ListAllAgainOnNextOpen();
                    Uploads.FindChanges();
                    await Uploads.RunAsync(stop).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    LogUploadsStopped(_logger, _number, exception);
                }
            },
            CancellationToken.None);
        LogStarted(_logger, _number);
    }

    /// <summary>Registers the folder again with a new name; its files stay.</summary>
    public void Rename(string name) => _sync.Register(new SyncRootRegistration(Id, _server.Folder, name, _iconResource));

    /// <summary>Lets the server be tried again at once; see <see cref="IServerConnections.TryAgainNow"/>.</summary>
    public void TryAgainNow() => _connections.TryAgainNow();

    /// <summary>
    /// Disconnects and unregisters the folder, as removing the server does: online-only files
    /// disappear, and files on the PC stay in what's now an ordinary folder.
    /// </summary>
    public void Remove()
    {
        Dispose();
        _sync.Unregister(Id);
        _journal.Delete();
        LogRemoved(_logger, _number);
    }

    /// <summary>Disconnects; the folder stays registered.</summary>
    public void Dispose()
    {
        _uploading?.Cancel();
        _uploading?.Dispose();
        _uploading = null;
        _watch?.Dispose();
        _watch = null;
        _connection?.Dispose();
        _connection = null;
        Listings.Dispose();
        _connections.Dispose();
    }

    public void FetchPlaceholders(FetchPlaceholdersRequest request) => _ = Task.Run(() => Listings.HandleAsync(new ListingRequest(request)));

    public void FetchData(FetchDataRequest request) => _ = Task.Run(() => Downloads.HandleAsync(new DataRequest(request)));

    public void CancelFetchData(CancelFetchDataNotice notice) => Downloads.Cancel(notice.Path);

    // PC Companion's own changes, such as removing a file deleted on the server, go ahead; others
    // are made on the server first, or go to the journal.
    public void Delete(DeleteRequest request)
    {
        if (IsOwn(request) || request.IsUndelete || ChangeOf(request.PathOnVolume) is not { } source)
        {
            request.Allow();
            return;
        }

        Make(request, () => DeleteOf(source.Local, source.Relative, request.IsDirectory, request.Identity));
    }

    public void Rename(RenameRequest request)
    {
        if (IsOwn(request) || ChangeOf(request.PathOnVolume) is not { } source)
        {
            request.Allow();
            return;
        }

        if (!request.IsTargetInSyncRoot)
        {
            // Moving out of the folder, as to the Recycle Bin, deletes it on the server.
            Make(request, () => DeleteOf(source.Local, source.Relative, request.IsDirectory, request.Identity));
            return;
        }

        if (ChangeOf(request.TargetPathOnVolume) is not { } target)
        {
            request.Refuse(CloudFileStatus.Unsuccessful);
            return;
        }

        Make(request, () => new JournalEntry { Kind = JournalKind.Rename, Path = source.Relative, Target = target.Relative, IsDirectory = request.IsDirectory });
    }

    // Its placeholder, and those in a folder, learn their new paths.
    public void RenameCompleted(RenameCompletedNotice notice)
    {
        if (_paths?.FromVolume(notice.PathOnVolume) is not { } local)
        {
            return;
        }

        Uploads.Changed(local);
        _ = Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(local))
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(local, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
                    {
                        Uploads.Changed(path);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogRequestFailed(_logger, _number, exception);
            }
        });
    }

    // Windows removes only copies that are in sync, so nothing waiting for the server is lost.
    public void Dehydrate(DehydrateRequest request) => request.Allow();

    // A program closed a file it may have written.
    public void FileClosed(FileClosedNotice notice) => Uploads.Changed(_paths?.FromVolume(notice.PathOnVolume) ?? notice.Path);

    private static bool IsOwn(CloudFileRequest request) => request.Program?.ProcessId == Environment.ProcessId;

    // The request's file on the PC and below the folder; none for the folder itself.
    private (string Local, string Relative)? ChangeOf(string pathOnVolume) =>
        _paths?.FromVolume(pathOnVolume) is { } local && _paths.Relative(local) is { Length: > 0 } relative ? (local, relative) : null;

    // The change runs on the thread pool, and Windows always gets an answer.
    private void Make(CloudFileRequest request, Func<JournalEntry> change)
    {
        _ = Task.Run(async () =>
        {
            int status;
            try
            {
                status = await Changes.MakeAsync(change(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogRequestFailed(_logger, _number, exception);
                status = CloudFileStatus.Unsuccessful;
            }

            if (status == CloudFileStatus.Success)
            {
                switch (request)
                {
                    case DeleteRequest delete:
                        delete.Allow();
                        break;
                    case RenameRequest rename:
                        rename.Allow();
                        break;
                }
            }
            else
            {
                request.Refuse(status);
            }
        });
    }

    // A delete keeps the versions the PC knew: of the file, or of every file in the folder.
    private JournalEntry DeleteOf(string local, string relative, bool isDirectory, IReadOnlyList<byte> identityBytes)
    {
        if (!isDirectory)
        {
            var identity = identityBytes.Count == 0 ? null : ItemIdentity.FromBytes([.. identityBytes]);
            return new JournalEntry { Kind = JournalKind.Delete, Path = relative, Size = identity?.Size, LastWriteTimeUtc = identity?.LastWriteTimeUtc };
        }

        var files = new List<JournalFile>();
        var folders = new Stack<(string Local, string Relative)>();
        folders.Push((local, relative));
        while (folders.Count > 0)
        {
            var (folder, folderRelative) = folders.Pop();
            foreach (var item in _pc.Items(folder))
            {
                string itemRelative = ItemIdentity.Join(folderRelative, item.Name);
                if (item.IsDirectory && !item.IsLink)
                {
                    folders.Push((item.Path, itemRelative));
                }
                else if (!item.IsDirectory && item.Identity is { } known)
                {
                    files.Add(new JournalFile(itemRelative, known.Size, known.LastWriteTimeUtc));
                }
            }
        }

        return new JournalEntry { Kind = JournalKind.Delete, Path = relative, IsDirectory = true, Files = files };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: its folder is connected.")]
    private static partial void LogStarted(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: its folder is unregistered.")]
    private static partial void LogRemoved(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: the journal of changes waiting for the server couldn't be read, so they're lost.")]
    private static partial void LogJournalUnreadable(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server {Server}: uploads stopped.")]
    private static partial void LogUploadsStopped(ILogger logger, int server, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a request from Windows failed, and was refused.")]
    private static partial void LogRequestFailed(ILogger logger, int server, Exception exception);
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

    /// <summary>Lets every server be tried again at once, after a network change or after the PC wakes from sleep.</summary>
    public void TryAgainNow()
    {
        foreach (var folder in _folders.Values)
        {
            Run(folder.TryAgainNow);
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
