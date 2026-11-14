using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>Windows' request to list a folder, as <see cref="ServerListings"/> answers it.</summary>
public interface IListingRequest
{
    /// <summary>What the folder's placeholder keeps; empty for the server's folder itself.</summary>
    IReadOnlyList<byte> Identity { get; }

    /// <summary>Whether the provider itself lists the folder, as when it looks for changes on the PC.</summary>
    bool IsFromProvider { get; }

    /// <summary>
    /// Answers with new entries. When <paramref name="complete"/>, Windows treats the folder as
    /// complete until <see cref="IPlaceholderSystem.ListAgainOnNextOpen"/>; otherwise it asks again
    /// each time the folder is listed, and can't create files in it.
    /// </summary>
    int Answer(IReadOnlyList<PlaceholderEntry> entries, bool complete, Span<int> results);

    /// <summary>Fails the listing with <paramref name="status"/>.</summary>
    int Refuse(int status);
}

/// <summary>A listing request from Windows.</summary>
public sealed class ListingRequest(FetchPlaceholdersRequest request) : IListingRequest
{
    public IReadOnlyList<byte> Identity => request.Identity;

    public bool IsFromProvider => request.Program?.ProcessId == Environment.ProcessId;

    public int Answer(IReadOnlyList<PlaceholderEntry> entries, bool complete, Span<int> results) => request.Answer(entries, complete, results);

    public int Refuse(int status) => request.Refuse(status);
}

/// <summary>
/// Lists a server's folders when Windows asks, and applies what changed on the server. Each answer
/// tells Windows the folder is complete, since Windows can't create a file in a folder that isn't,
/// and 30 seconds later the folder is listed again the next time a program opens it. See
/// "Changes on the server" in docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerListings(
    string localRoot,
    string remoteFolder,
    IServerConnections connections,
    IPlaceholderSystem pc,
    TimeProvider time,
    ILogger logger,
    int serverNumber) : IDisposable
{
    /// <summary>How long a listing is reused: Windows asks again each time a program lists the folder.</summary>
    public static readonly TimeSpan ReuseFor = TimeSpan.FromSeconds(30);

    /// <summary>How long Windows waits for the first listing of a folder.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(10);

    /// <summary>How long Windows waits for a later listing; the rest appears when it finishes.</summary>
    public static readonly TimeSpan LaterWait = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _listed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<HiddenReason, int>> _hidden = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ITimer> _listAgain = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    /// <summary>How many entries on the server can't be shown, by reason, in the folders listed so far.</summary>
    public IReadOnlyDictionary<HiddenReason, int> Hidden =>
        _hidden.Values.SelectMany(counts => counts).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));

    /// <summary>When each folder, by its path below the server's folder, was last listed.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> Listed => _listed;

    /// <summary>Answers Windows' request to list a folder. Never throws: a failure refuses the request.</summary>
    public async Task HandleAsync(IListingRequest request)
    {
        bool answered = false;
        try
        {
            // The provider looks only at what's on the PC; asking the server would list every
            // folder, and a complete answer would hide what's on the server from programs.
            if (request.IsFromProvider)
            {
                request.Answer([], complete: false, []);
                answered = true;
                return;
            }

            string? relative = RelativeFolder(request.Identity);
            if (relative is null)
            {
                request.Refuse(CloudFileStatus.Unsuccessful);
                answered = true;
                return;
            }

            string local = ListingDiff.LocalPath(localRoot, relative);
            if (_listed.TryGetValue(relative, out var at) && time.GetUtcNow() - at < ReuseFor)
            {
                Complete(request, [], [], relative, local);
                answered = true;
                return;
            }

            bool listedBefore = pc.Items(local).Count > 0;
            var listing = ListAsync(relative);
            var finished = await Task.WhenAny(listing, Task.Delay(listedBefore ? LaterWait : FirstWait, time)).ConfigureAwait(false);
            if (finished != listing)
            {
                // Windows gets what's known; the rest appears when the listing finishes.
                Complete(request, [], [], relative, local);
                answered = true;
                LogListingSlow(logger, serverNumber);
                _ = FinishLaterAsync(listing, relative, local);
                return;
            }

            RemoteListing remote;
            try
            {
                remote = await listing.ConfigureAwait(false);
            }
            catch (RemoteServerException exception)
            {
                LogListingFailed(logger, serverNumber, exception.Error);
                if (listedBefore)
                {
                    Complete(request, [], [], relative, local);
                }
                else
                {
                    request.Refuse(StatusFor(exception.Error));
                }

                answered = true;
                return;
            }

            var changes = ListingDiff.Compute(relative, remote, pc.Items(local));
            var results = new int[changes.New.Count];
            int result = Complete(request, [.. changes.New.Select(added => added.Entry)], results, relative, local);
            answered = true;
            if (result != 0)
            {
                LogAnswerFailed(logger, serverNumber, result);
            }

            MarkNew(local, changes.New, results);
            Apply(local, changes);
            RecordListing(relative, changes);
        }
        catch (Exception exception)
        {
            LogListingCrashed(logger, serverNumber, exception);
            if (!answered)
            {
                request.Refuse(CloudFileStatus.Unsuccessful);
            }
        }
    }

    /// <summary>Lists a folder now and applies what changed, as Sync now and the refresh of kept folders do.</summary>
    /// <returns>Whether the server could be listed.</returns>
    public async Task<bool> RefreshAsync(string relativeFolder)
    {
        string local = ListingDiff.LocalPath(localRoot, relativeFolder);
        try
        {
            var remote = await ListAsync(relativeFolder).ConfigureAwait(false);
            ApplyWithNew(relativeFolder, local, remote);
            return true;
        }
        catch (RemoteServerException exception)
        {
            LogListingFailed(logger, serverNumber, exception.Error);
            return false;
        }
    }

    /// <summary>
    /// Has Windows list every folder on the PC again the next time it's opened, as at start, since
    /// a folder a listing marked complete before the provider stopped would otherwise stay so.
    /// </summary>
    public void ListAllAgainOnNextOpen()
    {
        var folders = new Stack<string>();
        folders.Push(localRoot);
        while (folders.Count > 0 && !_disposed)
        {
            string folder = folders.Pop();
            ListAgain(string.Empty, folder);
            foreach (var item in pc.Items(folder))
            {
                if (item.IsDirectory && !item.IsLink && item.State is not null)
                {
                    folders.Push(item.Path);
                }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var timer in _listAgain.Values)
        {
            timer.Dispose();
        }

        _listAgain.Clear();
    }

    /// <summary>The Cloud Files status Windows gives programs for a server's error.</summary>
    public static int StatusFor(RemoteError error) => error switch
    {
        RemoteError.SignInFailed or RemoteError.KeyNotTrusted or RemoteError.KeyFile => CloudFileStatus.AuthenticationFailed,
        RemoteError.PermissionDenied => CloudFileStatus.AccessDenied,
        RemoteError.NotFound or RemoteError.Changed or RemoteError.Failed => CloudFileStatus.Unsuccessful,
        _ => CloudFileStatus.NetworkUnavailable,
    };

    // The folder a request is about: the server's folder for an empty identity, the path in a
    // folder's identity, or none for a placeholder Remote Folders didn't make.
    private static string? RelativeFolder(IReadOnlyList<byte> identity)
    {
        if (identity.Count == 0)
        {
            return string.Empty;
        }

        return ItemIdentity.FromBytes([.. identity]) is { IsDirectory: true } folder ? folder.RelativePath : null;
    }

    // Answers that the folder is complete, and has Windows ask again once the answer is old.
    private int Complete(IListingRequest request, IReadOnlyList<PlaceholderEntry> entries, Span<int> results, string relative, string local)
    {
        int result = request.Answer(entries, complete: true, results);
        if (_disposed)
        {
            return result;
        }

        var timer = time.CreateTimer(_ => ListAgain(relative, local), null, ReuseFor, Timeout.InfiniteTimeSpan);
        _listAgain.AddOrUpdate(relative, timer, (_, old) =>
        {
            old.Dispose();
            return timer;
        });
        return result;
    }

    private void ListAgain(string relative, string local)
    {
        if (_listAgain.TryRemove(relative, out var timer))
        {
            timer.Dispose();
        }

        if (_disposed)
        {
            return;
        }

        try
        {
            int result = pc.ListAgainOnNextOpen(local);
            if (result != 0)
            {
                LogListAgainFailed(logger, serverNumber, result);
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            // The folder is gone, or was renamed; a renamed one is listed when it's opened.
            LogListAgainFailed(logger, serverNumber, exception.HResult);
        }
    }

    private Task<RemoteListing> ListAsync(string relativeFolder) =>
        connections.UseAsync((server, cancellation) => server.ListAsync(ItemIdentity.RemotePath(remoteFolder, relativeFolder), RemoteNames.MaximumEntries, cancellation), CancellationToken.None);

    private async Task FinishLaterAsync(Task<RemoteListing> listing, string relative, string local)
    {
        try
        {
            ApplyWithNew(relative, local, await listing.ConfigureAwait(false));
        }
        catch (RemoteServerException exception)
        {
            LogListingFailed(logger, serverNumber, exception.Error);
        }
        catch (Exception exception)
        {
            LogListingCrashed(logger, serverNumber, exception);
        }
    }

    private void ApplyWithNew(string relative, string local, RemoteListing remote)
    {
        var changes = ListingDiff.Compute(relative, remote, pc.Items(local));
        if (changes.New.Count > 0)
        {
            var results = new int[changes.New.Count];
            int result = pc.Create(local, [.. changes.New.Select(added => added.Entry)], results);
            if (result != 0)
            {
                LogAnswerFailed(logger, serverNumber, result);
            }

            MarkNew(local, changes.New, results);
        }

        Apply(local, changes);
        RecordListing(relative, changes);
    }

    private void RecordListing(string relative, ListingChanges changes)
    {
        _listed[relative] = time.GetUtcNow();
        _hidden[relative] = changes.Hidden;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            int hidden = changes.Hidden.Values.Sum();
            LogListed(logger, serverNumber, changes.New.Count, changes.Changed.Count, changes.Gone.Count, hidden);
        }
    }

    // Every file from the server is marked as from the internet, without the server's address.
    private void MarkNew(string local, IReadOnlyList<(PlaceholderEntry Entry, ItemIdentity Identity)> added, int[] results)
    {
        for (int i = 0; i < added.Count; i++)
        {
            if (results[i] == 0 && !added[i].Entry.IsDirectory)
            {
                try
                {
                    pc.MarkFromInternet(System.IO.Path.Join(local, added[i].Entry.Name));
                }
                catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
                {
                    LogMarkFailed(logger, serverNumber, exception.HResult);
                }
            }
        }
    }

    private void Apply(string local, ListingChanges changes)
    {
        foreach (var change in changes.Changed)
        {
            // Only a copy that's still in sync is replaced: Windows checks again as it removes it.
            if (change.Local.State is not { InSync: true })
            {
                continue;
            }

            // Windows doesn't remove a kept file's copy, so its pin steps aside for the update; set
            // again, it has the new version downloaded at once.
            var entry = ListingDiff.Placeholder(change.Remote, change.NewIdentity);
            bool kept = change.Local.State.Pin == PinState.Pinned;
            if (kept)
            {
                pc.SetPinState(change.Local.Path, PinState.Unspecified);
            }

            int result = pc.Update(change.Local.Path, entry, change.NewIdentity.ToBytes(), PlaceholderUpdate.VerifyInSync | PlaceholderUpdate.MarkInSync | PlaceholderUpdate.Dehydrate);
            if (kept)
            {
                pc.SetPinState(change.Local.Path, PinState.Pinned);
            }

            if (result != 0)
            {
                LogUpdateFailed(logger, serverNumber, result);
            }
        }

        foreach (var item in changes.Gone)
        {
            try
            {
                RemoveGone(item);
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                LogRemoveFailed(logger, serverNumber, exception.HResult);
            }
        }
    }

    // A copy with changes that wait for the server stays; it's uploaded again as a new file.
    private void RemoveGone(LocalItem item)
    {
        if (item.State is not { InSync: true })
        {
            return;
        }

        if (item.IsDirectory)
        {
            if (pc.IsAllInSync(item.Path))
            {
                pc.Recycle(item.Path);
            }
        }
        else if (Placeholders.IsOnlineOnly(item.State.Attributes))
        {
            pc.Delete(item.Path);
        }
        else
        {
            pc.Recycle(item.Path);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: listed a folder: {New} new, {Changed} changed, {Gone} gone, {Hidden} not shown.")]
    private static partial void LogListed(ILogger logger, int server, int @new, int changed, int gone, int hidden);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a listing took longer than Windows waits; it's applied when it finishes.")]
    private static partial void LogListingSlow(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a folder couldn't be set to be listed again (0x{Result:X8}).")]
    private static partial void LogListAgainFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a folder couldn't be listed ({Error}).")]
    private static partial void LogListingFailed(ILogger logger, int server, RemoteError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: listing a folder failed.")]
    private static partial void LogListingCrashed(ILogger logger, int server, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: Windows refused a folder's entries (0x{Result:X8}).")]
    private static partial void LogAnswerFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a file couldn't get its Mark of the Web (0x{Result:X8}).")]
    private static partial void LogMarkFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a changed file wasn't updated (0x{Result:X8}); it may have changes of its own, or be open.")]
    private static partial void LogUpdateFailed(ILogger logger, int server, int result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a file deleted on the server couldn't be removed from the PC (0x{Result:X8}).")]
    private static partial void LogRemoveFailed(ILogger logger, int server, int result);
}
