using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// Always keep on this device and Free up space, as File Explorer's menu sets them: every file in a
/// kept folder is downloaded, one at a time, and kept folders are listed every 10 minutes while the
/// server can be reached, so what changes there is downloaded again; Free up space removes the
/// copies on the PC that have no changes waiting. Links on the server to folders aren't followed.
/// See "Downloading" in docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerKeeping(
    RootPaths paths,
    IPlaceholderSystem pc,
    ServerListings listings,
    IServerConnections connections,
    TimeProvider time,
    ILogger logger,
    int serverNumber)
{
    /// <summary>How often kept folders are listed.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    // A download or a copy that couldn't be removed is tried again after this long.
    private static readonly TimeSpan TryAgainAfter = TimeSpan.FromSeconds(10);

    private readonly WaitingChanges _queue = new(time);

    /// <summary>How many files and folders wait to be kept or freed.</summary>
    public int Waiting => _queue.Count;

    /// <summary>Looks at a file or folder whose pin or copy may have changed, as the folder's watch reports.</summary>
    public void Changed(string path)
    {
        if (paths.Relative(path) is not null)
        {
            _queue.Changed(path);
        }
    }

    /// <summary>
    /// Finds the files and folders kept on this device or to be freed, as at start, and, with
    /// <paramref name="keptFoldersOnly"/>, only the kept folders, as for the refresh every 10 minutes.
    /// </summary>
    public int FindPinned(bool keptFoldersOnly)
    {
        int found = 0;
        var folders = new Stack<string>();
        folders.Push(paths.LocalRoot);
        while (folders.Count > 0)
        {
            foreach (var item in pc.Items(folders.Pop()))
            {
                if (item.IsLink || item.State is not { } state)
                {
                    continue;
                }

                bool kept = state.Pin == PinState.Pinned && (item.IsDirectory || !keptFoldersOnly);
                if (kept || (!keptFoldersOnly && state.Pin == PinState.Unpinned))
                {
                    _queue.Later(item.Path, TimeSpan.Zero);
                    found++;
                }

                // A kept folder's contents follow it; there's no need to look further.
                if (item.IsDirectory && !kept)
                {
                    folders.Push(item.Path);
                }
            }
        }

        return found;
    }

    /// <summary>Keeps and frees as asked, one file at a time, until <paramref name="cancellation"/> stops it.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (await _queue.NextAsync(cancellation).ConfigureAwait(false) is not { } path)
                {
                    continue;
                }

                var outcome = await KeepAsync(path, cancellation).ConfigureAwait(false);
                if (outcome == UploadOutcome.Busy)
                {
                    _queue.Later(path, TryAgainAfter);
                }
                else if (outcome == UploadOutcome.Waiting)
                {
                    _queue.Later(path, TimeSpan.Zero);
                    await connections.State.WaitForRetryAsync(cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Keeps or frees one file or folder. Never throws, except when cancelled.</summary>
    public async Task<UploadOutcome> KeepAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return await KeepItemAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCrashed(logger, serverNumber, exception);
            return UploadOutcome.Failed;
        }
    }

    private async Task<UploadOutcome> KeepItemAsync(string path, CancellationToken cancellation)
    {
        if (pc.Inspect(path) is not { State: { } state } item || item.IsLink)
        {
            return UploadOutcome.Done;
        }

        if (state.Pin == PinState.Unpinned)
        {
            return Free(item, state);
        }

        if (!IsKept(item))
        {
            return UploadOutcome.Done;
        }

        if (item.IsDirectory)
        {
            return await KeepFolderAsync(item).ConfigureAwait(false);
        }

        if (state.IsDownloaded)
        {
            return UploadOutcome.Done;
        }

        // Windows asks this process for the data, as it would a program, and waits for it.
        int result = await Task.Run(() => pc.Hydrate(path), cancellation).ConfigureAwait(false);
        if (result == 0)
        {
            LogKept(logger, serverNumber);
            return UploadOutcome.Done;
        }

        LogKeepFailed(logger, serverNumber, result);
        return connections.State.FailFast() is null ? UploadOutcome.Busy : UploadOutcome.Waiting;
    }

    // A kept folder is listed from the server, so what's new there is kept too; a link isn't followed.
    private async Task<UploadOutcome> KeepFolderAsync(LocalItem folder)
    {
        if (folder.Identity is { IsLink: true } || paths.Relative(folder.Path) is not { } relative)
        {
            return UploadOutcome.Done;
        }

        if (!await listings.RefreshAsync(relative).ConfigureAwait(false))
        {
            return connections.State.FailFast() is null ? UploadOutcome.Failed : UploadOutcome.Waiting;
        }

        foreach (var item in pc.Items(folder.Path))
        {
            if (!item.IsLink && item.State is { } state && (item.IsDirectory || !state.IsDownloaded))
            {
                _queue.Later(item.Path, TimeSpan.Zero);
            }
        }

        return UploadOutcome.Done;
    }

    // Free up space removes a copy only when nothing waits for the server; then the pin is cleared,
    // so opening the file later downloads and keeps it, as for any other file.
    private UploadOutcome Free(LocalItem item, PlaceholderState state)
    {
        if (item.IsDirectory)
        {
            foreach (var child in pc.Items(item.Path))
            {
                if (!child.IsLink && child.State is not null)
                {
                    _queue.Later(child.Path, TimeSpan.Zero);
                }
            }
        }
        else if (state.IsDownloaded && state.InSync)
        {
            int result = pc.Dehydrate(item.Path);
            if (result != 0)
            {
                // A program has it open; it's tried again.
                LogFreeFailed(logger, serverNumber, result);
                return UploadOutcome.Busy;
            }

            LogFreed(logger, serverNumber);
        }

        int unpinned = pc.SetPinState(item.Path, PinState.Unspecified);
        if (unpinned != 0)
        {
            LogFreeFailed(logger, serverNumber, unpinned);
        }

        return UploadOutcome.Done;
    }

    // Kept itself, or in a kept folder that isn't a link; a folder or file set otherwise isn't.
    private bool IsKept(LocalItem item)
    {
        if (item.State!.Pin is PinState.Pinned)
        {
            return true;
        }

        if (item.State.Pin is not (PinState.Unspecified or PinState.Inherit))
        {
            return false;
        }

        string? parent = Path.GetDirectoryName(item.Path);
        while (parent is not null && paths.Relative(parent) is not null)
        {
            if (pc.Inspect(parent) is not { State: { } state } folder)
            {
                return false;
            }

            if (state.Pin is PinState.Pinned)
            {
                return folder.Identity is not { IsLink: true };
            }

            if (state.Pin is not (PinState.Unspecified or PinState.Inherit) || folder.Identity is { IsLink: true })
            {
                return false;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: downloaded a file kept on this device.")]
    private static partial void LogKept(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a file kept on this device couldn't be downloaded (0x{Result:X8}); it's tried again.")]
    private static partial void LogKeepFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: freed up a file's space.")]
    private static partial void LogFreed(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a file's space couldn't be freed (0x{Result:X8}).")]
    private static partial void LogFreeFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: keeping a file on this device failed.")]
    private static partial void LogCrashed(ILogger logger, int server, Exception exception);
}
