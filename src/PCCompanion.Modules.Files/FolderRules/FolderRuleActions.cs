using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Carries out a rule's action on a file, on the feature's thread, and says what happened for the
/// history. A file is never replaced: a name that's taken gets the next number, or the file is
/// skipped. See "Actions" and "Moving and copying" in docs/features/files/folder-rules.md.
/// </summary>
public sealed class FolderRuleActions
{
    // Windows' error codes that the history tells apart.
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;
    private const int AccessDenied = 5;
    private const int NotReady = 21;
    private const int DeviceNotConnected = 1167;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    private readonly IFolderRulesSystem _system;
    private readonly SpecialFolders _places;
    private readonly TimeZoneInfo _zone;

    /// <param name="zone">The user's time zone, for the dates in new names and destinations.</param>
    public FolderRuleActions(IFolderRulesSystem system, SpecialFolders places, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(zone);

        _system = system;
        _places = places;
        _zone = zone;
    }

    /// <summary>
    /// Does what <paramref name="rule"/> says with <paramref name="file"/>, which is directly in
    /// <paramref name="folder"/>, and returns the history's entry for it, or <see langword="null"/>
    /// when there was nothing to do, such as a rename to the name the file has.
    /// </summary>
    public HistoryEntry? Act(string folder, RuleMatcher rule, FileDetails file, DateTimeOffset now, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(file);

        var entry = new HistoryEntry
        {
            Time = now,
            Folder = folder,
            Rule = rule.Rule.Name.Trim(),
            Name = file.Name,
            File = new FileIdentity(file.FileId, file.Size, file.Modified),
        };
        var source = Path.Join(folder, file.Name);
        var facts = new FileFacts(file.Name, file.Size, file.Created, file.Modified);
        var ifExists = rule.Rule.KnownIfExists ?? NameConflict.Skip;

        return rule.Rule.KnownAction switch
        {
            FolderRuleAction.Move => Transfer(entry, rule.Rule, facts, folder, source, ifExists, HistoryOutcome.Moved, cancel),
            FolderRuleAction.Copy => Transfer(entry, rule.Rule, facts, folder, source, ifExists, HistoryOutcome.Copied, cancel),
            FolderRuleAction.Rename => Rename(entry, rule.Rule, facts, folder, source, ifExists, cancel),
            _ => null,
        };
    }

    private HistoryEntry Transfer(
        HistoryEntry entry, FolderRule rule, FileFacts facts, string folder, string source, NameConflict ifExists, HistoryOutcome outcome, CancellationToken cancel)
    {
        var destination = FileNaming.Destination(rule.Destination, facts, _zone, folder, _places);
        if (destination.Problem != NamingProblem.None)
        {
            return Skipped(entry, SkipReason.Naming) with { Naming = destination.Problem };
        }

        var name = facts.Name;
        if (rule.NewName.Trim().Length > 0)
        {
            var newName = FileNaming.NewName(rule.NewName, facts, _zone);
            if (newName.Problem != NamingProblem.None)
            {
                return Skipped(entry, SkipReason.Naming) with { Naming = newName.Problem };
            }

            name = newName.Value;
        }

        // Windows drops a file's Mark of the Web without an error on a drive that can't keep it,
        // so a file with a mark stays rather than lose it.
        FolderPaths.Normalize(FolderPaths.FixedPart(rule.Destination), out var start);
        if (!_system.KeepsStreams(start) && _system.ReadMark(source) is not null)
        {
            return Skipped(entry, SkipReason.MarkWouldBeLost);
        }

        var prepared = PrepareDestination(start, destination.Value);
        if (prepared is not null)
        {
            return prepared(entry);
        }

        return Place(entry, source, destination.Value, name, ifExists, outcome,
            (from, to) => outcome == HistoryOutcome.Moved ? _system.Move(from, to, cancel) : _system.Copy(from, to, cancel));
    }

    private HistoryEntry? Rename(HistoryEntry entry, FolderRule rule, FileFacts facts, string folder, string source, NameConflict ifExists, CancellationToken cancel)
    {
        var newName = FileNaming.NewName(rule.NewName, facts, _zone);
        if (newName.Problem != NamingProblem.None)
        {
            return Skipped(entry, SkipReason.Naming) with { Naming = newName.Problem };
        }

        return string.Equals(newName.Value, facts.Name, StringComparison.Ordinal)
            ? null
            : Place(entry, source, folder, newName.Value, ifExists, HistoryOutcome.Renamed, (from, to) => _system.Move(from, to, cancel));
    }

    // Tries each name the rule allows until one is free. A name that appears at the last moment
    // makes the move fail rather than replace it, and the next number is tried.
    private HistoryEntry Place(
        HistoryEntry entry, string source, string destination, string name, NameConflict ifExists, HistoryOutcome outcome, Func<string, string, int> transfer)
    {
        foreach (var candidate in FileNaming.Candidates(name, ifExists))
        {
            var path = Path.Join(destination, candidate);
            var error = transfer(source, path);
            switch (error)
            {
                case 0:
                    return entry with { Outcome = outcome, NewPath = path };
                case FileTransfer.FileExists or FileTransfer.AlreadyExists:
                    continue;
                default:
                    return Refused(entry, error, source, destination);
            }
        }

        return Skipped(entry, SkipReason.NameTaken);
    }

    // Checks that the folder the destination starts with, start, exists and that nothing on the way
    // is a link, and creates the subfolders made from the file. Returns how to skip the file, or null.
    private Func<HistoryEntry, HistoryEntry>? PrepareDestination(string start, string destination)
    {
        var root = Path.GetPathRoot(start)!;
        var path = root;
        foreach (var part in destination[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Join(path, part);
            FileDetails? details;
            try
            {
                details = _system.Read(path);
            }
            catch (Win32Exception exception)
            {
                return entry => Refused(entry, exception.NativeErrorCode, path);
            }

            var inFixedPart = path.Length <= start.Length;
            if (details is null)
            {
                if (inFixedPart)
                {
                    return entry => Skipped(entry, SkipReason.DestinationNotFound);
                }

                var error = _system.CreateFolder(path);
                if (error != 0)
                {
                    return entry => Refused(entry, error, path);
                }
            }
            else if (!details.IsFolder || details.IsLink)
            {
                return entry => Skipped(entry, SkipReason.Naming) with { Naming = NamingProblem.DestinationNotAllowed };
            }
        }

        return null;
    }

    // Controlled folder access refuses apps Windows doesn't trust in the folders it protects, with
    // no error of its own, and its setting can't be read without admin rights, so a refusal there
    // counts as one of its blocks.
    private HistoryEntry Refused(HistoryEntry entry, int error, params string[] paths) => error switch
    {
        SharingViolation or LockViolation => Skipped(entry, SkipReason.InUse),
        FileTransfer.Cancelled or NotReady or DeviceNotConnected => Skipped(entry, SkipReason.Interrupted),
        FileNotFound => Skipped(entry, SkipReason.Changed),
        PathNotFound => Skipped(entry, SkipReason.DestinationNotFound),
        AccessDenied when paths.Any(path => _places.Protected.Any(folder => FolderPaths.IsInside(path, folder))) =>
            Skipped(entry, SkipReason.Blocked) with { ErrorCode = error },
        _ => Skipped(entry, SkipReason.Failed) with { ErrorCode = error },
    };

    private static HistoryEntry Skipped(HistoryEntry entry, SkipReason reason) => entry with { Outcome = HistoryOutcome.Skipped, Reason = reason };
}
