using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>What became of one change on the PC.</summary>
public enum UploadOutcome
{
    /// <summary>It's on the server, or there was nothing to upload.</summary>
    Done,

    /// <summary>A program has the file open, or opened it during the upload; it's tried again a little later.</summary>
    Busy,

    /// <summary>The server can't be reached or refused the sign-in; it waits for the server.</summary>
    Waiting,

    /// <summary>The file also changed on the server.</summary>
    Conflict,

    /// <summary>The server refused it; it's tried again after the next change.</summary>
    Failed,
}

/// <summary>A file on the PC opened for an upload; see <see cref="UploadFile"/>.</summary>
public interface IUploadSource : IDisposable
{
    long Length { get; }

    /// <summary>Whether a program opened the file during the upload, so it may have changed.</summary>
    bool IsClosed { get; }

    Stream OpenRead();

    /// <summary>Marks the file as the same as on the server, with its new identity; see <see cref="UploadFile.MarkInSync"/>.</summary>
    int MarkInSync(byte[] identity, bool isPlaceholder);
}

/// <summary>
/// The changes on the PC that wait for an upload, each due once it has been quiet for 2 seconds.
/// One per server; any thread may add to it.
/// </summary>
public sealed class WaitingChanges(TimeProvider time)
{
    /// <summary>How long a file must stay unchanged before it's uploaded.</summary>
    public static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _due = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _added = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many changes wait.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _due.Count;
            }
        }
    }

    /// <summary>Records a change: the path is due once it hasn't changed for <see cref="QuietFor"/>.</summary>
    public void Changed(string path) => Set(path, time.GetUtcNow() + QuietFor, later: false);

    /// <summary>Tries a path again after <paramref name="delay"/>, or later if it changed again meanwhile.</summary>
    public void Later(string path, TimeSpan delay) => Set(path, time.GetUtcNow() + delay, later: true);

    /// <summary>Waits for the next path that's due, and takes it from the queue.</summary>
    public async Task<string> NextAsync(CancellationToken cancellation)
    {
        while (true)
        {
            Task added;
            TimeSpan? wait = null;
            lock (_gate)
            {
                added = _added.Task;
                string? first = null;
                var firstDue = DateTimeOffset.MaxValue;
                foreach (var (path, due) in _due)
                {
                    if (due < firstDue)
                    {
                        first = path;
                        firstDue = due;
                    }
                }

                if (first is not null)
                {
                    var left = firstDue - time.GetUtcNow();
                    if (left <= TimeSpan.Zero)
                    {
                        _due.Remove(first);
                        return first;
                    }

                    wait = left;
                }
            }

            if (wait is null)
            {
                await added.WaitAsync(cancellation).ConfigureAwait(false);
            }
            else
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                await Task.WhenAny(added, Task.Delay(wait.Value, time, stop.Token)).ConfigureAwait(false);
                await stop.CancelAsync().ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
            }
        }
    }

    private void Set(string path, DateTimeOffset due, bool later)
    {
        TaskCompletionSource added;
        lock (_gate)
        {
            if (later && _due.TryGetValue(path, out var known) && known > due)
            {
                return;
            }

            _due[path] = due;
            added = _added;
            _added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        added.TrySetResult();
    }
}

/// <summary>
/// Uploads the changes made on the PC to a server's folder: new and changed files, one at a time,
/// and new folders. See "Uploading" in docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerUploads(
    string localRoot,
    string remoteFolder,
    IServerConnections connections,
    IPlaceholderSystem pc,
    TimeProvider time,
    ILogger logger,
    int serverNumber)
{
    private readonly WaitingChanges _queue = new(time);

    // The versions this PC put on the server whose copies couldn't be marked in sync, because a
    // program opened them meanwhile: they aren't conflicts when the copy is uploaded again.
    private readonly ConcurrentDictionary<string, ItemIdentity> _uploaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RemoteError> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _conflicts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many changes wait for the server.</summary>
    public int Waiting => _queue.Count;

    /// <summary>The files the server refused, by their paths below the server's folder.</summary>
    public IReadOnlyDictionary<string, RemoteError> Failed => _failed;

    /// <summary>The files that also changed on the server, by their paths below the server's folder.</summary>
    public IReadOnlyCollection<string> Conflicts => (IReadOnlyCollection<string>)_conflicts.Keys;

    /// <summary>Records a change to a file or folder in the server's folder, from the folder's watch or from Windows.</summary>
    public void Changed(string path)
    {
        if (RelativePath(path) is { Length: > 0 })
        {
            _queue.Changed(path);
        }
    }

    /// <summary>Finds the changes made while the provider wasn't running, or that the watch missed: new files and folders, and changed files.</summary>
    public void FindChanges()
    {
        int found = 0;
        foreach (string path in pc.FindChanges(localRoot))
        {
            Changed(path);
            found++;
        }

        LogFoundChanges(logger, serverNumber, found);
    }

    /// <summary>Uploads the changes as they come, one at a time, until <paramref name="cancellation"/> stops it.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            string path;
            UploadOutcome outcome;
            try
            {
                path = await _queue.NextAsync(cancellation).ConfigureAwait(false);
                outcome = await UploadAsync(path, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (outcome == UploadOutcome.Busy)
            {
                _queue.Later(path, WaitingChanges.QuietFor);
            }
            else if (outcome == UploadOutcome.Waiting)
            {
                _queue.Later(path, TimeSpan.Zero);
                try
                {
                    await connections.State.WaitForRetryAsync(cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Uploads one change. Never throws, except when cancelled: a failure is an outcome.</summary>
    public async Task<UploadOutcome> UploadAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return await UploadItemAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUploadCrashed(logger, serverNumber, exception);
            return UploadOutcome.Failed;
        }
    }

    private async Task<UploadOutcome> UploadItemAsync(string path, CancellationToken cancellation)
    {
        string? relative = RelativePath(path);
        if (relative is not { Length: > 0 } || pc.Inspect(path) is not { } item)
        {
            // Outside the folder, the folder itself, or gone.
            LogNothingToUpload(logger, serverNumber, "gone");
            return UploadOutcome.Done;
        }

        if (item.IsLink || item.State is { Pin: PinState.Excluded })
        {
            // Links are never uploaded or followed, and excluded files stay on the PC.
            LogNothingToUpload(logger, serverNumber, item.IsLink ? "a link" : "excluded");
            return UploadOutcome.Done;
        }

        if (RemoteNames.StaysOnPc(item.Name))
        {
            int excluded = pc.Exclude(path);
            if (excluded != 0)
            {
                LogExcludeFailed(logger, serverNumber, excluded);
            }

            LogNothingToUpload(logger, serverNumber, "stays on the PC");
            return UploadOutcome.Done;
        }

        if (item.State is { } state && (item.IsDirectory || state.InSync))
        {
            LogNothingToUpload(logger, serverNumber, item.IsDirectory ? "a folder that's on the server" : "in sync");
            return UploadOutcome.Done;
        }

        // A new file in a new folder: the folder goes to the server first.
        string parent = Path.GetDirectoryName(path)!;
        if (RelativePath(parent) is { Length: > 0 } && pc.Inspect(parent) is { State: null })
        {
            var parentOutcome = await UploadItemAsync(parent, cancellation).ConfigureAwait(false);
            if (parentOutcome != UploadOutcome.Done)
            {
                return parentOutcome;
            }

            if (pc.Inspect(parent) is not { State: not null })
            {
                // The folder stays on the PC, as a link or an excluded folder does, and so does all in it.
                return UploadOutcome.Done;
            }
        }

        return item.IsDirectory
            ? await CreateFolderAsync(path, relative, cancellation).ConfigureAwait(false)
            : await UploadFileAsync(path, relative, item, cancellation).ConfigureAwait(false);
    }

    private async Task<UploadOutcome> CreateFolderAsync(string path, string relative, CancellationToken cancellation)
    {
        string remote = ItemIdentity.RemotePath(remoteFolder, relative);
        RemoteEntry folder;
        try
        {
            folder = await connections.UseAsync(
                async (server, token) =>
                {
                    var existing = await server.StatAsync(remote, token).ConfigureAwait(false);
                    if (existing is null)
                    {
                        await server.CreateFolderAsync(remote, token).ConfigureAwait(false);
                        existing = await server.StatAsync(remote, token).ConfigureAwait(false);
                    }

                    return existing is { IsDirectory: true }
                        ? existing
                        : throw new RemoteServerException(RemoteError.Failed, "A file on the server has the folder's name.");
                },
                cancellation).ConfigureAwait(false);
        }
        catch (RemoteServerException exception)
        {
            return Refused(relative, exception);
        }

        var identity = ItemIdentity.Of(Parent(relative), Path.GetFileName(path), isDirectory: true, 0, folder.LastWriteTimeUtc);
        int result = pc.ConvertFolder(path, identity.ToBytes());
        if (result != 0)
        {
            LogMarkFailed(logger, serverNumber, result);
            return UploadOutcome.Busy;
        }

        _failed.TryRemove(relative, out _);
        LogFolderCreated(logger, serverNumber);

        // Whatever is already in it follows.
        foreach (var child in pc.Items(path))
        {
            _queue.Changed(child.Path);
        }

        return UploadOutcome.Done;
    }

    private async Task<UploadOutcome> UploadFileAsync(string path, string relative, LocalItem item, CancellationToken cancellation)
    {
        // The version on the server the PC's copy came from; none for a new file.
        var version = item.Identity is { IsDirectory: false } known && known.RelativePath == relative ? known : null;
        int opened = pc.OpenForUpload(path, out var file);
        if (opened != 0 || file is null)
        {
            LogInUse(logger, serverNumber, opened);
            return UploadOutcome.Busy;
        }

        using (file)
        {
            var started = time.GetTimestamp();
            string remote = ItemIdentity.RemotePath(remoteFolder, relative);
            string temporary = ItemIdentity.RemotePath(remoteFolder, ItemIdentity.Join(Parent(relative), RemoteNames.UploadPrefix + RandomPart()));
            RemoteEntry? uploaded;
            try
            {
                uploaded = await connections.UseAsync((server, token) => PutAsync(server, file, relative, temporary, remote, version, token), cancellation).ConfigureAwait(false);
            }
            catch (RemoteServerException) when (file.IsClosed)
            {
                LogOpenedDuringUpload(logger, serverNumber);
                return UploadOutcome.Busy;
            }
            catch (RemoteServerException exception)
            {
                return Refused(relative, exception);
            }

            if (uploaded is null)
            {
                _conflicts[relative] = 0;
                LogConflict(logger, serverNumber);
                return UploadOutcome.Conflict;
            }

            var identity = ItemIdentity.Of(Parent(relative), item.Name, isDirectory: false, uploaded.Size, uploaded.LastWriteTimeUtc);
            _uploaded[relative] = identity;
            int marked = file.MarkInSync(identity.ToBytes(), item.State is not null);
            if (marked != 0)
            {
                // A program opened it meanwhile, so it may have changed: it's checked again.
                LogMarkFailed(logger, serverNumber, marked);
                return UploadOutcome.Busy;
            }

            _uploaded.TryRemove(relative, out _);
            _failed.TryRemove(relative, out _);
            double elapsed = time.GetElapsedTime(started).TotalMilliseconds;
            LogUploaded(logger, serverNumber, file.Length, elapsed);
            return UploadOutcome.Done;
        }
    }

    // Uploads under a temporary name, then puts it in place if the server's file is still the
    // version the PC's copy came from. Returns the file on the server, or null for a conflict.
    private async Task<RemoteEntry?> PutAsync(IRemoteServer server, IUploadSource file, string relative, string temporary, string remote, ItemIdentity? version, CancellationToken cancellation)
    {
        try
        {
            await using var content = file.OpenRead();
            await server.UploadAsync(content, temporary, cancellation).ConfigureAwait(false);
        }
        catch (RemoteServerException exception) when (exception.Error == RemoteError.NotFound && !file.IsClosed)
        {
            // The folder is gone from the server; it's made again.
            await CreateFoldersAsync(server, Parent(relative), cancellation).ConfigureAwait(false);
            await using var content = file.OpenRead();
            await server.UploadAsync(content, temporary, cancellation).ConfigureAwait(false);
        }

        try
        {
            var current = await server.StatAsync(remote, cancellation).ConfigureAwait(false);
            if (current is null)
            {
                await server.RenameAsync(temporary, remote, cancellation).ConfigureAwait(false);
            }
            else if (IsExpected(current, relative, version))
            {
                await server.ReplaceAsync(temporary, remote, cancellation).ConfigureAwait(false);
            }
            else
            {
                await DeleteQuietlyAsync(server, temporary).ConfigureAwait(false);
                return null;
            }
        }
        catch (Exception)
        {
            await DeleteQuietlyAsync(server, temporary).ConfigureAwait(false);
            throw;
        }

        return await server.StatAsync(remote, cancellation).ConfigureAwait(false)
            ?? throw new RemoteServerException(RemoteError.NotFound, "The uploaded file isn't on the server.");
    }

    // The server's file is the version the PC's copy came from, or the one this PC put there.
    private bool IsExpected(RemoteEntry current, string relative, ItemIdentity? version) =>
        !current.IsDirectory
        && ((version is not null && version.IsSameVersion(current.Size, current.LastWriteTimeUtc))
            || (_uploaded.TryGetValue(relative, out var uploaded) && uploaded.IsSameVersion(current.Size, current.LastWriteTimeUtc)));

    private async Task CreateFoldersAsync(IRemoteServer server, string relativeFolder, CancellationToken cancellation)
    {
        string path = string.Empty;
        foreach (string part in relativeFolder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = ItemIdentity.Join(path, part);
            string remote = ItemIdentity.RemotePath(remoteFolder, path);
            if (await server.StatAsync(remote, cancellation).ConfigureAwait(false) is null)
            {
                await server.CreateFolderAsync(remote, cancellation).ConfigureAwait(false);
            }
        }
    }

    // A temporary file that's left behind is hidden from listings, so a failure here only costs space.
    private async Task DeleteQuietlyAsync(IRemoteServer server, string temporary)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), time);
            await server.DeleteFileAsync(temporary, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RemoteServerException or OperationCanceledException)
        {
            LogTemporaryLeft(logger, serverNumber);
        }
    }

    private UploadOutcome Refused(string relative, RemoteServerException exception)
    {
        if (exception.Error is RemoteError.Unreachable or RemoteError.SignInFailed or RemoteError.KeyNotTrusted or RemoteError.KeyFile or RemoteError.NoSafeAlgorithm)
        {
            return UploadOutcome.Waiting;
        }

        _failed[relative] = exception.Error;
        LogUploadFailed(logger, serverNumber, exception.Error);
        return UploadOutcome.Failed;
    }

    // The path below the server's folder, with /; null when it isn't in the folder.
    private string? RelativePath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRoot));
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/')
            : null;
    }

    private static string Parent(string relativePath) => relativePath[..Math.Max(0, relativePath.LastIndexOf('/'))];

    private static string RandomPart() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: uploaded {Bytes} bytes in {Milliseconds:F0} ms.")]
    private static partial void LogUploaded(ILogger logger, int server, long bytes, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a change needs no upload: {Reason}.")]
    private static partial void LogNothingToUpload(ILogger logger, int server, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: created a folder.")]
    private static partial void LogFolderCreated(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: found {Count} changes on the PC.")]
    private static partial void LogFoundChanges(ILogger logger, int server, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a file to upload is open in a program (0x{Result:X8}); it's tried again later.")]
    private static partial void LogInUse(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a program opened a file during its upload; it's tried again later.")]
    private static partial void LogOpenedDuringUpload(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: a file changed on the PC and on the server.")]
    private static partial void LogConflict(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: an upload failed ({Error}).")]
    private static partial void LogUploadFailed(ILogger logger, int server, RemoteError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: an upload failed.")]
    private static partial void LogUploadCrashed(ILogger logger, int server, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a file couldn't be marked in sync (0x{Result:X8}); it's checked again.")]
    private static partial void LogMarkFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a file that stays on the PC couldn't be marked as excluded (0x{Result:X8}).")]
    private static partial void LogExcludeFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a temporary file was left on the server.")]
    private static partial void LogTemporaryLeft(ILogger logger, int server);
}
