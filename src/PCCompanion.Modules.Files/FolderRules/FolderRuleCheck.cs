using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>What keeps a folder from being watched, or from being a rule's destination.</summary>
public enum FolderProblem
{
    None,
    NoPath,

    /// <summary>
    /// The path holds characters Windows doesn't allow or wildcards, names a stream of a file, or
    /// reaches a device directly, such as <c>\\?\C:\</c>.
    /// </summary>
    InvalidPath,

    /// <summary>
    /// The path doesn't start with a drive or a server and a folder it shares, such as <c>D:\</c>
    /// or <c>\\server\share</c>.
    /// </summary>
    NotFullPath,

    /// <summary>The Windows folder or a folder in it, which only administrators can change.</summary>
    WindowsFolder,

    /// <summary>A Program Files folder or a folder in one, which only administrators can change.</summary>
    ProgramFiles,

    /// <summary>PC Companion's data folder or a folder in it, where a rule could move its settings.</summary>
    DataFolder,

    /// <summary>A folder earlier in the list is the same folder.</summary>
    AlreadyWatched,

    /// <summary>It comes after the first <see cref="FolderRulesSettings.MaximumFolders"/> folders.</summary>
    TooMany,
}

/// <summary>What keeps a rule from being used.</summary>
public enum RuleProblem
{
    None,
    NoName,
    NameTooLong,

    /// <summary>The name holds control characters, such as a line break.</summary>
    InvalidName,

    /// <summary>No condition is on, so the rule would act on every file.</summary>
    NoCondition,

    UnknownAction,
    NoDestination,

    /// <summary>The destination can't be used; <see cref="FolderRuleCheck.CheckDestination"/> says why.</summary>
    DestinationNotAllowed,

    /// <summary>A Move or Copy to the rule's own folder, which Rename does.</summary>
    DestinationIsOwnFolder,

    NoNewName,

    /// <summary>What to do when a name is taken isn't <c>AddNumber</c> or <c>Skip</c>.</summary>
    UnknownIfExists,

    /// <summary>It comes after the first <see cref="FolderRulesSettings.MaximumRules"/> rules.</summary>
    TooMany,
}

/// <summary>A folder's problem and its rules' problems, in the order of the list.</summary>
public sealed record FolderReview(FolderProblem Problem, IReadOnlyList<RuleProblem> Rules);

/// <summary>
/// Checks folders and rules, for the host and for the page and the rule editor. See "Rules" and
/// "Which folders" in docs/features/files/folder-rules.md.
/// </summary>
public static class FolderRuleCheck
{
    /// <summary>The longest name, which the history shows.</summary>
    public const int MaximumNameLength = 60;

    /// <summary>
    /// The problem of each folder and rule in <paramref name="settings"/>, including those beyond the
    /// limits. The rules of a folder beyond the limit are beyond it too, and don't count.
    /// </summary>
    public static IReadOnlyList<FolderReview> Review(FolderRulesSettings settings, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(places);

        var reviews = new List<FolderReview>();
        var watched = new List<string>();
        var rules = 0;
        foreach (var folder in settings.Folders)
        {
            if (reviews.Count >= FolderRulesSettings.MaximumFolders)
            {
                reviews.Add(new FolderReview(FolderProblem.TooMany, [.. folder.Rules.Select(_ => RuleProblem.TooMany)]));
                continue;
            }

            var problem = CheckFolder(folder.Path, places);
            if (problem == FolderProblem.None)
            {
                FolderPaths.Normalize(folder.Path, out var fullPath);
                if (watched.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    problem = FolderProblem.AlreadyWatched;
                }

                watched.Add(fullPath);
            }

            var ruleProblems = new List<RuleProblem>();
            foreach (var rule in folder.Rules)
            {
                rules++;
                ruleProblems.Add(rules > FolderRulesSettings.MaximumRules ? RuleProblem.TooMany : Check(rule, folder.Path, places));
            }

            reviews.Add(new FolderReview(problem, ruleProblems));
        }

        return reviews;
    }

    /// <summary>Checks a folder to watch, by its path alone.</summary>
    public static FolderProblem CheckFolder(string path, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(places);

        var problem = FolderPaths.Normalize(path, out var fullPath);
        return problem == FolderProblem.None ? CheckPlace(fullPath, places) : problem;
    }

    /// <summary>
    /// Checks a Move or Copy destination by the folder it starts with, before any subfolder made
    /// from the file, such as <c>D:\Invoices</c> for <c>D:\Invoices\{year}</c>.
    /// </summary>
    public static FolderProblem CheckDestination(string destination, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Trim().Length == 0)
        {
            return FolderProblem.NoPath;
        }

        var fixedPart = FolderPaths.FixedPart(destination);
        return fixedPart.Length == 0 ? FolderProblem.NotFullPath : CheckFolder(fixedPart, places);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is in <c>AppData</c> or <c>ProgramData</c>, where programs
    /// keep their own files, which the page warns about.
    /// </summary>
    public static bool IsProgramData(string path, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(places);

        return FolderPaths.Normalize(path, out var fullPath) == FolderProblem.None
            && places.ProgramData.Any(folder => FolderPaths.IsInside(fullPath, folder));
    }

    /// <summary>The first problem of a rule of the folder <paramref name="folder"/>, or <see cref="RuleProblem.None"/>.</summary>
    public static RuleProblem Check(FolderRule rule, string folder, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(places);

        var name = CheckName(rule.Name);
        if (name != RuleProblem.None)
        {
            return name;
        }

        if (!rule.HasCondition)
        {
            return RuleProblem.NoCondition;
        }

        return rule.KnownAction switch
        {
            null => RuleProblem.UnknownAction,
            FolderRuleAction.Move or FolderRuleAction.Copy => CheckMoveOrCopy(rule, folder, places),
            FolderRuleAction.Rename when rule.NewName.Trim().Length == 0 => RuleProblem.NoNewName,
            FolderRuleAction.Rename when rule.KnownIfExists is null => RuleProblem.UnknownIfExists,
            _ => RuleProblem.None,
        };
    }

    public static RuleProblem CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return RuleProblem.NoName;
        }

        if (trimmed.Length > MaximumNameLength)
        {
            return RuleProblem.NameTooLong;
        }

        return trimmed.Any(char.IsControl) ? RuleProblem.InvalidName : RuleProblem.None;
    }

    private static RuleProblem CheckMoveOrCopy(FolderRule rule, string folder, SpecialFolders places)
    {
        switch (CheckDestination(rule.Destination, places))
        {
            case FolderProblem.None:
                break;
            case FolderProblem.NoPath:
                return RuleProblem.NoDestination;
            default:
                return RuleProblem.DestinationNotAllowed;
        }

        // A destination with subfolders made from the file is checked when a file gets them.
        if (!FolderPaths.HasParts(rule.Destination)
            && FolderPaths.Normalize(rule.Destination, out var destination) == FolderProblem.None
            && FolderPaths.Normalize(folder, out var source) == FolderProblem.None
            && string.Equals(destination, source, StringComparison.OrdinalIgnoreCase))
        {
            return RuleProblem.DestinationIsOwnFolder;
        }

        return rule.KnownIfExists is null ? RuleProblem.UnknownIfExists : RuleProblem.None;
    }

    // The places a folder can't be in, for a folder whose path is full.
    private static FolderProblem CheckPlace(string fullPath, SpecialFolders places)
    {
        if (FolderPaths.IsInside(fullPath, places.DataFolder))
        {
            return FolderProblem.DataFolder;
        }

        if (FolderPaths.IsInside(fullPath, places.Windows))
        {
            return FolderProblem.WindowsFolder;
        }

        return places.ProgramFiles.Any(folder => FolderPaths.IsInside(fullPath, folder)) ? FolderProblem.ProgramFiles : FolderProblem.None;
    }
}
