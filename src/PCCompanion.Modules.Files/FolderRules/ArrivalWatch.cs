using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>A file's size and last modified time, which change while it's written.</summary>
public readonly record struct FileState(long Size, DateTimeOffset Modified);

/// <summary>Reads files while they arrive, on the feature's thread.</summary>
public interface IArrivalProbe
{
    /// <summary>The file's size and last modified time, or <see langword="null"/> when it's gone.</summary>
    FileState? Read(string path);

    /// <summary>
    /// Whether the file opens without letting other programs write to it, which fails while a
    /// program has it open for writing.
    /// </summary>
    bool CanOpenAlone(string path);
}

/// <summary>How a file's arrival ended.</summary>
public enum ArrivalOutcome
{
    /// <summary>The file has finished arriving, and the rules can act on it.</summary>
    Arrived,

    /// <summary>Another program kept the file open for writing for too long.</summary>
    InUse,
}

public sealed record Arrival(string Path, ArrivalOutcome Outcome);

/// <summary>
/// Tells when files have finished arriving: when neither their size nor their last modified
/// time has changed for <see cref="QuietTime"/>, a wait that each change starts again, and no
/// other program has them open for writing. It runs on the feature's thread, which calls
/// <see cref="Check"/> at <see cref="NextDue"/>. See "Waiting until a file has arrived" in
/// docs/features/files/folder-rules.md.
/// </summary>
public sealed class ArrivalWatch
{
    /// <summary>How long a file must stay the same.</summary>
    public static readonly TimeSpan QuietTime = TimeSpan.FromSeconds(2);

    /// <summary>The longest wait between tries to open a file that's in use.</summary>
    public static readonly TimeSpan LongestRetryWait = TimeSpan.FromSeconds(30);

    /// <summary>How long a file may stay in use before it's left where it is.</summary>
    public static readonly TimeSpan InUseTime = TimeSpan.FromMinutes(5);

    // The extensions of files that browsers and other programs are still writing, which they
    // rename when they're done.
    private static readonly string[] UnfinishedExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp"];

    private readonly IArrivalProbe _probe;
    private readonly Dictionary<string, Waiting> _files = new(StringComparer.OrdinalIgnoreCase);

    public ArrivalWatch(IArrivalProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        _probe = probe;
    }

    /// <summary>How many files are waiting.</summary>
    public int Count => _files.Count;

    /// <summary>When <see cref="Check"/> next has work, or <see langword="null"/> when no file waits.</summary>
    public DateTimeOffset? NextDue => _files.Count == 0 ? null : _files.Values.Min(file => file.Due);

    /// <summary>
    /// Whether a file is skipped by its name: one that a browser or another program is still
    /// writing, such as <c>report.pdf.crdownload</c>, or Office's <c>~$</c> lock file.
    /// </summary>
    public static bool IsSkippedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.StartsWith("~$", StringComparison.Ordinal)
            || UnfinishedExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Notes that a file arrived or changed, which starts its wait again. A file with a skipped
    /// name, or that's gone, isn't waited for.
    /// </summary>
    public void Notice(string path, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (IsSkippedName(Path.GetFileName(path)) || _probe.Read(path) is not { } state)
        {
            _files.Remove(path);
            return;
        }

        _files[path] = new Waiting(state, now + QuietTime);
    }

    /// <summary>Stops waiting for the files directly in <paramref name="folder"/>, such as when its watch stops.</summary>
    public void ForgetFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        foreach (var path in _files.Keys.Where(path => string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _files.Remove(path);
        }
    }

    /// <summary>
    /// Checks the files whose time has come, and returns those that have finished arriving or
    /// stayed in use too long. Files that are gone, or empty when they settle, are dropped: a
    /// program that writes one later brings a new change.
    /// </summary>
    public IReadOnlyList<Arrival> Check(DateTimeOffset now)
    {
        var results = new List<Arrival>();
        foreach (var (path, file) in _files.Where(pair => pair.Value.Due <= now).ToArray())
        {
            if (_probe.Read(path) is not { } state || (state == file.State && state.Size == 0))
            {
                _files.Remove(path);
            }
            else if (state != file.State)
            {
                _files[path] = new Waiting(state, now + QuietTime);
            }
            else if (_probe.CanOpenAlone(path))
            {
                _files.Remove(path);
                results.Add(new Arrival(path, ArrivalOutcome.Arrived));
            }
            else if (file.InUseSince is { } since && now - since >= InUseTime)
            {
                _files.Remove(path);
                results.Add(new Arrival(path, ArrivalOutcome.InUse));
            }
            else
            {
                var wait = file.RetryWait == TimeSpan.Zero ? QuietTime : TimeSpan.FromTicks(Math.Min(file.RetryWait.Ticks * 2, LongestRetryWait.Ticks));
                _files[path] = file with { Due = now + wait, InUseSince = file.InUseSince ?? now, RetryWait = wait };
            }
        }

        return results;
    }

    private sealed record Waiting(FileState State, DateTimeOffset Due, DateTimeOffset? InUseSince = null, TimeSpan RetryWait = default);
}
