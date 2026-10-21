using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;

namespace PCCompanion.App.Features.ClipboardActions;

/// <summary>A rule in the page's list.</summary>
/// <param name="Index">The rule's place in the list, from 0.</param>
/// <param name="Summary">What the rule does, or why it isn't in the menu.</param>
public sealed record ClipboardRuleItem(int Index, ClipboardRule Rule, string Name, string Summary, bool HasProblem, string EditName, string MoreName, bool CanMoveUp, bool CanMoveDown);

/// <summary>
/// The Clipboard Actions page. Every change is saved right away; a rule is saved when the rule
/// editor's Save is chosen.
/// </summary>
public sealed partial class ClipboardActionsViewModel : FeatureViewModel<ClipboardActionsSettings>
{
    private static readonly CompositeFormat EditFormat = CompositeFormat.Parse(Strings.ClipboardActionsEditName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.ClipboardActionsMoreName);
    private static readonly CompositeFormat AddressFormat = CompositeFormat.Parse(Strings.ClipboardActionsSummaryAddress);
    private static readonly CompositeFormat ProgramFormat = CompositeFormat.Parse(Strings.ClipboardActionsSummaryProgram);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.ClipboardActionsSummaryProblem);

    public ClipboardActionsViewModel(FeatureSettingsEditor editor)
        : base(editor, ClipboardActionsFeature.Definition)
    {
        RefreshRules();
    }

    public bool OpenLinks
    {
        get => Settings.OpenLinks;
        set => Change(Settings.OpenLinks, value, () => Settings with { OpenLinks = value });
    }

    public ObservableCollection<ClipboardRuleItem> Rules { get; } = [];

    public bool HasNoRules => Rules.Count == 0;

    /// <summary>What keeps a rule out of the menu, or an empty string.</summary>
    public static string ProblemOf(ClipboardRule rule)
    {
        var problem = ClipboardRuleCheck.Check(rule, out _) switch
        {
            RuleProblem.NoName => Strings.ClipboardActionsNameMissing,
            RuleProblem.NoPattern => Strings.ClipboardActionsPatternMissing,
            RuleProblem.InvalidPattern => Strings.ClipboardActionsPatternInvalid,
            RuleProblem.PatternNeedsBacktracking => Strings.ClipboardActionsPatternNotSupported,
            RuleProblem.UnknownAction => Strings.ClipboardActionsUnknownAction,
            RuleProblem.NoAddress => Strings.ClipboardActionsAddressMissing,
            RuleProblem.NotWebAddress => Strings.ClipboardActionsAddressNotWeb,
            RuleProblem.ProgramNotChosen => Strings.ClipboardActionsProgramNotChosen,
            RuleProblem.ProgramNotFullPath => Strings.ClipboardActionsProgramNotFullPath,
            RuleProblem.ProgramNotExe => Strings.ClipboardActionsProgramNotExe,
            RuleProblem.CommandInterpreter => Strings.ClipboardActionsProgramInterpreter,
            _ => string.Empty,
        };

        if (problem.Length == 0 && rule.KnownAction == ClipboardRuleAction.RunProgram
            && ClipboardRuleCheck.CheckProgram(rule.Program, File.Exists) == RuleProgramProblem.NotFound)
        {
            problem = Strings.ClipboardActionsProgramNotFound;
        }

        return problem;
    }

    public void Add(ClipboardRule rule) => SaveRules([.. Settings.Rules, rule]);

    public void Replace(int index, ClipboardRule rule) =>
        SaveRules([.. Settings.Rules.Select((existing, at) => at == index ? rule : existing)]);

    public void Delete(int index) => SaveRules([.. Settings.Rules.Where((_, at) => at != index)]);

    public void Move(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || target < 0 || index >= Settings.Rules.Count || target >= Settings.Rules.Count)
        {
            return;
        }

        var rules = Settings.Rules.ToList();
        (rules[index], rules[target]) = (rules[target], rules[index]);
        SaveRules(rules);
    }

    private static string Summary(ClipboardRule rule, string problem)
    {
        if (problem.Length > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, ProblemFormat, problem);
        }

        return rule.KnownAction == ClipboardRuleAction.RunProgram
            ? string.Format(CultureInfo.CurrentCulture, ProgramFormat, Path.GetFileName(rule.Program))
            : string.Format(CultureInfo.CurrentCulture, AddressFormat, ClipboardRuleCheck.BuildAddress(rule.Address, "text")?.Host);
    }

    private void SaveRules(IReadOnlyList<ClipboardRule> rules)
    {
        Save(Settings with { Rules = rules });
        RefreshRules();
    }

    private void RefreshRules()
    {
        Rules.Clear();
        var rules = Settings.Rules;
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var problem = ProblemOf(rule);
            var name = rule.Name.Trim();
            Rules.Add(new ClipboardRuleItem(
                index,
                rule,
                name,
                Summary(rule, problem),
                problem.Length > 0,
                string.Format(CultureInfo.CurrentCulture, EditFormat, name),
                string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
                index > 0,
                index < rules.Count - 1));
        }

        OnPropertyChanged(nameof(HasNoRules));
    }
}
