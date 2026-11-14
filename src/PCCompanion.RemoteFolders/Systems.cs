using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// The PC's side of a server's folder: its placeholders and files. Tests use a fake; the provider
/// uses <see cref="WindowsPlaceholderSystem"/>.
/// </summary>
public interface IPlaceholderSystem
{
    /// <summary>Makes placeholders in <paramref name="folder"/>; see <see cref="Placeholders.Create"/>.</summary>
    int Create(string folder, IReadOnlyList<PlaceholderEntry> entries, Span<int> results);

    /// <summary>The files and folders in <paramref name="folder"/>, with their placeholder states; empty when it's gone.</summary>
    IReadOnlyList<LocalItem> Items(string folder);

    /// <summary>A placeholder's state, or <see langword="null"/> when it isn't one or is gone.</summary>
    PlaceholderState? Read(string path);

    /// <summary>See <see cref="Placeholders.Update"/>.</summary>
    int Update(string path, PlaceholderEntry metadata, byte[] identity, PlaceholderUpdate update);

    /// <summary>Marks a file as downloaded from the internet, with <c>ZoneId=3</c> and no address.</summary>
    void MarkFromInternet(string path);

    /// <summary>Deletes an online-only placeholder, or an empty folder.</summary>
    void Delete(string path);

    /// <summary>Moves a file or folder to the Recycle Bin.</summary>
    RecycleResult Recycle(string path);

    /// <summary>Whether everything in <paramref name="folder"/> is a placeholder with nothing that waits for the server.</summary>
    bool IsAllInSync(string folder);

    /// <summary>Has Windows ask for a folder's contents again; see <see cref="Placeholders.ListAgainOnNextOpen"/>.</summary>
    /// <exception cref="IOException">The folder can't be opened, for example because it's gone.</exception>
    int ListAgainOnNextOpen(string folder);

    /// <summary>A file or folder, with its placeholder state, or <see langword="null"/> when it's gone.</summary>
    LocalItem? Inspect(string path);

    /// <summary>Opens a file for an upload; see <see cref="UploadFile.Open"/>.</summary>
    /// <returns>Windows' HRESULT, 0 when <paramref name="file"/> is open.</returns>
    int OpenForUpload(string path, out IUploadSource? file);

    /// <summary>Makes a new folder a placeholder; see <see cref="Placeholders.ConvertFolder"/>.</summary>
    int ConvertFolder(string path, byte[] identity);

    /// <summary>Gives a placeholder a new identity; see <see cref="Placeholders.UpdateIdentity"/>.</summary>
    int UpdateIdentity(string path, byte[] identity, bool markInSync);

    /// <summary>Marks a file that stays on the PC as excluded, which File Explorer shows.</summary>
    int Exclude(string path);

    /// <summary>
    /// The new files and folders, the changed files, and the placeholders renamed or moved since
    /// they were last on the server, in <paramref name="folder"/> and the folders below it, without
    /// following links.
    /// </summary>
    IEnumerable<string> FindChanges(string folder);

    /// <summary>
    /// Watches <paramref name="folder"/> and the folders below it: <paramref name="changed"/> gets
    /// each new, changed or renamed file and folder, and <paramref name="missed"/> is called when
    /// Windows reported more changes than it could keep. Dispose the watch to stop.
    /// </summary>
    IDisposable Watch(string folder, Action<string> changed, Action missed);
}

/// <summary>Registering and connecting sync roots; tests use a fake.</summary>
public interface ISyncRootSystem
{
    void Register(SyncRootRegistration registration);

    void Unregister(string id);

    IReadOnlyList<(string Id, string Folder)> Current(string idPrefix);

    IDisposable Connect(string folder, ICloudFileRequests requests, Action<Exception> failed);
}

/// <summary>
/// The connections to one server. Work runs on a connection that's open, connecting first when
/// needed, one request at a time per connection.
/// </summary>
public interface IServerConnections : IDisposable
{
    /// <summary>The server's state, and when it's tried again.</summary>
    ConnectionState State { get; }

    /// <summary>Runs <paramref name="work"/> on the connection for listings and changes.</summary>
    /// <exception cref="RemoteServerException">The server can't be reached, refused the sign-in, or failed the request.</exception>
    Task<T> UseAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation);

    /// <summary>Runs <paramref name="work"/> on a connection for downloads, so they don't hold up listings.</summary>
    /// <exception cref="RemoteServerException">The server can't be reached, refused the sign-in, or failed the request.</exception>
    Task<T> UseForTransferAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation);

    /// <summary>
    /// Lets the server be tried again at once, and closes the connections that aren't in use,
    /// which may have dropped: after a network change, after the PC wakes from sleep, or when the
    /// user signs in again.
    /// </summary>
    void TryAgainNow();
}

/// <summary>The PC's side of a server's folder, with Windows' Cloud Files API and file system.</summary>
public sealed class WindowsPlaceholderSystem(RecycleBin recycleBin) : IPlaceholderSystem
{
    private const string ZoneIdentifier = "[ZoneTransfer]\r\nZoneId=3\r\n";

    public int Create(string folder, IReadOnlyList<PlaceholderEntry> entries, Span<int> results) => Placeholders.Create(folder, entries, results);

    public IReadOnlyList<LocalItem> Items(string folder)
    {
        var info = new DirectoryInfo(folder);
        if (!info.Exists)
        {
            return [];
        }

        var items = new List<LocalItem>();
        foreach (var entry in info.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            items.Add(Item(entry));
        }

        return items;
    }

    public LocalItem? Inspect(string path)
    {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return entry.Exists ? Item(entry) : null;
    }

    public int OpenForUpload(string path, out IUploadSource? file)
    {
        int result = UploadFile.Open(path, out var opened);
        file = opened is null ? null : new UploadSource(opened);
        return result;
    }

    public int ListAgainOnNextOpen(string folder) => Placeholders.ListAgainOnNextOpen(folder);

    public int ConvertFolder(string path, byte[] identity) => Placeholders.ConvertFolder(path, identity);

    public int UpdateIdentity(string path, byte[] identity, bool markInSync)
    {
        try
        {
            return Placeholders.UpdateIdentity(path, identity, markInSync);
        }
        catch (IOException exception)
        {
            return exception.HResult;
        }
    }

    public int Exclude(string path) => Placeholders.SetPinState(path, PinState.Excluded);

    public IEnumerable<string> FindChanges(string folder)
    {
        var folders = new Stack<string>();
        folders.Push(folder);
        while (folders.Count > 0)
        {
            foreach (var item in Items(folders.Pop()))
            {
                if (item.IsLink)
                {
                    continue;
                }

                if (item.State is null || (!item.IsDirectory && !item.State.InSync && item.State.Pin != PinState.Excluded)
                    || (item.Identity is { } identity && identity.RelativePath != item.Path[(folder.Length + 1)..].Replace('\\', '/')))
                {
                    yield return item.Path;
                }

                if (item.IsDirectory)
                {
                    folders.Push(item.Path);
                }
            }
        }
    }

    public IDisposable Watch(string folder, Action<string> changed, Action missed)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, e) => changed(e.FullPath);
        watcher.Changed += (_, e) => changed(e.FullPath);
        watcher.Renamed += (_, e) => changed(e.FullPath);
        watcher.Error += (_, _) => missed();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    public PlaceholderState? Read(string path)
    {
        try
        {
            return Placeholders.Read(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public int Update(string path, PlaceholderEntry metadata, byte[] identity, PlaceholderUpdate update) => Placeholders.Update(path, metadata, identity, update);

    public void MarkFromInternet(string path) => File.WriteAllText(path + ":Zone.Identifier", ZoneIdentifier);

    public void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
        else
        {
            File.Delete(path);
        }
    }

    public RecycleResult Recycle(string path)
    {
        using var done = new ManualResetEventSlim();
        RecycleResult result = new(RecycleOutcome.Failed);
        recycleBin.Recycle(path, () => { }, outcome =>
        {
            result = outcome;
            done.Set();
        });
        done.Wait();
        return result;
    }

    // A reparse point that isn't a placeholder is a link, which stays on the PC.
    private LocalItem Item(FileSystemInfo entry)
    {
        var state = Read(entry.FullName);
        var identity = state is null ? null : ItemIdentity.FromBytes([.. state.Identity]);
        return new LocalItem(entry.Name, entry.FullName, entry is DirectoryInfo, state, identity)
        {
            IsLink = state is null && entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
        };
    }

    public bool IsAllInSync(string folder)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            if (Read(path) is not { InSync: true })
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A file opened for an upload, through <see cref="UploadFile"/>.</summary>
public sealed class UploadSource(UploadFile file) : IUploadSource
{
    public long Length => file.Length;

    public bool IsClosed => file.IsClosed;

    public Stream OpenRead() => file.OpenRead();

    public int MarkInSync(byte[] identity, bool isPlaceholder) => file.MarkInSync(identity, isPlaceholder);

    public int Rename(string newPath) => file.Rename(newPath);

    public void Dispose() => file.Dispose();
}

/// <summary>Registering and connecting sync roots with Windows.</summary>
public sealed class WindowsSyncRootSystem : ISyncRootSystem
{
    public void Register(SyncRootRegistration registration) => SyncRoots.Register(registration);

    public void Unregister(string id) => SyncRoots.Unregister(id);

    public IReadOnlyList<(string Id, string Folder)> Current(string idPrefix) => SyncRoots.Current(idPrefix);

    public IDisposable Connect(string folder, ICloudFileRequests requests, Action<Exception> failed) => SyncRootConnection.Connect(folder, requests, failed);
}
