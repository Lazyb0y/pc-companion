using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Provider;
using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>A folder for Windows to show as a sync provider's, with its name and icon.</summary>
/// <param name="Id">The sync root's ID, from <see cref="SyncRoots.MakeId"/>.</param>
/// <param name="Folder">The folder's full path, on an NTFS drive.</param>
/// <param name="DisplayName">The name File Explorer's navigation pane shows.</param>
/// <param name="IconResource">The icon File Explorer shows, as <c>path,-id</c> or <c>path,index</c>.</param>
public sealed record SyncRootRegistration(string Id, string Folder, string DisplayName, string IconResource);

/// <summary>
/// Registers folders as sync roots, as cloud storage apps do: File Explorer then shows each in its
/// navigation pane, with the Status column and the menu's <b>Always keep on this device</b> and
/// <b>Free up space</b>. Registering needs neither package identity nor admin rights, and is per
/// user. These methods wait for Windows, so they run off a UI thread.
/// </summary>
public static class SyncRoots
{
    /// <summary>The longest ID Windows accepts.</summary>
    public const int MaximumIdLength = 174;

    /// <summary>
    /// The ID Windows requires: the provider's name, the user's SID and the provider's own name for
    /// the folder, joined by <c>!</c>, which none of them may contain.
    /// </summary>
    /// <exception cref="ArgumentException">A part is empty or contains <c>!</c>, or the ID is too long.</exception>
    public static string MakeId(string provider, string userSid, string account)
    {
        foreach (string part in new[] { provider, userSid, account })
        {
            if (string.IsNullOrEmpty(part) || part.Contains('!', StringComparison.Ordinal))
            {
                throw new ArgumentException("A sync root ID's parts are not empty and have no '!'.", nameof(account));
            }
        }

        string id = $"{provider}!{userSid}!{account}";
        return id.Length <= MaximumIdLength ? id : throw new ArgumentException($"A sync root ID has at most {MaximumIdLength} characters.", nameof(account));
    }

    /// <summary>
    /// Registers the folder, or registers it again with a new name or icon, which keeps its files.
    /// Windows downloads a file in full when a program reads it, may free up unused ones when
    /// Storage Sense is on, asks for a folder's entries when it's listed, and lets the user keep
    /// files on the PC or free up space.
    /// </summary>
    /// <exception cref="Exception">Windows refused, for example for a folder that isn't on an NTFS drive.</exception>
    public static void Register(SyncRootRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var folder = StorageFolder.GetFolderFromPathAsync(registration.Folder).AsTask().GetAwaiter().GetResult();
        var info = new StorageProviderSyncRootInfo
        {
            Id = registration.Id,
            Path = folder,
            DisplayNameResource = registration.DisplayName,
            IconResource = registration.IconResource,
            HydrationPolicy = StorageProviderHydrationPolicy.Full,
            HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.AutoDehydrationAllowed,
            PopulationPolicy = StorageProviderPopulationPolicy.Full,
            InSyncPolicy = StorageProviderInSyncPolicy.PreserveInsyncForSyncEngine,
            HardlinkPolicy = StorageProviderHardlinkPolicy.None,
            ProtectionMode = StorageProviderProtectionMode.Unknown,
            AllowPinning = true,
            ShowSiblingsAsGroup = false,
            Version = "1",
            Context = CryptographicBuffer.ConvertStringToBinary(registration.Id, BinaryStringEncoding.Utf8),
        };
        StorageProviderSyncRootManager.Register(info);
    }

    /// <summary>
    /// Unregisters a sync root of the current user. Windows deletes its online-only files, and the
    /// others become ordinary files. Disconnect first.
    /// </summary>
    /// <exception cref="Exception">Windows refused, for example for an ID it doesn't know.</exception>
    public static void Unregister(string id) => StorageProviderSyncRootManager.Unregister(id);

    /// <summary>The current user's sync roots whose IDs start with <paramref name="idPrefix"/>, with their folders.</summary>
    public static IReadOnlyList<(string Id, string Folder)> Current(string idPrefix) =>
        [.. StorageProviderSyncRootManager.GetCurrentSyncRoots()
            .Where(root => root.Id.StartsWith(idPrefix, StringComparison.Ordinal))
            .Select(root => (root.Id, root.Path?.Path ?? string.Empty))];

    /// <summary>
    /// Unregisters the sync root at <paramref name="folder"/> with the filter only, which also works
    /// for another user's folder when called as SYSTEM, where <see cref="Unregister"/> doesn't find
    /// it. Windows deletes its online-only files; its registration in the registry stays. Returns
    /// Windows' HRESULT.
    /// </summary>
    public static int UnregisterFolder(string folder) => PInvoke.CfUnregisterSyncRoot(folder).Value;
}
