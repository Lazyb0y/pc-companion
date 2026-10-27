using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>
/// A file system in memory, with paths compared ignoring case as Windows does, which moves and
/// copies as Windows does: never over a file that's there, and only into a folder that exists.
/// The feature's thread and the test use it at the same time.
/// </summary>
internal sealed class FakeFolderRulesSystem : RecordingFake<FakeFolderRulesSystem>, IFolderRulesSystem
{
    private const int AccessDenied = 5;
    private const int SharingViolation = 32;
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;

    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Action<string> Changed, Action<bool> Failed)> _watches = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 1;

    public static DateTimeOffset Time { get; } = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Whether files get IDs, as on NTFS, or not, as on FAT.</summary>
    public bool FileIds { get; set; } = true;

    public bool IsUacElevated { get; set; }

    /// <summary>The moves and copies asked for so far, as "move a -> b" or "copy a -> b".</summary>
    public IReadOnlyList<string> Transfers => [.. Events.Where(happened => happened.StartsWith("move ", StringComparison.Ordinal) || happened.StartsWith("copy ", StringComparison.Ordinal))];

    public FakeFolderRulesSystem AddFolder(string path)
    {
        lock (Gate)
        {
            foreach (var folder in Ancestors(path).Append(path))
            {
                _items.TryAdd(folder, new Item(0, Time, Time, Time, NextId(), FileAttributes.Directory, null));
            }

            Changed();
        }

        return this;
    }

    public FakeFolderRulesSystem AddFile(
        string path, long size = 100, DateTimeOffset? modified = null, DateTimeOffset? created = null, string? mark = null, FileAttributes attributes = FileAttributes.Archive)
    {
        lock (Gate)
        {
            AddFolder(Path.GetDirectoryName(path)!);
            var time = created ?? Time;
            _items[path] = new Item(size, time, modified ?? time, time, NextId(), attributes, mark);
            Changed();
        }

        return this;
    }

    public FakeFolderRulesSystem AddLink(string path)
    {
        lock (Gate)
        {
            AddFolder(Path.GetDirectoryName(path)!);
            _items[path] = new Item(0, Time, Time, Time, NextId(), FileAttributes.Directory | FileAttributes.ReparsePoint, null, 0xA0000003);
        }

        return this;
    }

    /// <summary>Adds a file and reports it to the folder's watch, as Windows reports one that arrives.</summary>
    public void Arrive(string path, long size = 100, DateTimeOffset? created = null, FileAttributes attributes = FileAttributes.Archive)
    {
        AddFile(path, size, created: created, attributes: attributes);
        Report(path);
    }

    /// <summary>Reports a change to a file, as Windows does, to its folder's watch.</summary>
    public void Report(string path)
    {
        Action<string>? changed;
        lock (Gate)
        {
            changed = _watches.TryGetValue(Path.GetDirectoryName(path)!, out var watch) ? watch.Changed : null;
        }

        changed?.Invoke(path);
    }

    /// <summary>Ends a folder's watch with an error, as Windows does when changes were lost.</summary>
    public void LoseChanges(string folder)
    {
        Action<bool>? failed;
        lock (Gate)
        {
            failed = _watches.TryGetValue(folder, out var watch) ? watch.Failed : null;
        }

        failed?.Invoke(true);
    }

    public void SetBusy(string path, bool busy)
    {
        lock (Gate)
        {
            _ = busy ? _busy.Add(path) : _busy.Remove(path);
        }
    }

    public void Deny(string folder)
    {
        lock (Gate)
        {
            _denied.Add(folder);
        }
    }

    public bool IsWatching(string folder)
    {
        lock (Gate)
        {
            return _watches.ContainsKey(folder);
        }
    }

    public bool Exists(string path)
    {
        lock (Gate)
        {
            return _items.ContainsKey(path);
        }
    }

    public void EnterBackgroundMode() => Record("background");

    public FileDetails? Read(string path)
    {
        lock (Gate)
        {
            return _items.TryGetValue(path, out var item) ? Details(path, item) : null;
        }
    }

    public IReadOnlyList<FileDetails> List(string folder)
    {
        lock (Gate)
        {
            Record($"list {folder}");
            if (!_items.ContainsKey(folder))
            {
                throw new System.ComponentModel.Win32Exception(PathNotFound);
            }

            return [.. _items.Where(pair => string.Equals(Path.GetDirectoryName(pair.Key), folder, StringComparison.OrdinalIgnoreCase)).Select(pair => Details(pair.Key, pair.Value))];
        }
    }

    public bool CanOpenAlone(string path)
    {
        lock (Gate)
        {
            return !_busy.Contains(path);
        }
    }

    public MarkOfTheWeb? ReadMark(string path)
    {
        lock (Gate)
        {
            return _items.TryGetValue(path, out var item) && item.Mark is { } mark ? MarkOfTheWeb.Parse(mark) : null;
        }
    }

    public int CreateFolder(string path)
    {
        lock (Gate)
        {
            var parent = Path.GetDirectoryName(path)!;
            if (!_items.ContainsKey(parent))
            {
                return PathNotFound;
            }

            if (_denied.Contains(parent))
            {
                return AccessDenied;
            }

            AddFolder(path);
            return 0;
        }
    }

    public int Move(string source, string destination, CancellationToken cancel)
    {
        lock (Gate)
        {
            Record($"move {source} -> {destination}");
            var error = Check(source, destination);
            if (error != 0 && !(error == FileTransfer.AlreadyExists && string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)))
            {
                return error;
            }

            var item = _items[source];
            _items.Remove(source);
            _items[destination] = item;
            Changed();
            return 0;
        }
    }

    public int Copy(string source, string destination, CancellationToken cancel)
    {
        lock (Gate)
        {
            Record($"copy {source} -> {destination}");
            var error = Check(source, destination);
            if (error == FileTransfer.AlreadyExists)
            {
                return FileTransfer.FileExists;
            }

            if (error != 0)
            {
                return error;
            }

            _items[destination] = _items[source] with { Id = NextId() };
            Changed();
            return 0;
        }
    }

    public IDisposable Watch(string folder, Action<string> changed, Action<bool> failed)
    {
        lock (Gate)
        {
            if (!_items.ContainsKey(folder))
            {
                throw new ArgumentException("The folder doesn't exist.", nameof(folder));
            }

            _watches[folder] = (changed, failed);
            Record($"watch {folder}");
            return new Unwatch(this, folder);
        }
    }

    private int Check(string source, string destination)
    {
        if (_busy.Contains(source))
        {
            return SharingViolation;
        }

        if (!_items.ContainsKey(source))
        {
            return FileNotFound;
        }

        var folder = Path.GetDirectoryName(destination)!;
        if (!_items.ContainsKey(folder))
        {
            return PathNotFound;
        }

        if (_denied.Contains(folder))
        {
            return AccessDenied;
        }

        return _items.ContainsKey(destination) ? FileTransfer.AlreadyExists : 0;
    }

    private static List<string> Ancestors(string path)
    {
        var parents = new List<string>();
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            parents.Insert(0, parent);
        }

        return parents;
    }

    private long NextId() => _nextId++;

    private FileDetails Details(string path, Item item) =>
        new(Path.GetFileName(path), item.Attributes, item.Tag, item.Size, item.Created, item.Modified, item.Changed, FileIds ? $"0001-{item.Id:X32}" : null);

    private sealed record Item(long Size, DateTimeOffset Created, DateTimeOffset Modified, DateTimeOffset Changed, long Id, FileAttributes Attributes, string? Mark, uint Tag = 0);

    private sealed class Unwatch(FakeFolderRulesSystem system, string folder) : IDisposable
    {
        public void Dispose()
        {
            lock (system.Gate)
            {
                system._watches.Remove(folder);
                system.Record($"unwatch {folder}");
            }
        }
    }
}
