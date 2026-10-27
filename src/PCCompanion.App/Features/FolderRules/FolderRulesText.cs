using System;
using System.Globalization;
using System.IO;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>What the page and the rule editor say about folders and rules.</summary>
public static class FolderRulesText
{
    private static readonly CompositeFormat FolderTooManyFormat = CompositeFormat.Parse(Strings.FolderRulesFolderTooMany);
    private static readonly CompositeFormat RuleTooManyFormat = CompositeFormat.Parse(Strings.FolderRulesRuleTooMany);
    private static readonly CompositeFormat DestinationFormat = CompositeFormat.Parse(Strings.FolderRulesRuleDestinationNotAllowed);
    private static readonly CompositeFormat MoveFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryMove);
    private static readonly CompositeFormat CopyFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryCopy);
    private static readonly CompositeFormat RenameFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryRename);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryProblem);
    private static readonly CompositeFormat MovedFormat = CompositeFormat.Parse(Strings.FolderRulesOutcomeMoved);
    private static readonly CompositeFormat CopiedFormat = CompositeFormat.Parse(Strings.FolderRulesOutcomeCopied);
    private static readonly CompositeFormat RenamedFormat = CompositeFormat.Parse(Strings.FolderRulesOutcomeRenamed);
    private static readonly CompositeFormat SkippedFormat = CompositeFormat.Parse(Strings.FolderRulesOutcomeSkipped);
    private static readonly CompositeFormat UndoneFormat = CompositeFormat.Parse(Strings.FolderRulesOutcomeUndone);
    private static readonly CompositeFormat ReasonFailedFormat = CompositeFormat.Parse(Strings.FolderRulesReasonFailed);
    private static readonly CompositeFormat UndoFailedFormat = CompositeFormat.Parse(Strings.FolderRulesUndoFailed);

    /// <summary>What keeps a folder from being watched, or from being a destination, or an empty string.</summary>
    public static string Of(FolderProblem problem) => problem switch
    {
        FolderProblem.NoPath => Strings.FolderRulesFolderNoPath,
        FolderProblem.InvalidPath => Strings.FolderRulesFolderInvalidPath,
        FolderProblem.NotFullPath => Strings.FolderRulesFolderNotFullPath,
        FolderProblem.WindowsFolder => Strings.FolderRulesFolderWindows,
        FolderProblem.ProgramFiles => Strings.FolderRulesFolderProgramFiles,
        FolderProblem.DataFolder => Strings.FolderRulesFolderDataFolder,
        FolderProblem.SystemFolder => Strings.FolderRulesFolderSystem,
        FolderProblem.InvalidPart => Strings.FolderRulesRuleInvalidPart,
        FolderProblem.AlreadyWatched => Strings.FolderRulesFolderAlreadyWatched,
        FolderProblem.TooMany => string.Format(CultureInfo.CurrentCulture, FolderTooManyFormat, FolderRulesSettings.MaximumFolders),
        _ => string.Empty,
    };

    /// <summary>What keeps a rule from being used, or an empty string.</summary>
    public static string Of(RuleProblem problem, FolderRule rule, SpecialFolders places) => problem switch
    {
        RuleProblem.NoName => Strings.FolderRulesRuleNoName,
        RuleProblem.NameTooLong => Strings.FolderRulesRuleNameTooLong,
        RuleProblem.InvalidName => Strings.FolderRulesRuleInvalidName,
        RuleProblem.NoCondition => Strings.FolderRulesRuleNoCondition,
        RuleProblem.InvalidNamePattern => Strings.FolderRulesRuleInvalidNamePattern,
        RuleProblem.InvalidExtension => Strings.FolderRulesRuleInvalidExtension,
        RuleProblem.InvalidSize => Strings.FolderRulesRuleInvalidSize,
        RuleProblem.EmptySizeRange => Strings.FolderRulesRuleEmptySizeRange,
        RuleProblem.InvalidDate => Strings.FolderRulesRuleInvalidDate,
        RuleProblem.EmptyDateRange => Strings.FolderRulesRuleEmptyDateRange,
        RuleProblem.InvalidSitePattern => Strings.FolderRulesRuleInvalidSitePattern,
        RuleProblem.UnknownAction => Strings.FolderRulesRuleUnknownAction,
        RuleProblem.NoDestination => Strings.FolderRulesRuleNoDestination,
        RuleProblem.DestinationNotAllowed => string.Format(CultureInfo.CurrentCulture, DestinationFormat, Of(FolderRuleCheck.CheckDestination(rule.Destination, places))),
        RuleProblem.DestinationIsOwnFolder => Strings.FolderRulesRuleDestinationIsOwnFolder,
        RuleProblem.NoNewName => Strings.FolderRulesRuleNoNewName,
        RuleProblem.InvalidNewName => Strings.FolderRulesRuleInvalidNewName,
        RuleProblem.UnknownIfExists => Strings.FolderRulesRuleUnknownIfExists,
        RuleProblem.TooMany => string.Format(CultureInfo.CurrentCulture, RuleTooManyFormat, FolderRulesSettings.MaximumRules),
        _ => string.Empty,
    };

    /// <summary>Why a rule can't name a file, or make its destination from it, or an empty string.</summary>
    public static string Of(NamingProblem problem) => problem switch
    {
        NamingProblem.EmptyName => Strings.FolderRulesNamingEmptyName,
        NamingProblem.InvalidName => Strings.FolderRulesNamingInvalidName,
        NamingProblem.NameTooLong => Strings.FolderRulesNamingNameTooLong,
        NamingProblem.DestinationIsOwnFolder => Strings.FolderRulesNamingOwnFolder,
        NamingProblem.DestinationNotAllowed => Strings.FolderRulesNamingNotAllowed,
        _ => string.Empty,
    };

    /// <summary>What happened to a history entry's file, and whether Undo put it back.</summary>
    public static string Of(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var what = entry.Outcome switch
        {
            HistoryOutcome.Moved => string.Format(CultureInfo.CurrentCulture, MovedFormat, entry.NewPath),
            HistoryOutcome.Copied => string.Format(CultureInfo.CurrentCulture, CopiedFormat, entry.NewPath),
            HistoryOutcome.Renamed => string.Format(CultureInfo.CurrentCulture, RenamedFormat, Path.GetFileName(entry.NewPath)),
            HistoryOutcome.MovedToRecycleBin => Strings.FolderRulesOutcomeRecycleBin,
            HistoryOutcome.WaitingForAnswer => Strings.FolderRulesOutcomeWaiting,
            HistoryOutcome.DeletedForGood => Strings.FolderRulesOutcomeDeleted,
            HistoryOutcome.KeptByAnswer => Strings.FolderRulesOutcomeKept,
            _ => string.Format(CultureInfo.CurrentCulture, SkippedFormat, Of(entry.Reason, entry)),
        };

        return entry.Undone is null ? what : string.Format(CultureInfo.CurrentCulture, UndoneFormat, what);
    }

    /// <summary>What Undo did, or why it didn't, or an empty string when it put the file back.</summary>
    public static string OfUndo(FolderRulesAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return answer.Status switch
        {
            FolderRulesAnswerStatus.Done => string.Empty,
            FolderRulesAnswerStatus.Changed => Strings.FolderRulesUndoChanged,
            FolderRulesAnswerStatus.PlaceTaken => Strings.FolderRulesUndoPlaceTaken,
            FolderRulesAnswerStatus.InUse => Strings.FolderRulesUndoInUse,
            FolderRulesAnswerStatus.Blocked => Strings.FolderRulesUndoBlocked,
            FolderRulesAnswerStatus.NoRecycleBin => Strings.FolderRulesUndoNoRecycleBin,
            FolderRulesAnswerStatus.Kept => Strings.FolderRulesUndoKept,
            FolderRulesAnswerStatus.Failed => string.Format(CultureInfo.CurrentCulture, UndoFailedFormat, answer.ErrorCode),
            _ => Strings.FolderRulesUndoRefused,
        };
    }

    private static string Of(SkipReason reason, HistoryEntry entry) => reason switch
    {
        SkipReason.NameTaken => Strings.FolderRulesReasonNameTaken,
        SkipReason.InUse => Strings.FolderRulesReasonInUse,
        SkipReason.NoRecycleBin => Strings.FolderRulesReasonNoRecycleBin,
        SkipReason.MarkWouldBeLost => Strings.FolderRulesReasonMarkWouldBeLost,
        SkipReason.Blocked => Strings.FolderRulesReasonBlocked,
        SkipReason.Failed => string.Format(CultureInfo.CurrentCulture, ReasonFailedFormat, entry.ErrorCode),
        SkipReason.DestinationNotFound => Strings.FolderRulesReasonDestinationNotFound,
        SkipReason.Naming => Of(entry.Naming),
        SkipReason.Changed => Strings.FolderRulesReasonChanged,
        SkipReason.Interrupted => Strings.FolderRulesReasonInterrupted,
        _ => string.Empty,
    };

    /// <summary>What a rule does, or why it isn't used.</summary>
    public static string Summary(FolderRule rule, string problem)
    {
        if (problem.Length > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, ProblemFormat, problem);
        }

        return rule.KnownAction switch
        {
            FolderRuleAction.Move => string.Format(CultureInfo.CurrentCulture, MoveFormat, rule.Destination.Trim()),
            FolderRuleAction.Copy => string.Format(CultureInfo.CurrentCulture, CopyFormat, rule.Destination.Trim()),
            FolderRuleAction.Rename => string.Format(CultureInfo.CurrentCulture, RenameFormat, rule.NewName.Trim()),
            _ => Strings.FolderRulesSummaryRecycleBin,
        };
    }
}
