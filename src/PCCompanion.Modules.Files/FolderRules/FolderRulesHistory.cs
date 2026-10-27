using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>What a rule did with a file, as the history shows it.</summary>
public enum HistoryOutcome
{
    Moved,
    Copied,
    Renamed,
    MovedToRecycleBin,

    /// <summary>Windows asks whether to delete a file too big for the Recycle Bin, and waits for the user.</summary>
    WaitingForAnswer,

    /// <summary>The user answered Yes to Windows' question, and the file was deleted for good.</summary>
    DeletedForGood,

    /// <summary>The user answered No to Windows' question or closed it, and the file stayed.</summary>
    KeptByAnswer,

    /// <summary>The rule left the file where it is; <see cref="HistoryEntry.Reason"/> says why.</summary>
    Skipped,
}

/// <summary>Why a rule left a file where it is.</summary>
public enum SkipReason
{
    None,

    /// <summary>A file with the same name is there, and the rule skips.</summary>
    NameTaken,

    /// <summary>Another program kept the file open.</summary>
    InUse,

    /// <summary>The drive has no Recycle Bin, and PC Companion never deletes without it.</summary>
    NoRecycleBin,

    /// <summary>The destination's drive can't keep the file's Mark of the Web.</summary>
    MarkWouldBeLost,

    /// <summary>Windows blocked the change, as Controlled folder access does.</summary>
    Blocked,

    /// <summary>Windows refused the change; <see cref="HistoryEntry.ErrorCode"/> says how.</summary>
    Failed,

    /// <summary>The destination's folder doesn't exist.</summary>
    DestinationNotFound,

    /// <summary>The new name or a subfolder made from the file isn't one Windows allows; <see cref="HistoryEntry.Naming"/> says why.</summary>
    Naming,

    /// <summary>The file changed or went away before the rule acted.</summary>
    Changed,

    /// <summary>
    /// The move or copy stopped before it was done, as when Windows ejected its drive; the file
    /// stays and is tried again when the drive is back.
    /// </summary>
    Interrupted,
}

/// <summary>
/// Which file a history entry is about, so that the rules recognize it when it comes back: by its
/// file ID on drives that keep one, or else by its name, size and last modified time.
/// </summary>
/// <param name="FileId">The volume's serial number and the file's ID, or <see langword="null"/> where the drive has none.</param>
public sealed record FileIdentity(string? FileId, long Size, DateTimeOffset Modified)
{
    /// <summary>Whether two identities are of the same file, either named one of <paramref name="names"/>.</summary>
    public bool Matches(FileIdentity other, string name, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(names);

        return FileId is not null && other.FileId is not null
            ? string.Equals(FileId, other.FileId, StringComparison.OrdinalIgnoreCase)
            : Size == other.Size && Modified == other.Modified && names.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>An entry of the history: what a rule did with a file, or why it didn't.</summary>
public sealed record HistoryEntry
{
    /// <summary>The entry's number, which Undo and the settings app use.</summary>
    public long Id { get; set; }

    public DateTimeOffset Time { get; set; }

    /// <summary>The watched folder the file arrived in.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The rule's name.</summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>The file's name when it arrived.</summary>
    public string Name { get; set; } = string.Empty;

    public HistoryOutcome Outcome { get; set; }

    public SkipReason Reason { get; set; }

    /// <summary>For <see cref="SkipReason.Naming"/>: what's wrong with the name.</summary>
    public NamingProblem Naming { get; set; }

    /// <summary>For <see cref="SkipReason.Failed"/> and <see cref="SkipReason.Blocked"/>: Windows' error code.</summary>
    public int ErrorCode { get; set; }

    /// <summary>Where the file, or its copy, is now: for a move, a copy and a rename.</summary>
    public string NewPath { get; set; } = string.Empty;

    /// <summary>The file the rule acted on, as it was when the rule acted.</summary>
    public FileIdentity? File { get; set; }

    /// <summary>When Undo put the file back, or <see langword="null"/>.</summary>
    public DateTimeOffset? Undone { get; set; }

    /// <summary>
    /// Whether the rules acted on the file, so they leave it when it comes back: everything but a
    /// skip and the wait for Windows' answer.
    /// </summary>
    [JsonIgnore]
    public bool Acted => Outcome is not (HistoryOutcome.Skipped or HistoryOutcome.WaitingForAnswer);
}

/// <summary>
/// The history of what the rules did, newest first, which keeps the last
/// <see cref="KeptDays"/> days up to <see cref="MaximumEntries"/> entries, and when the rules
/// last looked at each folder. Only the host's feature thread uses it. See "History and undo" in
/// docs/features/files/folder-rules.md.
/// </summary>
public sealed class FolderRulesHistory
{
    public const int MaximumEntries = 1000;

    public const int KeptDays = 30;

    private readonly List<HistoryEntry> _entries;
    private readonly Dictionary<string, DateTimeOffset> _lastLooked;

    public FolderRulesHistory()
        : this([], new Dictionary<string, DateTimeOffset>())
    {
    }

    internal FolderRulesHistory(IEnumerable<HistoryEntry> entries, IReadOnlyDictionary<string, DateTimeOffset> lastLooked)
    {
        _entries = [.. entries.OrderByDescending(entry => entry.Id)];
        _lastLooked = new Dictionary<string, DateTimeOffset>(lastLooked, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The entries, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public IReadOnlyDictionary<string, DateTimeOffset> LastLooked => _lastLooked;

    /// <summary>Adds an entry with the next number, and returns it.</summary>
    public HistoryEntry Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var added = entry with { Id = (_entries.Count == 0 ? 0 : _entries[0].Id) + 1 };
        _entries.Insert(0, added);
        return added;
    }

    /// <summary>Replaces the entry with the same number, such as when Windows' question is answered.</summary>
    public void Replace(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var index = _entries.FindIndex(existing => existing.Id == entry.Id);
        if (index >= 0)
        {
            _entries[index] = entry;
        }
    }

    public HistoryEntry? Find(long id) => _entries.Find(entry => entry.Id == id);

    /// <summary>
    /// Whether the rules of <paramref name="folder"/> already acted on the file named
    /// <paramref name="name"/> with <paramref name="file"/>'s identity, such as a file they renamed
    /// or one the user put back, so they leave it.
    /// </summary>
    public bool WasHandled(string folder, string name, FileIdentity file)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(file);

        return _entries.Any(entry => entry.Acted && entry.File is { } handled
            && string.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase)
            && handled.Matches(file, name, [entry.Name, Path.GetFileName(entry.NewPath)]));
    }

    /// <summary>When the rules last looked at a folder, or <see langword="null"/> when never.</summary>
    public DateTimeOffset? LastLookedAt(string folder) => _lastLooked.TryGetValue(folder, out var time) ? time : null;

    /// <summary>Forgets when the rules last looked at a folder, which is no longer watched.</summary>
    public void ForgetFolder(string folder) => _lastLooked.Remove(folder);

    public void SetLastLooked(string folder, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(folder);

        _lastLooked[folder] = time;
    }

    /// <summary>
    /// Drops the entries older than <see cref="KeptDays"/> days and beyond <see cref="MaximumEntries"/>,
    /// and the folders that aren't in <paramref name="folders"/>.
    /// </summary>
    public void Prune(DateTimeOffset now, IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        _entries.RemoveAll(entry => now - entry.Time > TimeSpan.FromDays(KeptDays));
        if (_entries.Count > MaximumEntries)
        {
            _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
        }

        var kept = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _lastLooked.Keys.Where(folder => !kept.Contains(folder)).ToArray())
        {
            _lastLooked.Remove(folder);
        }
    }

    /// <summary>Empties the history; when the rules last looked at each folder stays.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Whether a file that's in a folder now arrived after the rules last looked at it: it was
    /// created, or moved or renamed, which changes its change time, since. A drive that keeps no
    /// change time gives 0. Nothing arrived in a folder the rules never looked at, so adding a
    /// folder doesn't act on the files already there.
    /// </summary>
    public static bool ArrivedSince(DateTimeOffset created, DateTimeOffset changed, DateTimeOffset? lastLooked) =>
        lastLooked is { } since && (created > since || changed > since);
}
