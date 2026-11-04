using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>A file an import copied, as PC Companion recognizes it the next time.</summary>
public sealed record ImportedFile(string Name, long Size, DateTimeOffset Modified);

/// <summary>A copy a backup made on a drive: the PC file as it was then, and the copy as it was left.</summary>
public sealed record BackupCopy(long SourceSize, DateTimeOffset SourceModified, long CopySize, DateTimeOffset CopyModified);

/// <summary>
/// Compares files' last modified times as a drive keeps them: FAT and exFAT drives keep them in
/// steps of 2 seconds, and in local time, which Windows reads an hour apart after a change to or
/// from daylight saving time.
/// </summary>
public static class FileTimes
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>Whether <paramref name="first"/> and <paramref name="second"/> are the same time, as a drive may have kept it.</summary>
    public static bool AreSame(DateTimeOffset first, DateTimeOffset second)
    {
        var difference = (first - second).Duration();
        return difference <= Step || (difference - Hour).Duration() <= Step;
    }
}

/// <summary>
/// What PC Companion recorded about one drive in the list, or Camera cards: the files its imports
/// copied, and the copies its backups made. Kept until the drive is removed from the list.
/// </summary>
public sealed class DriveRecords
{
    /// <summary>The most imported files remembered for a drive; the oldest are forgotten first.</summary>
    public const int MaximumImportedFiles = 100_000;

    private readonly List<ImportedFile> _imported = [];
    private readonly Dictionary<(string Name, long Size), List<DateTimeOffset>> _importedByName = new(new NameAndSizeComparer());
    private readonly Dictionary<string, Dictionary<string, BackupCopy>> _copies = new(StringComparer.OrdinalIgnoreCase);

    public DriveRecords()
    {
    }

    internal DriveRecords(IEnumerable<ImportedFile> imported, IReadOnlyDictionary<string, IReadOnlyDictionary<string, BackupCopy>> copies)
    {
        foreach (var file in imported)
        {
            AddImported(file);
        }

        foreach (var (folder, files) in copies)
        {
            foreach (var (path, copy) in files)
            {
                SetCopy(folder, path, copy);
            }
        }
    }

    /// <summary>The files imported, oldest first.</summary>
    public IReadOnlyList<ImportedFile> Imported => _imported;

    /// <summary>The copies made, by the folder of the drive's destination they're in, then by their path in it.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, BackupCopy>> Copies =>
        _copies.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, BackupCopy>)pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a file with this name and size was imported before, with a last modified time within
    /// an hour, ignoring case. See "Import from the drive" in docs/features/files/drive-actions.md.
    /// </summary>
    public bool WasImported(string name, long size, DateTimeOffset modified) =>
        _importedByName.TryGetValue((name, size), out var times) && times.Any(time => (time - modified).Duration() <= TimeSpan.FromHours(1) + TimeSpan.FromSeconds(2));

    /// <summary>Remembers an imported file, forgetting the oldest beyond <see cref="MaximumImportedFiles"/>.</summary>
    public void AddImported(ImportedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _imported.Add(file);
        var key = (file.Name, file.Size);
        if (!_importedByName.TryGetValue(key, out var times))
        {
            _importedByName[key] = times = [];
        }

        times.Add(file.Modified);
        while (_imported.Count > MaximumImportedFiles)
        {
            var oldest = _imported[0];
            _imported.RemoveAt(0);
            var oldestTimes = _importedByName[(oldest.Name, oldest.Size)];
            oldestTimes.Remove(oldest.Modified);
            if (oldestTimes.Count == 0)
            {
                _importedByName.Remove((oldest.Name, oldest.Size));
            }
        }
    }

    /// <summary>The copy of <paramref name="path"/> in the destination's <paramref name="folder"/>, or <see langword="null"/>.</summary>
    public BackupCopy? CopyOf(string folder, string path) =>
        _copies.TryGetValue(folder, out var files) && files.TryGetValue(path, out var copy) ? copy : null;

    /// <summary>Records the copy of <paramref name="path"/> in the destination's <paramref name="folder"/>.</summary>
    public void SetCopy(string folder, string path, BackupCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (!_copies.TryGetValue(folder, out var files))
        {
            _copies[folder] = files = new Dictionary<string, BackupCopy>(StringComparer.OrdinalIgnoreCase);
        }

        files[path] = copy;
    }

    /// <summary>Forgets the copy of <paramref name="path"/>, as when it's gone from the drive.</summary>
    public void RemoveCopy(string folder, string path)
    {
        if (_copies.TryGetValue(folder, out var files))
        {
            files.Remove(path);
        }
    }

    private sealed class NameAndSizeComparer : IEqualityComparer<(string Name, long Size)>
    {
        public bool Equals((string Name, long Size) x, (string Name, long Size) y) =>
            x.Size == y.Size && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, long Size) obj) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Size);
    }
}
