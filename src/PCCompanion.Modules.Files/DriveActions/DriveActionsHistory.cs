using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>How a run of a drive's actions ended.</summary>
public enum RunOutcome
{
    /// <summary>Its actions ran to the end; some files may have been skipped.</summary>
    Done,

    /// <summary>The user stopped it.</summary>
    Stopped,

    /// <summary>Windows ejected the drive while it ran.</summary>
    Ejected,

    /// <summary>The drive was pulled out, or went, while it ran.</summary>
    Removed,

    /// <summary>An import or a backup didn't fit on its destination's drive, so nothing more was copied.</summary>
    NotEnoughSpace,
}

/// <summary>What happened to a file in a run.</summary>
public enum FileOutcome
{
    /// <summary>An import copied it to the PC.</summary>
    Imported,

    /// <summary>A backup copied it to the drive.</summary>
    Copied,

    /// <summary>A backup replaced its older copy on the drive.</summary>
    Replaced,

    /// <summary>It stayed; <see cref="RunFile.Reason"/> says why.</summary>
    Skipped,
}

/// <summary>Why a run left a file.</summary>
public enum FileSkip
{
    None,

    /// <summary>A file with that name is there already, and the import skips such files, or ran out of numbers.</summary>
    NameTaken,

    /// <summary>Another program has it open.</summary>
    InUse,

    /// <summary>Windows blocked it, as Controlled folder access does in the folders it protects.</summary>
    Blocked,

    /// <summary>The destination folder can't be used, such as one that's a link.</summary>
    DestinationNotAllowed,

    /// <summary>The run stopped while it was copied.</summary>
    Interrupted,

    /// <summary>It changed or went away meanwhile.</summary>
    Changed,

    /// <summary>A file with a Mark of the Web, for a drive that can't keep it.</summary>
    MarkWouldBeLost,

    /// <summary>Too large for the drive's file system, such as 4 GB or more on FAT32.</summary>
    TooLarge,

    /// <summary>PC Companion's copy was changed on the drive.</summary>
    ChangedOnDrive,

    /// <summary>The drive has a file of that name PC Companion didn't put there.</summary>
    NotOurs,

    /// <summary>The copy to replace is read-only.</summary>
    ReadOnly,

    /// <summary>The backup's destination isn't on the recognized drive any more.</summary>
    WrongDrive,

    /// <summary>Windows refused, with <see cref="RunFile.ErrorCode"/>.</summary>
    Failed,
}

/// <summary>A file a run imported, copied or skipped.</summary>
public sealed record RunFile
{
    /// <summary>The file's path: in the folder on the drive for an import, in the backup's folder for a backup.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>For an imported file: its full path on the PC, which Undo checks and sends to the Recycle Bin.</summary>
    public string? NewPath { get; set; }

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }

    public FileOutcome Outcome { get; set; }

    public FileSkip Reason { get; set; }

    /// <summary>Windows' error code, for <see cref="FileSkip.Failed"/> and <see cref="FileSkip.Blocked"/>.</summary>
    public int ErrorCode { get; set; }

    /// <summary>Whether Undo put it back.</summary>
    public bool Undone { get; set; }
}

/// <summary>What one of a run's actions did.</summary>
public sealed record RunAction
{
    /// <summary>The action's name, as in the settings: <c>Import</c>, <c>BackUp</c> or <c>OpenFolder</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The files imported or copied, including replaced copies.</summary>
    public int Copied { get; set; }

    /// <summary>The files left, each for one of the reasons in <see cref="Files"/>.</summary>
    public int Skipped { get; set; }

    /// <summary>
    /// The files: for an import every file it copied or skipped, for a backup the files it skipped,
    /// up to <see cref="DriveActionsHistory.MaximumSkippedFiles"/>.
    /// </summary>
    public IReadOnlyList<RunFile> Files { get; set; } = [];

    /// <summary>For Open a folder: whether it opened.</summary>
    public bool Opened { get; set; }
}

/// <summary>A run of a drive's actions, in the history.</summary>
public sealed record RunEntry
{
    public long Id { get; set; }

    public DateTimeOffset Started { get; set; }

    public DateTimeOffset Ended { get; set; }

    /// <summary>The drive's name, as the list had it then.</summary>
    public string Drive { get; set; } = string.Empty;

    /// <summary>The key of the drive's records: its ID, or Camera cards'.</summary>
    public string DriveKey { get; set; } = string.Empty;

    public RunOutcome Outcome { get; set; }

    /// <summary>For <see cref="RunOutcome.NotEnoughSpace"/>: the space the copies needed and the space that was free.</summary>
    public long Needed { get; set; }

    public long Free { get; set; }

    public IReadOnlyList<RunAction> Actions { get; set; } = [];

    /// <summary>For Eject when done: whether Windows ejected the drive.</summary>
    public bool? Ejected { get; set; }
}

/// <summary>
/// The runs of the drives' actions, newest last, for the last 30 days, up to 1,000 runs. Use it on
/// the feature's thread.
/// </summary>
public sealed class DriveActionsHistory
{
    /// <summary>The most runs kept.</summary>
    public const int MaximumRuns = 1000;

    /// <summary>The most skipped files a backup's entry lists.</summary>
    public const int MaximumSkippedFiles = 100;

    /// <summary>How long runs are kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    private readonly List<RunEntry> _runs;

    public DriveActionsHistory()
        : this([])
    {
    }

    internal DriveActionsHistory(IEnumerable<RunEntry> runs)
    {
        _runs = [.. runs];
    }

    public IReadOnlyList<RunEntry> Runs => _runs;

    /// <summary>Adds a run with the next ID, and forgets the runs beyond the limits.</summary>
    public RunEntry Add(RunEntry run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        var added = run with { Id = _runs.Count == 0 ? 1 : _runs.Max(entry => entry.Id) + 1 };
        _runs.Add(added);
        _runs.RemoveAll(entry => now - entry.Started > KeptFor);
        if (_runs.Count > MaximumRuns)
        {
            _runs.RemoveRange(0, _runs.Count - MaximumRuns);
        }

        return added;
    }

    /// <summary>Puts <paramref name="run"/> in the place of the run with its ID.</summary>
    public void Replace(RunEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var index = _runs.FindIndex(entry => entry.Id == run.Id);
        if (index >= 0)
        {
            _runs[index] = run;
        }
    }

    public RunEntry? Find(long id) => _runs.Find(entry => entry.Id == id);

    public void Clear() => _runs.Clear();
}

/// <summary>
/// Reads and writes <c>drive-actions.json</c> in the data folder, which holds the history. Only the
/// host writes it. Any program running as the user can change it, so it's read as untrusted.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class DriveActionsHistoryStore(string path)
{
    /// <summary>The file's name in the data folder.</summary>
    public const string FileName = "drive-actions.json";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 16 * 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the saved history, or an empty one when there's none yet. When the file can't be
    /// read, <paramref name="readable"/> is <see langword="false"/> and the history is empty; the
    /// next save replaces the file.
    /// </summary>
    public DriveActionsHistory Load(out bool readable)
    {
        readable = true;
        try
        {
            using var stream = File.OpenRead(FilePath);
            if (stream.Length > MaximumFileSize)
            {
                readable = false;
                return new DriveActionsHistory();
            }

            var file = JsonSerializer.Deserialize(stream, DriveActionsHistoryJsonContext.Default.DriveActionsHistoryFile) ?? new DriveActionsHistoryFile();

            // The JSON reader checks nullable annotations on properties but not on a list's items.
            return new DriveActionsHistory(file.Runs.Where(run => run is not null).Select(WithoutNulls));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DriveActionsHistory();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            readable = false;
            return new DriveActionsHistory();
        }
    }

    /// <summary>
    /// Saves the history, to a temporary file first that then replaces the file, so a reader never
    /// sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(DriveActionsHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new DriveActionsHistoryFile { Runs = history.Runs }, DriveActionsHistoryJsonContext.Default.DriveActionsHistoryFile);
            stream.Flush(flushToDisk: true);
        }

        // The move fails while another process reads the file, which only takes a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryFile, FilePath, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceAttempts && exception is UnauthorizedAccessException or (IOException and not FileNotFoundException))
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    private static RunEntry WithoutNulls(RunEntry run) => run with
    {
        Actions = [.. run.Actions.Where(action => action is not null).Select(action => action with { Files = [.. action.Files.Where(file => file is not null)] })],
    };
}

/// <summary>The contents of <c>drive-actions.json</c>.</summary>
internal sealed record DriveActionsHistoryFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<RunEntry> Runs { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DriveActionsHistoryFile))]
internal sealed partial class DriveActionsHistoryJsonContext : JsonSerializerContext;
