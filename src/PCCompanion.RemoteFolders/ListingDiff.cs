using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>A file or folder in a folder on the PC, with its placeholder state, if it's a placeholder.</summary>
/// <param name="Identity">What its placeholder says about it on the server, or <see langword="null"/> when it isn't one of Remote Folders' placeholders, such as a new file.</param>
public sealed record LocalItem(string Name, string Path, bool IsDirectory, PlaceholderState? State, ItemIdentity? Identity);

/// <summary>A file on the server whose version isn't the one its placeholder came from.</summary>
public sealed record ChangedItem(LocalItem Local, RemoteEntry Remote, ItemIdentity NewIdentity);

/// <summary>What a folder's listing on the server means for the folder on the PC.</summary>
public sealed record ListingChanges(
    IReadOnlyList<(PlaceholderEntry Entry, ItemIdentity Identity)> New,
    IReadOnlyList<ChangedItem> Changed,
    IReadOnlyList<LocalItem> Gone,
    IReadOnlyDictionary<HiddenReason, int> Hidden)
{
    public bool IsEmpty => New.Count == 0 && Changed.Count == 0 && Gone.Count == 0;
}

/// <summary>
/// Compares a folder's listing on the server with the folder on the PC. See "Changes on the
/// server" in docs/features/files/remote-folders.md.
/// </summary>
public static class ListingDiff
{
    /// <summary>
    /// What changed on the server: entries without a file or folder of that name on the PC are new;
    /// files whose size or time differ from their placeholder's are changed; placeholders whose
    /// entry is gone from the server are gone, unless the listing was cut short, or the placeholder
    /// was renamed or moved on the PC and waits to be renamed on the server. Files made on the PC,
    /// which aren't placeholders yet, are left to the upload.
    /// </summary>
    /// <param name="relativeFolder">The folder's path below the server's folder, with <c>/</c>; empty for the server's folder itself.</param>
    public static ListingChanges Compute(string relativeFolder, RemoteListing remote, IReadOnlyList<LocalItem> local)
    {
        ArgumentNullException.ThrowIfNull(relativeFolder);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(local);

        var selection = RemoteNames.Select(remote.Entries, entry => entry.Name, remote.IsTruncated);
        var hidden = new Dictionary<HiddenReason, int>(selection.Hidden);
        var localByName = new Dictionary<string, LocalItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in local)
        {
            localByName.TryAdd(item.Name, item);
        }

        var added = new List<(PlaceholderEntry, ItemIdentity)>();
        var changed = new List<ChangedItem>();
        var onServer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in selection.Shown)
        {
            var identity = ItemIdentity.Of(relativeFolder, entry.Name, entry.IsDirectory, entry.Size, entry.LastWriteTimeUtc);
            if (!ItemIdentity.Fits(identity.RelativePath))
            {
                hidden[HiddenReason.TooLong] = hidden.GetValueOrDefault(HiddenReason.TooLong) + 1;
                continue;
            }

            onServer.Add(entry.Name);
            if (!localByName.TryGetValue(entry.Name, out var item))
            {
                added.Add((Placeholder(entry, identity), identity));
            }
            else if (item.Identity is { IsDirectory: false } known && !entry.IsDirectory && !known.IsSameVersion(entry.Size, entry.LastWriteTimeUtc)
                && known.RelativePath == identity.RelativePath)
            {
                changed.Add(new ChangedItem(item, entry, identity));
            }
        }

        var gone = remote.IsTruncated
            ? []
            : local.Where(item => item.Identity is { } known
                && !onServer.Contains(item.Name)
                && known.RelativePath == ItemIdentity.Join(relativeFolder, item.Name)).ToList();

        // Entries whose names weren't shown aren't gone: hidden names are still on the server.
        var hiddenNames = new HashSet<string>(remote.Entries.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
        gone = [.. gone.Where(item => !hiddenNames.Contains(item.Name))];
        return new ListingChanges(added, changed, gone, hidden);
    }

    /// <summary>The placeholder for an entry on the server.</summary>
    public static PlaceholderEntry Placeholder(RemoteEntry entry, ItemIdentity identity) =>
        new(entry.Name, identity.ToBytes(), entry.IsDirectory, entry.IsDirectory ? 0 : entry.Size, entry.LastWriteTimeUtc)
        {
            Hidden = RemoteNames.IsHiddenOnPc(entry.Name),
        };

    /// <summary>The path on the PC of a path below the server's folder.</summary>
    public static string LocalPath(string localRoot, string relativePath) =>
        relativePath.Length == 0 ? localRoot : Path.Join(localRoot, relativePath.Replace('/', '\\'));
}
