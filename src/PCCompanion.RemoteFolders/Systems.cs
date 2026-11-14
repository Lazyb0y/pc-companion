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
    /// <summary>Runs <paramref name="work"/> on the connection for listings and changes.</summary>
    /// <exception cref="RemoteServerException">The server can't be reached, refused the sign-in, or failed the request.</exception>
    Task<T> UseAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation);
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
            var state = Read(entry.FullName);
            var identity = state is null ? null : ItemIdentity.FromBytes([.. state.Identity]);
            items.Add(new LocalItem(entry.Name, entry.FullName, entry is DirectoryInfo, state, identity));
        }

        return items;
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

/// <summary>Registering and connecting sync roots with Windows.</summary>
public sealed class WindowsSyncRootSystem : ISyncRootSystem
{
    public void Register(SyncRootRegistration registration) => SyncRoots.Register(registration);

    public void Unregister(string id) => SyncRoots.Unregister(id);

    public IReadOnlyList<(string Id, string Folder)> Current(string idPrefix) => SyncRoots.Current(idPrefix);

    public IDisposable Connect(string folder, ICloudFileRequests requests, Action<Exception> failed) => SyncRootConnection.Connect(folder, requests, failed);
}
