using System;
using System.Collections.Concurrent;
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
/// The paths of a server's folder: on the PC, on its volume as Windows' requests give them, and
/// below the folder, with <c>/</c>, as on the server.
/// </summary>
public sealed class RootPaths(string localRoot, string rootOnVolume)
{
    public string LocalRoot { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRoot));

    /// <summary>The path on the PC of a path on the folder's volume, or <see langword="null"/> when it isn't in the folder.</summary>
    public string? FromVolume(string pathOnVolume)
    {
        string root = Path.TrimEndingDirectorySeparator(rootOnVolume);
        if (pathOnVolume.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return LocalRoot;
        }

        return pathOnVolume.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
            ? LocalRoot + pathOnVolume[root.Length..]
            : null;
    }

    /// <summary>The path below the folder, with <c>/</c>; empty for the folder itself, <see langword="null"/> when it isn't in it.</summary>
    public string? Relative(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.Equals(LocalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return full.StartsWith(LocalRoot + "\\", StringComparison.OrdinalIgnoreCase)
            ? full[(LocalRoot.Length + 1)..].Replace('\\', '/')
            : null;
    }
}

/// <summary>
/// The renames, moves and deletes made on the PC: made on the server before Windows completes
/// them while it can be reached, and written to the journal, then made on the server when it's
/// back, while it can't. A delete never deletes a file that changed on the server since the PC
/// last saw it. See "Uploading" in docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerChanges(
    string remoteFolder,
    IServerConnections connections,
    ChangeJournal journal,
    ServerUploads uploads,
    ILogger logger,
    int serverNumber)
{
    private readonly ConcurrentQueue<(JournalEntry Entry, RemoteError Error)> _skipped = new();

    /// <summary>How many changes wait in the journal.</summary>
    public int Waiting => journal.Count;

    /// <summary>The journal's changes that couldn't be made on the server, such as a rename to a name that's now taken.</summary>
    public IReadOnlyCollection<(JournalEntry Entry, RemoteError Error)> Skipped => [.. _skipped];

    /// <summary>
    /// Makes a rename, move or delete on the server, or writes it to the journal when the server
    /// can't be reached. Never throws.
    /// </summary>
    /// <returns>The status Windows gives the program: success, or the server's refusal.</returns>
    public async Task<int> MakeAsync(JournalEntry change, CancellationToken cancellation)
    {
        try
        {
            if (connections.State.FailFast() is not null || journal.Count > 0)
            {
                // Earlier changes wait, so this one waits after them.
                return Postpone(change);
            }

            await connections.UseAsync(
                async (server, token) =>
                {
                    await ApplyAsync(server, change, token).ConfigureAwait(false);
                    return true;
                },
                cancellation).ConfigureAwait(false);
            LogMade(logger, serverNumber, change.Kind);
            return CloudFileStatus.Success;
        }
        catch (RemoteServerException exception) when (IsServerDown(exception.Error))
        {
            return Postpone(change);
        }
        catch (RemoteServerException exception)
        {
            LogRefused(logger, serverNumber, change.Kind, exception.Error);
            return exception.Error == RemoteError.PermissionDenied ? CloudFileStatus.AccessDenied : CloudFileStatus.Unsuccessful;
        }
        catch (Exception exception)
        {
            LogCrashed(logger, serverNumber, exception);
            return CloudFileStatus.Unsuccessful;
        }
    }

    /// <summary>
    /// Makes the journal's changes on the server, in order. A change the server refuses is skipped.
    /// </summary>
    /// <returns>Whether the journal is empty; <see langword="false"/> when the server can't be reached.</returns>
    public async Task<bool> ReplayAsync(CancellationToken cancellation)
    {
        while (journal.First is { } change)
        {
            try
            {
                await connections.UseAsync(
                    async (server, token) =>
                    {
                        await ApplyAsync(server, change, token).ConfigureAwait(false);
                        return true;
                    },
                    cancellation).ConfigureAwait(false);
                LogMade(logger, serverNumber, change.Kind);
            }
            catch (RemoteServerException exception) when (IsServerDown(exception.Error))
            {
                return false;
            }
            catch (RemoteServerException exception)
            {
                _skipped.Enqueue((change, exception.Error));
                LogSkipped(logger, serverNumber, change.Kind, exception.Error);
            }

            journal.Remove(change);
        }

        return true;
    }

    /// <summary>Writes a change found at start to the journal, as a rename made while the provider wasn't running.</summary>
    public void Record(JournalEntry change) => Postpone(change);

    private static bool IsServerDown(RemoteError error) =>
        error is RemoteError.Unreachable or RemoteError.SignInFailed or RemoteError.KeyNotTrusted or RemoteError.KeyFile or RemoteError.NoSafeAlgorithm;

    private int Postpone(JournalEntry change)
    {
        try
        {
            journal.Add(change);
            LogPostponed(logger, serverNumber, change.Kind);
            uploads.Wake();
            return CloudFileStatus.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the journal the change would be lost on the server, so the PC refuses it.
            LogJournalFailed(logger, serverNumber, exception);
            return CloudFileStatus.NetworkUnavailable;
        }
    }

    private string Remote(string relative) => ItemIdentity.RemotePath(remoteFolder, relative);

    private Task ApplyAsync(IRemoteServer server, JournalEntry change, CancellationToken cancellation) => change.Kind switch
    {
        JournalKind.Rename => RenameAsync(server, change, cancellation),
        _ => change.IsDirectory ? DeleteFolderAsync(server, change, cancellation) : DeleteFileAsync(server, change, cancellation),
    };

    private async Task RenameAsync(IRemoteServer server, JournalEntry change, CancellationToken cancellation)
    {
        try
        {
            await server.RenameAsync(Remote(change.Path), Remote(change.Target!), cancellation).ConfigureAwait(false);
        }
        catch (RemoteServerException exception) when (exception.Error == RemoteError.NotFound)
        {
            // It's gone from the server: there's nothing to rename, and a copy with changes is uploaded under its new name.
        }
    }

    private async Task DeleteFileAsync(IRemoteServer server, JournalEntry change, CancellationToken cancellation)
    {
        var known = change.Size is { } size && change.LastWriteTimeUtc is { } time ? new JournalFile(change.Path, size, time) : null;
        if (!await DeleteIfKnownAsync(server, change.Path, known, cancellation).ConfigureAwait(false))
        {
            LogKept(logger, serverNumber);
        }
    }

    // The folder's files the PC knew are deleted, then its folders from the deepest, if they're
    // empty: whatever else is on the server stays, and the folder comes back with it.
    private async Task DeleteFolderAsync(IRemoteServer server, JournalEntry change, CancellationToken cancellation)
    {
        int kept = 0;
        var folders = new HashSet<string>(StringComparer.Ordinal) { change.Path };
        foreach (var file in change.Files ?? [])
        {
            if (!await DeleteIfKnownAsync(server, file.Path, file, cancellation).ConfigureAwait(false))
            {
                kept++;
            }

            for (string folder = Parent(file.Path); folder.Length > change.Path.Length; folder = Parent(folder))
            {
                folders.Add(folder);
            }
        }

        foreach (string folder in folders.OrderByDescending(path => path.Length))
        {
            try
            {
                await server.DeleteFolderAsync(Remote(folder), cancellation).ConfigureAwait(false);
            }
            catch (RemoteServerException exception) when (exception.Error is RemoteError.NotFound or RemoteError.Failed)
            {
                // Gone already, or not empty: what's left on the server comes back.
                kept += exception.Error == RemoteError.Failed ? 1 : 0;
            }
        }

        if (kept > 0)
        {
            LogKept(logger, serverNumber);
        }
    }

    // Deletes a file that's still the version the PC knew; returns false when one is there that isn't.
    private async Task<bool> DeleteIfKnownAsync(IRemoteServer server, string relative, JournalFile? known, CancellationToken cancellation)
    {
        var current = await server.StatAsync(Remote(relative), cancellation).ConfigureAwait(false);
        if (current is null)
        {
            return true;
        }

        bool isKnown = !current.IsDirectory
            && ((known is not null && known.Size == current.Size && known.LastWriteTimeUtc == current.LastWriteTimeUtc)
                || uploads.IsOwnVersion(relative, current));
        if (!isKnown)
        {
            return false;
        }

        try
        {
            await server.DeleteFileAsync(Remote(relative), cancellation).ConfigureAwait(false);
        }
        catch (RemoteServerException exception) when (exception.Error == RemoteError.NotFound)
        {
        }

        return true;
    }

    private static string Parent(string relative) => relative[..Math.Max(0, relative.LastIndexOf('/'))];

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a {Kind} made on the PC was made on the server.")]
    private static partial void LogMade(ILogger logger, int server, JournalKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: a {Kind} waits in the journal until the server can be reached.")]
    private static partial void LogPostponed(ILogger logger, int server, JournalKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: the server refused a {Kind} ({Error}), so the PC refused it too.")]
    private static partial void LogRefused(ILogger logger, int server, JournalKind kind, RemoteError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a {Kind} from the journal was skipped ({Error}).")]
    private static partial void LogSkipped(ILogger logger, int server, JournalKind kind, RemoteError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: a deleted file changed on the server since the PC saw it, so it stays there and comes back.")]
    private static partial void LogKept(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server {Server}: the journal can't be written, so a change was refused.")]
    private static partial void LogJournalFailed(ILogger logger, int server, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a rename or delete failed.")]
    private static partial void LogCrashed(ILogger logger, int server, Exception exception);
}
