using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// Which of a folder's pictures shows next: each in a shuffled order, every picture once before any
/// repeats, and a new order when the pictures in the folder change. See "Pictures" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class FolderOrder
{
    // A hash of 8 bytes tells the folder's lists of pictures apart, and is all that's kept.
    private const int FingerprintBytes = 8;

    // Each new seed has one chance in the number of pictures to start with the same picture.
    private const int MaximumTries = 8;

    /// <summary>The picture that shows next, and the folder's new place.</summary>
    /// <param name="folder">The folder's full path.</param>
    /// <param name="pictures">The folder's pictures, by name; at least one.</param>
    /// <param name="place">The folder's place from last time; <see langword="null"/> the first time.</param>
    /// <param name="newSeed">A seed for a new order.</param>
    public static (string Picture, FolderPlace Place) Next(string folder, IReadOnlyList<string> pictures, FolderPlace? place, Func<int> newSeed)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(newSeed);
        ArgumentOutOfRangeException.ThrowIfZero(pictures.Count);

        var fingerprint = Fingerprint(pictures);
        if (place is not null && place.Folder == folder && place.Pictures == fingerprint && place.Next >= 0 && place.Next < pictures.Count)
        {
            return (Order(pictures, place.Seed)[place.Next], place with { Next = place.Next + 1 });
        }

        // A new order, which doesn't start with the picture that showed last when the pictures are the same.
        var shownLast = place is not null && place.Pictures == fingerprint && place.Next == pictures.Count
            ? Order(pictures, place.Seed)[^1]
            : null;
        var seed = newSeed();
        for (var tries = 1; pictures.Count > 1 && tries < MaximumTries && string.Equals(Order(pictures, seed)[0], shownLast, StringComparison.Ordinal); tries++)
        {
            seed = newSeed();
        }

        return (Order(pictures, seed)[0], new FolderPlace(folder, fingerprint, seed, 1));
    }

    /// <summary>
    /// The picture that showed last, for a look within the same period; <see langword="null"/>
    /// when the folder's pictures changed since, or none showed yet.
    /// </summary>
    public static string? Current(string folder, IReadOnlyList<string> pictures, FolderPlace? place)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(pictures);

        return place is not null && place.Folder == folder && pictures.Count > 0 && place.Next > 0 && place.Next <= pictures.Count && place.Pictures == Fingerprint(pictures)
            ? Order(pictures, place.Seed)[place.Next - 1]
            : null;
    }

    // The pictures in the order a seed shuffles them into (Fisher-Yates).
    private static List<string> Order(IReadOnlyList<string> pictures, int seed)
    {
        var order = pictures.ToList();
        var random = new Random(seed);
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private static string Fingerprint(IReadOnlyList<string> pictures)
    {
        var names = string.Join('\n', pictures.Select(picture => Path.GetFileName(picture).ToUpperInvariant()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(names)), 0, FingerprintBytes);
    }
}
