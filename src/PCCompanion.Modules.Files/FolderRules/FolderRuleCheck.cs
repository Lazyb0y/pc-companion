using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// A destination's subfolder made from the file has a part in braces that isn't known or
    /// closed, a character a folder name can't have, or is <c>.</c> or <c>..</c>.
    /// </summary>
    InvalidPart,

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

    /// <summary>A name pattern is empty or holds characters a file name can't have, besides <c>*</c> and <c>?</c>.</summary>
    InvalidNamePattern,

    /// <summary>An extension is empty, holds a dot inside it, or holds characters a file name can't have.</summary>
    InvalidExtension,

    /// <summary>The Size condition has no size, or one that isn't from 1 to <see cref="FolderRuleCheck.MaximumSizeAmount"/>.</summary>
    InvalidSize,

    /// <summary>The smaller-than size isn't larger than the larger-than size, so no file matches.</summary>
    EmptySizeRange,

    /// <summary>The Date condition has no number of days, or one outside 0 to <see cref="FolderRuleCheck.MaximumDays"/>.</summary>
    InvalidDate,

    /// <summary>The less-than days aren't more than the more-than days, so no file matches.</summary>
    EmptyDateRange,

    /// <summary>A site pattern is empty or holds characters a site's name can't have, besides <c>*</c> and <c>?</c>.</summary>
    InvalidSitePattern,

    UnknownAction,
    NoDestination,

    /// <summary>The destination can't be used; <see cref="FolderRuleCheck.CheckDestination"/> says why.</summary>
    DestinationNotAllowed,

    /// <summary>A Move or Copy to the rule's own folder, which Rename does.</summary>
    DestinationIsOwnFolder,

    NoNewName,

    /// <summary>
    /// The new name has a part in braces that isn't known or closed, a character a file name can't
    /// have, or, without parts, is a name Windows doesn't allow, such as <c>CON</c>.
    /// </summary>
    InvalidNewName,

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

    /// <summary>The largest number in a size, in any unit.</summary>
    public const long MaximumSizeAmount = 1_000_000;

    /// <summary>The most days in the Date condition, about 100 years.</summary>
    public const int MaximumDays = 36_500;

    // The longest a file name, and so a name pattern, can be, and the longest a site's name can be.
    private const int MaximumFileNameLength = 255;
    private const int MaximumSiteLength = 253;

    // What a name pattern can't hold: what Windows doesn't allow in a file name, except wildcards.
    private static readonly char[] InvalidPatternCharacters = [.. Path.GetInvalidFileNameChars().Except(['*', '?'])];

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
        if (fixedPart.Length == 0)
        {
            return FolderProblem.NotFullPath;
        }

        var problem = CheckFolder(fixedPart, places);
        return problem == FolderProblem.None && !FileNaming.IsDestinationPattern(destination) ? FolderProblem.InvalidPart : problem;
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

        var conditions = new[]
        {
            CheckNames(rule.Names),
            CheckExtensions(rule.Extensions),
            CheckSize(rule.Size),
            CheckDate(rule.Date),
            CheckSites(rule.DownloadedFrom),
        };
        var condition = conditions.FirstOrDefault(problem => problem != RuleProblem.None);
        if (condition != RuleProblem.None)
        {
            return condition;
        }

        return rule.KnownAction switch
        {
            null => RuleProblem.UnknownAction,
            FolderRuleAction.Move or FolderRuleAction.Copy => CheckMoveOrCopy(rule, folder, places),
            FolderRuleAction.Rename when rule.NewName.Trim().Length == 0 => RuleProblem.NoNewName,
            FolderRuleAction.Rename when !FileNaming.IsNewNamePattern(rule.NewName) => RuleProblem.InvalidNewName,
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

    /// <summary>
    /// Checks the Name condition's patterns, such as <c>invoice*</c>, which match the whole name,
    /// extension included, with <c>*</c> for any characters and <c>?</c> for one.
    /// </summary>
    public static RuleProblem CheckNames(IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        return patterns.All(pattern => pattern.Trim() is { Length: > 0 and <= MaximumFileNameLength } trimmed && trimmed.IndexOfAny(InvalidPatternCharacters) < 0)
            ? RuleProblem.None
            : RuleProblem.InvalidNamePattern;
    }

    /// <summary>Checks the Type condition's extensions, such as <c>pdf</c>; a dot before one is left out.</summary>
    public static RuleProblem CheckExtensions(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        return extensions.All(extension => TrimExtension(extension) is { Length: > 0 and <= MaximumFileNameLength } trimmed
                && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && !trimmed.Contains('.', StringComparison.Ordinal))
            ? RuleProblem.None
            : RuleProblem.InvalidExtension;
    }

    public static RuleProblem CheckSize(SizeCondition? size)
    {
        if (size is null)
        {
            return RuleProblem.None;
        }

        if ((size.LargerThan is null && size.SmallerThan is null) || !IsSize(size.LargerThan) || !IsSize(size.SmallerThan))
        {
            return RuleProblem.InvalidSize;
        }

        return RuleMatcher.Bytes(size.LargerThan) is { } larger && RuleMatcher.Bytes(size.SmallerThan) is { } smaller && smaller <= larger + 1
            ? RuleProblem.EmptySizeRange
            : RuleProblem.None;
    }

    public static RuleProblem CheckDate(DateCondition? date)
    {
        if (date is null)
        {
            return RuleProblem.None;
        }

        if (!Enum.IsDefined(date.Which)
            || (date.MoreThanDaysAgo is null && date.LessThanDaysAgo is null)
            || date.MoreThanDaysAgo is < 0 or > MaximumDays
            || date.LessThanDaysAgo is < 1 or > MaximumDays)
        {
            return RuleProblem.InvalidDate;
        }

        return date.MoreThanDaysAgo is { } more && date.LessThanDaysAgo is { } less && less <= more
            ? RuleProblem.EmptyDateRange
            : RuleProblem.None;
    }

    /// <summary>
    /// Checks the Downloaded from condition's site patterns, such as <c>*.example.com</c>: a site's
    /// name, with <c>*</c> for any characters and <c>?</c> for one.
    /// </summary>
    public static RuleProblem CheckSites(IReadOnlyList<string> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);

        return sites.All(site => site.Trim() is { Length: > 0 and <= MaximumSiteLength } trimmed
                && trimmed.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '*' or '?'))
            ? RuleProblem.None
            : RuleProblem.InvalidSitePattern;
    }

    /// <summary>An extension as the rules compare it: without spaces at its ends or a dot before it.</summary>
    internal static string TrimExtension(string extension) => extension.Trim().TrimStart('.');

    // A size of 1 to the largest amount, in a known unit.
    private static bool IsSize(SizeLimit? size) =>
        size is null || (size.Amount is >= 1 and <= MaximumSizeAmount && Enum.IsDefined(size.Unit));

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

        // A Move or Copy keeps the file's name when it has no new name.
        if (rule.NewName.Trim().Length > 0 && !FileNaming.IsNewNamePattern(rule.NewName))
        {
            return RuleProblem.InvalidNewName;
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
