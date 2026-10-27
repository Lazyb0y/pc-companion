using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>
/// A file system in memory, with paths compared ignoring case as Windows does, which moves and
/// copies as Windows does: never over a file that's there, and only into a folder that exists.
/// </summary>
internal sealed class FakeFolderRulesSystem : IFolderRulesSystem
{
    private const int AccessDenied = 5;
    private const int SharingViolation = 32;
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;

    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 1;

    public static DateTimeOffset Time { get; } = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Files another program has open for writing.</summary>
    public HashSet<string> Busy { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders where Windows refuses to create or move anything.</summary>
    public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether files get IDs, as on NTFS, or not, as on FAT.</summary>
    public bool FileIds { get; set; } = true;

    /// <summary>Moves and copies asked for so far, as "move a -> b" or "copy a -> b".</summary>
    public List<string> Transfers { get; } = [];

    public FakeFolderRulesSystem AddFolder(string path)
    {
        foreach (var folder in Ancestors(path).Append(path))
        {
            _items.TryAdd(folder, new Item(true, 0, Time, Time, NextId(), FileAttributes.Directory, null));
        }

        return this;
    }

    public FakeFolderRulesSystem AddFile(string path, long size = 100, DateTimeOffset? modified = null, string? mark = null, FileAttributes attributes = FileAttributes.Archive)
    {
        AddFolder(Path.GetDirectoryName(path)!);
        _items[path] = new Item(false, size, Time, modified ?? Time, NextId(), attributes, mark);
        return this;
    }

    public FakeFolderRulesSystem AddLink(string path)
    {
        AddFolder(Path.GetDirectoryName(path)!);
        _items[path] = new Item(true, 0, Time, Time, NextId(), FileAttributes.Directory | FileAttributes.ReparsePoint, null, 0xA0000003);
        return this;
    }

    public bool Exists(string path) => _items.ContainsKey(path);

    public string? MarkOf(string path) => _items.TryGetValue(path, out var item) ? item.Mark : null;

    public FileDetails? Read(string path) => _items.TryGetValue(path, out var item) ? Details(path, item) : null;

    public IReadOnlyList<FileDetails> List(string folder) =>
        [.. _items.Where(pair => string.Equals(Path.GetDirectoryName(pair.Key), folder, StringComparison.OrdinalIgnoreCase)).Select(pair => Details(pair.Key, pair.Value))];

    public bool CanOpenAlone(string path) => !Busy.Contains(path);

    public MarkOfTheWeb? ReadMark(string path) => MarkOf(path) is { } mark ? MarkOfTheWeb.Parse(mark) : null;

    public int CreateFolder(string path)
    {
        if (!_items.ContainsKey(Path.GetDirectoryName(path)!))
        {
            return PathNotFound;
        }

        if (Denied.Contains(Path.GetDirectoryName(path)!))
        {
            return AccessDenied;
        }

        AddFolder(path);
        return 0;
    }

    public int Move(string source, string destination, CancellationToken cancel)
    {
        Transfers.Add($"move {source} -> {destination}");
        var error = Check(source, destination);
        if (error != 0 && !(error == FileTransfer.AlreadyExists && string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)))
        {
            return error;
        }

        var item = _items[source];
        _items.Remove(source);
        _items[destination] = item;
        return 0;
    }

    public int Copy(string source, string destination, CancellationToken cancel)
    {
        Transfers.Add($"copy {source} -> {destination}");
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
        return 0;
    }

    private int Check(string source, string destination)
    {
        if (Busy.Contains(source))
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

        if (Denied.Contains(folder))
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
        new(Path.GetFileName(path), item.Attributes, item.Tag, item.Size, item.Created, item.Modified, item.Created, FileIds ? $"0001-{item.Id:X32}" : null);

    private sealed record Item(bool IsFolder, long Size, DateTimeOffset Created, DateTimeOffset Modified, long Id, FileAttributes Attributes, string? Mark, uint Tag = 0);
}
