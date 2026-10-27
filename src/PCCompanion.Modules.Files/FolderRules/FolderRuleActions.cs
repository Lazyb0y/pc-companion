using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Carries out a rule's action on a file, on the feature's thread, and says what happened for the
/// history; says what a rule would do, for Run now; and puts a file back, for Undo. A file is never
/// replaced: a name that's taken gets the next number, or the file is skipped. See "Actions",
/// "Moving and copying" and "History and undo" in docs/features/files/folder-rules.md.
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
        if (Plan(folder, rule, file) is not { Action: not FolderRuleAction.RecycleBin } plan)
        {
            return null;
        }

        var entry = new HistoryEntry
        {
            Time = now,
            Folder = folder,
            Rule = plan.Rule,
            Name = file.Name,
            File = new FileIdentity(file.FileId, file.Size, file.Modified),
        };
        if (plan.Naming != NamingProblem.None)
        {
            return Skipped(entry, SkipReason.Naming) with { Naming = plan.Naming };
        }

        var source = Path.Join(folder, file.Name);
        var ifExists = rule.Rule.KnownIfExists ?? NameConflict.Skip;
        return plan.Action switch
        {
            FolderRuleAction.Move => Transfer(entry, rule.Rule, plan, source, ifExists, HistoryOutcome.Moved, cancel),
            FolderRuleAction.Copy => Transfer(entry, rule.Rule, plan, source, ifExists, HistoryOutcome.Copied, cancel),
            _ => Place(entry, source, folder, plan.NewName, ifExists, HistoryOutcome.Renamed, (from, to) => _system.Move(from, to, cancel)),
        };
    }

    /// <summary>
    /// What <paramref name="rule"/> would do with <paramref name="file"/>, which is directly in
    /// <paramref name="folder"/>, as Run now lists it, without doing anything, or
    /// <see langword="null"/> when there would be nothing to do, such as a rename to the name the
    /// file has.
    /// </summary>
    public RunNowFile? Plan(string folder, RuleMatcher rule, FileDetails file)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(file);

        if (rule.Rule.KnownAction is not { } action)
        {
            return null;
        }

        var facts = new FileFacts(file.Name, file.Size, file.Created, file.Modified);
        var plan = new RunNowFile { Name = file.Name, Size = file.Size, Modified = file.Modified, Rule = rule.Rule.Name.Trim(), Action = action };
        switch (action)
        {
            case FolderRuleAction.Move or FolderRuleAction.Copy:
                var destination = FileNaming.Destination(rule.Rule.Destination, facts, _zone, folder, _places);
                if (destination.Problem != NamingProblem.None)
                {
                    return plan with { Naming = destination.Problem };
                }

                var name = rule.Rule.NewName.Trim().Length > 0 ? FileNaming.NewName(rule.Rule.NewName, facts, _zone) : new Naming(file.Name, NamingProblem.None);
                return plan with { Destination = destination.Value, NewName = name.Value, Naming = name.Problem };
            case FolderRuleAction.Rename:
                var newName = FileNaming.NewName(rule.Rule.NewName, facts, _zone);
                return string.Equals(newName.Value, file.Name, StringComparison.Ordinal) ? null : plan with { NewName = newName.Value, Naming = newName.Problem };
            default:
                return plan;
        }
    }

    /// <summary>
    /// Whether the file of a move, copy or rename can be put back: it's still where the rule put
    /// it, unchanged, and, for a move or rename, its old place is free. Returns
    /// <see cref="FolderRulesAnswerStatus.Done"/> when it can.
    /// </summary>
    public FolderRulesAnswer CheckUndo(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Outcome is not (HistoryOutcome.Moved or HistoryOutcome.Copied or HistoryOutcome.Renamed) || entry.Undone is not null || entry.File is not { } identity)
        {
            return Undo(FolderRulesAnswerStatus.Refused);
        }

        try
        {
            if (_system.Read(entry.NewPath) is not { IsFolder: false, IsLink: false } file
                || !identity.Matches(new FileIdentity(file.FileId, file.Size, file.Modified), file.Name, [entry.Name, Path.GetFileName(entry.NewPath)]))
            {
                return Undo(FolderRulesAnswerStatus.Changed);
            }

            // A rename that changed only the case leaves the old place to the file itself.
            var oldPath = OldPath(entry);
            return entry.Outcome != HistoryOutcome.Copied && !string.Equals(oldPath, entry.NewPath, StringComparison.OrdinalIgnoreCase) && _system.Read(oldPath) is not null
                ? Undo(FolderRulesAnswerStatus.PlaceTaken)
                : Undo(FolderRulesAnswerStatus.Done);
        }
        catch (Win32Exception exception)
        {
            return Undo(FolderRulesAnswerStatus.Failed) with { ErrorCode = exception.NativeErrorCode };
        }
    }

    /// <summary>Moves the file of a move or rename back to its old place and name, never over a file there.</summary>
    public FolderRulesAnswer PutBack(HistoryEntry entry, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var oldPath = OldPath(entry);
        var error = _system.Move(entry.NewPath, oldPath, cancel);
        return error switch
        {
            0 => Undo(FolderRulesAnswerStatus.Done),
            FileTransfer.FileExists or FileTransfer.AlreadyExists => Undo(FolderRulesAnswerStatus.PlaceTaken),
            FileNotFound => Undo(FolderRulesAnswerStatus.Changed),
            SharingViolation or LockViolation => Undo(FolderRulesAnswerStatus.InUse),
            AccessDenied when IsProtected(entry.NewPath, oldPath) => Undo(FolderRulesAnswerStatus.Blocked) with { ErrorCode = error },
            _ => Undo(FolderRulesAnswerStatus.Failed) with { ErrorCode = error },
        };
    }

    private static string OldPath(HistoryEntry entry) => Path.Join(entry.Folder, entry.Name);

    private static FolderRulesAnswer Undo(FolderRulesAnswerStatus status) => new() { Status = status };

    private HistoryEntry Transfer(
        HistoryEntry entry, FolderRule rule, RunNowFile plan, string source, NameConflict ifExists, HistoryOutcome outcome, CancellationToken cancel)
    {
        // Windows drops a file's Mark of the Web without an error on a drive that can't keep it,
        // so a file with a mark stays rather than lose it.
        FolderPaths.Normalize(FolderPaths.FixedPart(rule.Destination), out var start);
        if (!_system.KeepsStreams(start) && _system.ReadMark(source) is not null)
        {
            return Skipped(entry, SkipReason.MarkWouldBeLost);
        }

        var prepared = PrepareDestination(start, plan.Destination);
        if (prepared is not null)
        {
            return prepared(entry);
        }

        return Place(entry, source, plan.Destination, plan.NewName, ifExists, outcome,
            (from, to) => outcome == HistoryOutcome.Moved ? _system.Move(from, to, cancel) : _system.Copy(from, to, cancel));
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
    // is a link or a hidden system folder, and creates the subfolders made from the file. Returns
    // how to skip the file, or null.
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
            else if (!details.IsFolder || details.IsLink || FolderRuleFilter.IsSystemFolder(details))
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
        AccessDenied when IsProtected(paths) => Skipped(entry, SkipReason.Blocked) with { ErrorCode = error },
        _ => Skipped(entry, SkipReason.Failed) with { ErrorCode = error },
    };

    private static HistoryEntry Skipped(HistoryEntry entry, SkipReason reason) => entry with { Outcome = HistoryOutcome.Skipped, Reason = reason };

    private bool IsProtected(params string[] paths) => paths.Any(path => _places.Protected.Any(folder => FolderPaths.IsInside(path, folder)));
}
