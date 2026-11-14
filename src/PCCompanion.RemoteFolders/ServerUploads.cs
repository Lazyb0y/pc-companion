using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    /// <summary>Renames the file, as for a conflict copy; see <see cref="UploadFile.Rename"/>.</summary>
    int Rename(string newPath);
}

/// <summary>A file changed on the PC and on the server: the PC's version is now <see cref="CopyPath"/>.</summary>
public sealed class ConflictEventArgs(string copyPath) : EventArgs
{
    /// <summary>The PC's version, the conflict copy, next to the server's version.</summary>
    public string CopyPath { get; } = copyPath;
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
    private bool _woken;

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

    /// <summary>Has <see cref="NextAsync"/> return without a path, so its caller can look at other work.</summary>
    public void Wake()
    {
        TaskCompletionSource added;
        lock (_gate)
        {
            _woken = true;
            added = _added;
            _added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        added.TrySetResult();
    }

    /// <summary>
    /// Waits for the next path that's due, and takes it from the queue; returns <see langword="null"/>
    /// when <see cref="Wake"/> was called and nothing is due.
    /// </summary>
    public async Task<string?> NextAsync(CancellationToken cancellation)
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

                if (_woken)
                {
                    _woken = false;
                    return null;
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
    // More than this many conflict copies of one file in a minute aren't made.
    private const int MaximumCopies = 99;

    private readonly WaitingChanges _queue = new(time);

    // The conflict copies on the server whose PC copies couldn't be renamed yet, by the file's path.
    private readonly ConcurrentDictionary<string, string> _pendingCopies = new(StringComparer.OrdinalIgnoreCase);

    // The versions this PC put on the server whose copies couldn't be marked in sync, because a
    // program opened them meanwhile: they aren't conflicts when the copy is uploaded again.
    private readonly ConcurrentDictionary<string, ItemIdentity> _uploaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RemoteError> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _conflicts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many changes wait for the server.</summary>
    public int Waiting => _queue.Count;

    /// <summary>The files the server refused, by their paths below the server's folder.</summary>
    public IReadOnlyDictionary<string, RemoteError> Failed => _failed;

    /// <summary>The conflict copies made, by their paths below the server's folder.</summary>
    public IReadOnlyCollection<string> Conflicts => [.. _conflicts.Keys];

    /// <summary>Forgets the conflict copies made so far, once the user opened the folder.</summary>
    public void ClearConflicts() => _conflicts.Clear();

    /// <summary>Raised when a file that changed on the PC and on the server was kept as a conflict copy.</summary>
    public event EventHandler<ConflictEventArgs>? ConflictFound;

    /// <summary>The renames, moves and deletes, whose journal is made on the server before any upload.</summary>
    public ServerChanges? Changes { get; set; }

    /// <summary>Whether <paramref name="current"/> is the version this PC put at <paramref name="relative"/> and hasn't marked in sync yet.</summary>
    public bool IsOwnVersion(string relative, RemoteEntry current) =>
        _uploaded.TryGetValue(relative, out var uploaded) && uploaded.IsSameVersion(current.Size, current.LastWriteTimeUtc);

    /// <summary>Has the worker look at the journal soon, as after a change was written to it.</summary>
    public void Wake() => _queue.Wake();

    /// <summary>
    /// Records a change to a file or folder in the server's folder, from the folder's watch or from
    /// Windows. A folder that's on the server needs nothing, so it isn't a change waiting: Windows
    /// and the provider change its placeholder as it's listed, such as every time it's listed again.
    /// </summary>
    public void Changed(string path)
    {
        if (RelativePath(path) is { Length: > 0 } && pc.Inspect(path) is not { IsDirectory: true, State: not null })
        {
            _queue.Changed(path);
        }
    }

    /// <summary>
    /// Finds the changes made while the provider wasn't running, or that the watch missed: new
    /// files and folders, changed files, and renames and moves, which go to the journal, a folder's
    /// once for all in it.
    /// </summary>
    public void FindChanges()
    {
        var found = pc.FindChanges(localRoot).OrderBy(path => path.Count(c => c == '\\')).ToList();
        var moves = new List<(string From, string To)>();
        foreach (string path in found)
        {
            if (Changes is { } changes && RelativePath(path) is { Length: > 0 } relative
                && pc.Inspect(path) is { State: not null, Identity: { } identity } item
                && identity.RelativePath != relative && WhereItWas(relative, moves) != identity.RelativePath)
            {
                changes.Record(new JournalEntry { Kind = JournalKind.Rename, Path = identity.RelativePath, Target = relative, IsDirectory = item.IsDirectory });
                moves.Add((identity.RelativePath, relative));
            }

            Changed(path);
        }

        LogFoundChanges(logger, serverNumber, found.Count, moves.Count);
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
                // Renames and deletes come first, so uploads find the server's files where the PC has them.
                if (Changes is { Waiting: > 0 } changes && !await changes.ReplayAsync(cancellation).ConfigureAwait(false))
                {
                    await connections.State.WaitForRetryAsync(cancellation).ConfigureAwait(false);
                    continue;
                }

                if (await _queue.NextAsync(cancellation).ConfigureAwait(false) is not { } next)
                {
                    continue;
                }

                path = next;
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
        if (relative is not { Length: > 0 })
        {
            // Outside the folder, or the folder itself, as when the worker is woken for the journal.
            return UploadOutcome.Done;
        }

        if (pc.Inspect(path) is not { } item)
        {
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

        if (item.State is not null && item.Identity is { } identity && identity.RelativePath != relative)
        {
            return await MovedAsync(path, relative, item, identity, cancellation).ConfigureAwait(false);
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

    // Renamed or moved on the PC, and on the server: its placeholder learns where it is now. A file
    // with changes, or one the server doesn't have there as the PC knew it, is uploaded.
    private async Task<UploadOutcome> MovedAsync(string path, string relative, LocalItem item, ItemIdentity identity, CancellationToken cancellation)
    {
        bool unchanged = item.State!.InSync || item.State.ModifiedSize == 0;
        if (!item.IsDirectory)
        {
            if (!unchanged)
            {
                return await UploadFileAsync(path, relative, item, cancellation).ConfigureAwait(false);
            }

            RemoteEntry? current;
            try
            {
                current = await connections.UseAsync((server, token) => server.StatAsync(ItemIdentity.RemotePath(remoteFolder, relative), token), cancellation).ConfigureAwait(false);
            }
            catch (RemoteServerException exception)
            {
                return Refused(relative, exception);
            }

            if (current is null || current.IsDirectory || !identity.IsSameVersion(current.Size, current.LastWriteTimeUtc))
            {
                return await UploadFileAsync(path, relative, item, cancellation).ConfigureAwait(false);
            }
        }

        int result = pc.UpdateIdentity(path, (identity with { RelativePath = relative }).ToBytes(), markInSync: true);
        if (result != 0)
        {
            LogMarkFailed(logger, serverNumber, result);
            return UploadOutcome.Busy;
        }

        LogNothingToUpload(logger, serverNumber, "moved");
        return UploadOutcome.Done;
    }

    private async Task<UploadOutcome> UploadFileAsync(string path, string relative, LocalItem item, CancellationToken cancellation)
    {
        // The version on the server the PC's copy came from, also when it moved since; none for a new file.
        var version = item.Identity is { IsDirectory: false } known ? known : null;
        int opened = pc.OpenForUpload(path, out var file);
        if (opened != 0 || file is null)
        {
            LogInUse(logger, serverNumber, opened);
            return UploadOutcome.Busy;
        }

        using (file)
        {
            var started = time.GetTimestamp();
            string temporary = ItemIdentity.RemotePath(remoteFolder, ItemIdentity.Join(Parent(relative), RemoteNames.UploadPrefix + RandomPart()));
            Put put;
            try
            {
                put = await connections.UseAsync((server, token) => PutAsync(server, file, path, relative, temporary, version, token), cancellation).ConfigureAwait(false);
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

            if (put.CopyName is { } copyName)
            {
                return Conflict(path, relative, item, file, put, copyName);
            }

            var identity = ItemIdentity.Of(Parent(relative), item.Name, isDirectory: false, put.Uploaded.Size, put.Uploaded.LastWriteTimeUtc);
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

    // The PC's version is on the server as a conflict copy: the PC's copy takes its name, and the
    // server's version keeps the original name, as an online-only file.
    private UploadOutcome Conflict(string path, string relative, LocalItem item, IUploadSource file, Put put, string copyName)
    {
        string folder = Path.GetDirectoryName(path)!;
        string copyPath = Path.Join(folder, copyName);
        string copyRelative = ItemIdentity.Join(Parent(relative), copyName);
        var copyIdentity = ItemIdentity.Of(Parent(relative), copyName, isDirectory: false, put.Uploaded.Size, put.Uploaded.LastWriteTimeUtc);
        _uploaded[copyRelative] = copyIdentity;
        int renamed = file.Rename(copyPath);
        if (renamed != 0)
        {
            // The copy waits on the server under its name; renaming the PC's copy is tried again.
            LogCopyRenameFailed(logger, serverNumber, renamed);
            return UploadOutcome.Busy;
        }

        _pendingCopies.TryRemove(relative, out _);
        _conflicts[copyRelative] = 0;
        int marked = file.MarkInSync(copyIdentity.ToBytes(), item.State is not null);
        if (marked == 0)
        {
            _uploaded.TryRemove(copyRelative, out _);
        }
        else
        {
            // A program opened the copy meanwhile; it's uploaded again under its own name.
            LogMarkFailed(logger, serverNumber, marked);
            _queue.Changed(copyPath);
        }

        RestoreServerVersion(folder, relative, item.Name, put.ServerVersion!);
        LogConflict(logger, serverNumber);
        ConflictFound?.Invoke(this, new ConflictEventArgs(copyPath));
        return UploadOutcome.Conflict;
    }

    private void RestoreServerVersion(string folder, string relative, string name, RemoteEntry serverVersion)
    {
        var identity = ItemIdentity.Of(Parent(relative), name, serverVersion.IsDirectory, serverVersion.Size, serverVersion.LastWriteTimeUtc);
        var results = new int[1];
        int created = pc.Create(folder, [ListingDiff.Placeholder(serverVersion with { Name = name }, identity)], results);
        if (created != 0 || results[0] != 0)
        {
            // The next listing of the folder brings it.
            LogRestoreFailed(logger, serverNumber, created != 0 ? created : results[0]);
            return;
        }

        if (!serverVersion.IsDirectory)
        {
            try
            {
                pc.MarkFromInternet(Path.Join(folder, name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogRestoreFailed(logger, serverNumber, exception.HResult);
            }
        }
    }

    // Uploads under a temporary name, then puts it in place if the server's file is still the
    // version the PC's copy came from, or next to it under a conflict name if it isn't.
    private async Task<Put> PutAsync(IRemoteServer server, IUploadSource file, string path, string relative, string temporary, ItemIdentity? version, CancellationToken cancellation)
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

        string remote = ItemIdentity.RemotePath(remoteFolder, relative);
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
                string copyName = await CopyNameAsync(server, path, relative, cancellation).ConfigureAwait(false);
                string copyRemote = ItemIdentity.RemotePath(remoteFolder, ItemIdentity.Join(Parent(relative), copyName));
                if (await server.StatAsync(copyRemote, cancellation).ConfigureAwait(false) is null)
                {
                    await server.RenameAsync(temporary, copyRemote, cancellation).ConfigureAwait(false);
                }
                else
                {
                    // This PC put the copy there before, and couldn't rename its own copy then.
                    await server.ReplaceAsync(temporary, copyRemote, cancellation).ConfigureAwait(false);
                }

                var copy = await server.StatAsync(copyRemote, cancellation).ConfigureAwait(false)
                    ?? throw new RemoteServerException(RemoteError.NotFound, "The conflict copy isn't on the server.");
                return new Put(copy, copyName, current);
            }
        }
        catch (Exception)
        {
            await DeleteQuietlyAsync(server, temporary).ConfigureAwait(false);
            throw;
        }

        var uploaded = await server.StatAsync(remote, cancellation).ConfigureAwait(false)
            ?? throw new RemoteServerException(RemoteError.NotFound, "The uploaded file isn't on the server.");
        return new Put(uploaded, null, null);
    }

    // The conflict copy's name, in the user's language, free on the server and on the PC; the same
    // name as before when the PC's copy couldn't be renamed then.
    private async Task<string> CopyNameAsync(IRemoteServer server, string path, string relative, CancellationToken cancellation)
    {
        if (_pendingCopies.TryGetValue(relative, out string? pending))
        {
            return pending;
        }

        string folder = Path.GetDirectoryName(path)!;
        string first = ConflictNames.For(
            Path.GetFileName(path),
            time.GetLocalNow().DateTime,
            CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture,
            CultureInfo.CurrentCulture);
        string name = first;
        for (int number = 2; ; number++)
        {
            string remote = ItemIdentity.RemotePath(remoteFolder, ItemIdentity.Join(Parent(relative), name));
            if (pc.Inspect(Path.Join(folder, name)) is null && await server.StatAsync(remote, cancellation).ConfigureAwait(false) is null)
            {
                break;
            }

            if (number > MaximumCopies)
            {
                throw new RemoteServerException(RemoteError.Failed, "Every conflict copy's name is taken.");
            }

            name = ConflictNames.Numbered(first, number);
        }

        _pendingCopies[relative] = name;
        return name;
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

    // Where an item was before the moves found so far, the deepest first.
    private static string WhereItWas(string relative, List<(string From, string To)> moves)
    {
        foreach (var (from, to) in moves.OrderByDescending(move => move.To.Length))
        {
            if (relative == to || relative.StartsWith(to + "/", StringComparison.Ordinal))
            {
                return from + relative[to.Length..];
            }
        }

        return relative;
    }

    private static string RandomPart() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: uploaded {Bytes} bytes in {Milliseconds:F0} ms.")]
    private static partial void LogUploaded(ILogger logger, int server, long bytes, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a change needs no upload: {Reason}.")]
    private static partial void LogNothingToUpload(ILogger logger, int server, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: created a folder.")]
    private static partial void LogFolderCreated(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: found {Count} changes on the PC, {Moves} of them renames or moves.")]
    private static partial void LogFoundChanges(ILogger logger, int server, int count, int moves);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a file to upload is open in a program (0x{Result:X8}); it's tried again later.")]
    private static partial void LogInUse(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a program opened a file during its upload; it's tried again later.")]
    private static partial void LogOpenedDuringUpload(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: a file changed on the PC and on the server; the PC's version is a conflict copy.")]
    private static partial void LogConflict(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a conflict copy couldn't be renamed on the PC (0x{Result:X8}); it's tried again.")]
    private static partial void LogCopyRenameFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: the server's version of a conflict waits for the next listing (0x{Result:X8}).")]
    private static partial void LogRestoreFailed(ILogger logger, int server, int result);

    // What an upload did: the file is in place, or next to the server's version as a conflict copy.
    private sealed record Put(RemoteEntry Uploaded, string? CopyName, RemoteEntry? ServerVersion);

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
