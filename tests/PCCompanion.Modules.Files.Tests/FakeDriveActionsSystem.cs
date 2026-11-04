using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>
/// Drives and files in memory, for Drive Actions' runner: no drive is read, ejected or written.
/// Paths are compared ignoring case, as Windows does.
/// </summary>
internal sealed class FakeDriveActionsSystem : IDriveActionsSystem
{
    public static readonly DateTimeOffset Taken = new(2026, 10, 30, 14, 5, 0, TimeSpan.Zero);

    private readonly Dictionary<string, FakeFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, DriveReading> _drives = [];
    private readonly Dictionary<char, long> _free = [];

    public bool IsUacElevated { get; set; }

    public uint Letters => _drives.Keys.Aggregate(0u, (mask, letter) => mask | (1u << (letter - 'A')));

    /// <summary>Copies made so far, as source and destination.</summary>
    public List<(string Source, string Destination)> Copies { get; } = [];

    /// <summary>Called before each copy, as a drive pulled out meanwhile would.</summary>
    public Action<string, string>? BeforeCopy { get; set; }

    public List<char> Ejected { get; } = [];

    /// <summary>What <see cref="Eject"/> answers.</summary>
    public EjectResult EjectAnswer { get; set; } = new(Ejected: true, Vetoed: false, 0);

    public void EnterBackgroundMode()
    {
    }

    public void AddDrive(char letter, string label = "BACKUP", string fileSystem = "NTFS", uint serial = 0xA8D4F2F8, string? hardwareSerial = "0LVF0HQ5", bool locked = false, int bus = 7, bool removable = true)
    {
        _drives[letter] = new DriveReading(
            $@"\\?\Volume{{{letter}}}\",
            removable,
            !removable,
            locked,
            label,
            fileSystem,
            serial,
            fileSystem == "NTFS",
            ReadOnly: false,
            Size: 64L << 30,
            Free: 32L << 30,
            bus,
            hardwareSerial,
            Ejectable: true,
            IsWindowsDrive: false);
        _folders.Add($"{letter}:\\");
    }

    public void RemoveDrive(char letter)
    {
        _drives.Remove(letter);
        var root = $"{letter}:\\";
        foreach (var path in _files.Keys.Where(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _files.Remove(path);
        }

        _folders.RemoveWhere(folder => folder.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    public void SetLocked(char letter, bool locked) => _drives[letter] = _drives[letter] with { IsLocked = locked };

    public void SetFree(string root, long free) => _free[char.ToUpperInvariant(root[0])] = free;

    public void AddFolder(string path)
    {
        for (var folder = path; folder is not null && folder.Length > 3; folder = Path.GetDirectoryName(folder))
        {
            _folders.Add(Path.TrimEndingDirectorySeparator(folder));
        }
    }

    public void AddFile(string path, long size = 1000, DateTimeOffset? modified = null, FileAttributes attributes = FileAttributes.Archive, bool marked = false, string? text = null)
    {
        AddFolder(Path.GetDirectoryName(path)!);
        _files[path] = new FakeFile(size, modified ?? Taken, attributes, marked, text);
    }

    public bool Exists(string path) => _files.ContainsKey(path);

    public FakeFile? FileAt(string path) => _files.GetValueOrDefault(path);

    public IEnumerable<string> FilesIn(string folder) =>
        _files.Keys.Where(path => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + "\\", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drives Windows still has that can't be read now, as a locked drive while Windows tries to mount it.</summary>
    public HashSet<char> Unreadable { get; } = [];

    public DriveReading? ReadDrive(char letter) => Unreadable.Contains(letter) ? null : _drives.GetValueOrDefault(letter);

    public string? ReadIdFile(string root) => _files.GetValueOrDefault(Path.Join(root, DriveIdentity.IdFileName))?.Text;

    public FileDetails? Read(string path)
    {
        var trimmed = path.Length > 3 ? Path.TrimEndingDirectorySeparator(path) : path;
        if (_files.TryGetValue(trimmed, out var file))
        {
            return file.Details(Path.GetFileName(trimmed));
        }

        return _folders.Contains(trimmed) ? new FileDetails(Path.GetFileName(trimmed), FileAttributes.Directory, 0, 0, Taken, Taken, Taken, null) : null;
    }

    public IReadOnlyList<ListedFile> List(string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(folder) + "\\";
        return [.. _files
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new ListedFile(pair.Key[prefix.Length..], pair.Value.Details(Path.GetFileName(pair.Key))))];
    }

    public int CreateFolder(string path)
    {
        _folders.Add(Path.TrimEndingDirectorySeparator(path));
        return 0;
    }

    public int Copy(string source, string destination, CancellationToken cancel)
    {
        BeforeCopy?.Invoke(source, destination);
        if (cancel.IsCancellationRequested)
        {
            return 1235;
        }

        if (!_files.TryGetValue(source, out var file))
        {
            return 2;
        }

        if (!_folders.Contains(Path.GetDirectoryName(destination)!) && !(Path.GetDirectoryName(destination)!.Length == 3))
        {
            return 3;
        }

        if (_files.ContainsKey(destination))
        {
            return 80;
        }

        _files[destination] = file with { Attributes = file.Attributes & ~FileAttributes.ReadOnly };
        Copies.Add((source, destination));
        return 0;
    }

    public int Rename(string source, string destination)
    {
        if (_files.ContainsKey(destination))
        {
            return 183;
        }

        if (!_files.Remove(source, out var file))
        {
            return 2;
        }

        _files[destination] = file;
        return 0;
    }

    public int Replace(string newCopy, string file)
    {
        if (_files.TryGetValue(file, out var old) && old.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            return 5;
        }

        if (!_files.Remove(newCopy, out var copy))
        {
            return 2;
        }

        _files[file] = copy;
        return 0;
    }

    public int Delete(string path) => _files.Remove(path) ? 0 : 2;

    public bool HasMark(string path) => _files.TryGetValue(path, out var file) && file.Marked;

    public long FreeSpace(string folder) => _free.TryGetValue(char.ToUpperInvariant(folder[0]), out var free) ? free : 100L << 30;

    public EjectResult Eject(char letter)
    {
        Ejected.Add(letter);
        return EjectAnswer;
    }

    public IDriveActionsDevices WatchDevices() => throw new NotSupportedException("The runner's tests have no host UI thread.");

    internal sealed record FakeFile(long Size, DateTimeOffset Modified, FileAttributes Attributes, bool Marked, string? Text)
    {
        public FileDetails Details(string name) => new(name, Attributes, 0, Size, Modified, Modified, Modified, null);
    }
}

/// <summary>
/// What the runner asks of the host, recorded: notifications, watches, folders to open and the
/// state; the work it schedules runs only when the test asks.
/// </summary>
internal sealed class FakeDriveActionsHost : IDriveActionsHost
{
    private readonly Queue<Action> _later = new();

    public List<(string Title, string Text, Action? Selected)> Notifications { get; } = [];

    public HashSet<char> Watched { get; } = [];

    public List<string> Opened { get; } = [];

    public DriveActionsState State { get; private set; } = DriveActionsState.Empty;

    public void Notify(string title, string text, Action? selected) => Notifications.Add((title, text, selected));

    public void Watch(char letter) => Watched.Add(letter);

    public void Unwatch(char letter) => Watched.Remove(letter);

    public void OpenFolder(string path) => Opened.Add(path);

    public void Publish(DriveActionsState state) => State = state;

    public void Later(Action work) => _later.Enqueue(work);

    /// <summary>Runs the scheduled work, and what it schedules, until there's none.</summary>
    public void RunAll()
    {
        for (var steps = 0; _later.TryDequeue(out var work); steps++)
        {
            if (steps > 100_000)
            {
                throw new InvalidOperationException("The runner doesn't stop.");
            }

            work();
        }
    }

    /// <summary>Runs one piece of scheduled work.</summary>
    public bool RunOne()
    {
        if (!_later.TryDequeue(out var work))
        {
            return false;
        }

        work();
        return true;
    }
}
