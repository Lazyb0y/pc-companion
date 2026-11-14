using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders.Tests;

/// <summary>An SFTP server in memory: paths with <c>/</c>, files with their bytes and times.</summary>
public sealed class FakeRemoteServer : IRemoteServer
{
    private readonly Dictionary<string, (bool IsDirectory, byte[] Content, DateTime Time)> _items = new(StringComparer.Ordinal) { ["/home/ana"] = (true, [], DateTime.UnixEpoch) };

    public string HomeFolder => "/home/ana";

    public bool IsConnected { get; set; } = true;

    public bool CanReplaceAtOnce { get; set; } = true;

    /// <summary>When set, every request fails with it.</summary>
    public RemoteError? Failure { get; set; }

    /// <summary>When set, listings wait for it.</summary>
    public TaskCompletionSource? ListingGate { get; set; }

    public int Listings { get; private set; }

    /// <summary>Every file and folder on the server.</summary>
    public IReadOnlyCollection<string> Paths => _items.Keys;

    private DateTime _uploadTime = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    public void AddFile(string path, string content, DateTime? time = null) => _items[path] = (false, System.Text.Encoding.UTF8.GetBytes(content), time ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

    public void AddFolder(string path) => _items[path] = (true, [], new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

    public void Remove(string path) => _items.Remove(path);

    public string? Content(string path) => _items.TryGetValue(path, out var item) && !item.IsDirectory ? System.Text.Encoding.UTF8.GetString(item.Content) : null;

    public bool Exists(string path) => _items.ContainsKey(path);

    public Task<RemoteEntry?> StatAsync(string path, CancellationToken cancellation)
    {
        Fail();
        return Task.FromResult(_items.TryGetValue(path, out var item) ? Entry(path, item) : null);
    }

    public async Task<RemoteListing> ListAsync(string folder, int limit, CancellationToken cancellation)
    {
        Listings++;
        if (ListingGate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        Fail();
        if (!_items.TryGetValue(folder, out var item) || !item.IsDirectory)
        {
            throw new RemoteServerException(RemoteError.NotFound, "No such folder.");
        }

        var children = _items.Where(pair => Parent(pair.Key) == folder).Select(pair => Entry(pair.Key, pair.Value)).ToList();
        return new RemoteListing([.. children.Take(limit)], children.Count > limit);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellation)
    {
        Fail();
        return _items.TryGetValue(path, out var item) && !item.IsDirectory
            ? Task.FromResult<Stream>(new MemoryStream(item.Content, writable: false))
            : throw new RemoteServerException(RemoteError.NotFound, "No such file.");
    }

    public async Task UploadAsync(Stream content, string path, CancellationToken cancellation)
    {
        Fail();
        if (!_items.TryGetValue(Parent(path), out var folder) || !folder.IsDirectory)
        {
            throw new RemoteServerException(RemoteError.NotFound, "No such folder.");
        }

        using var copy = new MemoryStream();
        try
        {
            await content.CopyToAsync(copy, cancellation).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new RemoteServerException(RemoteError.Failed, "The upload failed.", exception);
        }

        _uploadTime = _uploadTime.AddSeconds(1);
        _items[path] = (false, copy.ToArray(), _uploadTime);
    }

    public Task RenameAsync(string source, string destination, CancellationToken cancellation)
    {
        Fail();
        if (!_items.ContainsKey(source) || _items.ContainsKey(destination))
        {
            throw new RemoteServerException(RemoteError.Failed, "Failure");
        }

        foreach (var key in _items.Keys.Where(key => key == source || key.StartsWith(source + "/", StringComparison.Ordinal)).ToList())
        {
            _items[destination + key[source.Length..]] = _items[key];
            _items.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task ReplaceAsync(string source, string destination, CancellationToken cancellation)
    {
        Fail();
        _items[destination] = _items[source];
        _items.Remove(source);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellation)
    {
        Fail();
        return _items.Remove(path) ? Task.CompletedTask : throw new RemoteServerException(RemoteError.NotFound, "No such file.");
    }

    public Task DeleteFolderAsync(string path, CancellationToken cancellation) => DeleteFileAsync(path, cancellation);

    public Task CreateFolderAsync(string path, CancellationToken cancellation)
    {
        Fail();
        AddFolder(path);
        return Task.CompletedTask;
    }

    public void Dispose() => IsConnected = false;

    private static string Parent(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];

    private static RemoteEntry Entry(string path, (bool IsDirectory, byte[] Content, DateTime Time) item) =>
        new(path[(path.LastIndexOf('/') + 1)..], item.IsDirectory, item.IsDirectory ? 0 : item.Content.Length, item.Time);

    private void Fail()
    {
        if (Failure is { } error)
        {
            throw new RemoteServerException(error, "Failed on purpose.");
        }
    }
}

/// <summary>Connections that use one <see cref="FakeRemoteServer"/>.</summary>
public sealed class FakeConnections(FakeRemoteServer server) : IServerConnections
{
    public bool IsDisposed { get; private set; }

    public int TriedAgain { get; private set; }

    public ConnectionState State { get; } = new(TimeProvider.System);

    /// <summary>When set, downloads use it instead of the fake server.</summary>
    public IRemoteServer? Transfer { get; init; }

    public Task<T> UseAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation) => work(server, cancellation);

    public Task<T> UseForTransferAsync<T>(Func<IRemoteServer, CancellationToken, Task<T>> work, CancellationToken cancellation) => work(Transfer ?? server, cancellation);

    public void TryAgainNow() => TriedAgain++;

    public void Dispose() => IsDisposed = true;
}

/// <summary>A folder on the PC in memory, with the placeholders made in it.</summary>
public sealed class FakePlaceholderSystem : IPlaceholderSystem
{
    public Dictionary<string, (bool IsDirectory, PlaceholderState? State)> Local { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Marked { get; } = [];

    public List<(string Path, PlaceholderUpdate Update)> Updates { get; } = [];

    public List<string> Deleted { get; } = [];

    public List<string> Recycled { get; } = [];

    /// <summary>The content of files on the PC.</summary>
    public Dictionary<string, string> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files a program has open.</summary>
    public HashSet<string> InUse { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Excluded { get; } = [];

    /// <summary>A file a program opens while it's read for an upload.</summary>
    public string? OpenedDuringUpload { get; set; }

    /// <summary>A file a program opens after its upload, before it's marked in sync.</summary>
    public string? OpenedBeforeMark { get; set; }

    /// <summary>A file a program opens after its upload, before it's renamed as a conflict copy.</summary>
    public string? OpenedBeforeRename { get; set; }

    /// <summary>Renames a file, as Windows does; fails when the name is taken.</summary>
    public int Move(string path, string newPath)
    {
        if (Local.ContainsKey(newPath))
        {
            return unchecked((int)0x800700B7);
        }

        Local[newPath] = Local[path];
        Local.Remove(path);
        if (Contents.Remove(path, out var content))
        {
            Contents[newPath] = content;
        }

        return 0;
    }

    public int UploadAttempts { get; private set; }

    /// <summary>The folders Windows was told to list again.</summary>
    public List<string> ListedAgain { get; } = [];

    public int ListAgainOnNextOpen(string folder)
    {
        ListedAgain.Add(folder);
        return 0;
    }

    public (Action<string> Changed, Action Missed)? Watched { get; private set; }

    public void AddNewFile(string path, string content)
    {
        Local[path] = (false, null);
        Contents[path] = content;
    }

    public void AddNewFolder(string path) => Local[path] = (true, null);

    public LocalItem? Inspect(string path) =>
        Local.TryGetValue(path, out var item)
            ? new LocalItem(Path.GetFileName(path), path, item.IsDirectory, item.State, item.State is { } state ? ItemIdentity.FromBytes([.. state.Identity]) : null) { IsLink = Links.Contains(path) }
            : null;

    public int OpenForUpload(string path, out IUploadSource? file)
    {
        UploadAttempts++;
        file = null;
        if (InUse.Contains(path))
        {
            return unchecked((int)0x80070020);
        }

        file = new FakeUploadSource(this, path, System.Text.Encoding.UTF8.GetBytes(Contents[path]));
        return 0;
    }

    public int ConvertFolder(string path, byte[] identity)
    {
        Local[path] = (true, State(identity, inSync: true, onlineOnly: false));
        return 0;
    }

    public int Exclude(string path)
    {
        Excluded.Add(path);
        return 0;
    }

    public IEnumerable<string> FindChanges(string folder) =>
        [.. Local.Where(pair => pair.Key.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase) && !Links.Contains(pair.Key)
            && (pair.Value.State is null || (!pair.Value.IsDirectory && !pair.Value.State.InSync))).Select(pair => pair.Key)];

    public IDisposable Watch(string folder, Action<string> changed, Action missed)
    {
        Watched = (changed, missed);
        return new MemoryStream();
    }

    /// <summary>Marks a file as uploaded, as Windows does through the upload's handle.</summary>
    public int MarkUploaded(string path, byte[] identity)
    {
        if (OpenedBeforeMark == path)
        {
            return UploadFile.Closed;
        }

        Local[path] = (false, State(identity, inSync: true, onlineOnly: false));
        return 0;
    }

    public int Create(string folder, IReadOnlyList<PlaceholderEntry> entries, Span<int> results)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            string path = Path.Join(folder, entries[i].Name);
            if (Local.ContainsKey(path))
            {
                results[i] = unchecked((int)0x800700B7);
                continue;
            }

            Local[path] = (entries[i].IsDirectory, State(entries[i].Identity, inSync: true, onlineOnly: !entries[i].IsDirectory));
            results[i] = 0;
        }

        return 0;
    }

    /// <summary>Records what Windows made from a listing's answer.</summary>
    public void Created(string folder, IReadOnlyList<PlaceholderEntry> entries) => Create(folder, entries, new int[entries.Count]);

    public void AddPlaceholder(string path, ItemIdentity identity, bool inSync = true, bool onlineOnly = true) =>
        Local[path] = (identity.IsDirectory, State(identity.ToBytes(), inSync, onlineOnly && !identity.IsDirectory));

    public void AddPlainFile(string path) => Local[path] = (false, null);

    public static PlaceholderState NotInSync(PlaceholderState state) => state with { InSync = false };

    public IReadOnlyList<LocalItem> Items(string folder) =>
        [.. Local.Where(pair => string.Equals(Path.GetDirectoryName(pair.Key), folder, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new LocalItem(Path.GetFileName(pair.Key), pair.Key, pair.Value.IsDirectory, pair.Value.State, pair.Value.State is { } state ? ItemIdentity.FromBytes([.. state.Identity]) : null))];

    public PlaceholderState? Read(string path) => Local.TryGetValue(path, out var item) ? item.State : null;

    public int Update(string path, PlaceholderEntry metadata, byte[] identity, PlaceholderUpdate update)
    {
        Updates.Add((path, update));
        if (update.HasFlag(PlaceholderUpdate.VerifyInSync) && Local[path].State is not { InSync: true })
        {
            return unchecked((int)0x80070179);
        }

        Local[path] = (false, State(identity, inSync: true, onlineOnly: update.HasFlag(PlaceholderUpdate.Dehydrate)));
        return 0;
    }

    public void MarkFromInternet(string path) => Marked.Add(path);

    public void Delete(string path)
    {
        Deleted.Add(path);
        Local.Remove(path);
    }

    public RecycleResult Recycle(string path)
    {
        Recycled.Add(path);
        foreach (var key in Local.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Local.Remove(key);
        }

        return new RecycleResult(RecycleOutcome.Recycled);
    }

    public bool IsAllInSync(string folder) =>
        Local.Where(pair => pair.Key.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)).All(pair => pair.Value.State is { InSync: true });

    private static PlaceholderState State(byte[] identity, bool inSync, bool onlineOnly) =>
        new(inSync, PinState.Unspecified, onlineOnly ? 0 : 1, inSync ? 0 : 1, identity, onlineOnly ? (FileAttributes)0x00401620 : FileAttributes.Archive | FileAttributes.ReparsePoint);
}

/// <summary>A listing request that records its answer.</summary>
public sealed class FakeListingRequest(byte[] identity) : IListingRequest
{
    public IReadOnlyList<byte> Identity => identity;

    public bool IsFromProvider { get; init; }

    /// <summary>Whether the answer told Windows the folder is complete.</summary>
    public bool? Complete { get; private set; }

    public IReadOnlyList<PlaceholderEntry>? Answered { get; private set; }

    public int? Refused { get; private set; }

    public int Answer(IReadOnlyList<PlaceholderEntry> entries, bool complete, Span<int> results)
    {
        Answered = entries;
        Complete = complete;
        results.Clear();
        return 0;
    }

    public int Refuse(int status)
    {
        Refused = status;
        return 0;
    }
}

/// <summary>Sync roots in memory.</summary>
public sealed class FakeSyncRootSystem : ISyncRootSystem
{
    public Dictionary<string, SyncRootRegistration> Registered { get; } = new(StringComparer.Ordinal);

    public List<string> Connected { get; } = [];

    public List<string> Unregistered { get; } = [];

    public Func<SyncRootRegistration, Exception?> Refusal { get; set; } = _ => null;

    public void Register(SyncRootRegistration registration)
    {
        if (Refusal(registration) is { } exception)
        {
            throw exception;
        }

        Registered[registration.Id] = registration;
    }

    public void Unregister(string id)
    {
        Unregistered.Add(id);
        Registered.Remove(id);
    }

    public IReadOnlyList<(string Id, string Folder)> Current(string idPrefix) =>
        [.. Registered.Values.Where(root => root.Id.StartsWith(idPrefix, StringComparison.Ordinal)).Select(root => (root.Id, root.Folder))];

    public IDisposable Connect(string folder, ICloudFileRequests requests, Action<Exception> failed)
    {
        Connected.Add(folder);
        return new Disconnect(() => Connected.Remove(folder));
    }

    private sealed class Disconnect(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>A file on the PC opened for an upload.</summary>
public sealed class FakeUploadSource(FakePlaceholderSystem pc, string path, byte[] content) : IUploadSource
{
    private string _path = path;

    public long Length => content.Length;

    public bool IsClosed { get; private set; }

    public Stream OpenRead() => pc.OpenedDuringUpload == _path ? new ClosingStream(this) : new MemoryStream(content, writable: false);

    public int MarkInSync(byte[] identity, bool isPlaceholder)
    {
        int result = IsClosed ? UploadFile.Closed : pc.MarkUploaded(_path, identity);
        IsClosed |= result == UploadFile.Closed;
        return result;
    }

    public int Rename(string newPath)
    {
        int result = IsClosed || pc.OpenedBeforeRename == _path ? UploadFile.Closed : pc.Move(_path, newPath);
        IsClosed |= result == UploadFile.Closed;
        if (result == 0)
        {
            _path = newPath;
        }

        return result;
    }

    public void Dispose()
    {
    }

    // A program opens the file: Windows closes the upload's handle.
    private sealed class ClosingStream(FakeUploadSource file) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => file.Length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            file.IsClosed = true;
            throw new IOException("A program opened the file.", UploadFile.Closed);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
