using System.Globalization;
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
