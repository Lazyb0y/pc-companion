using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;

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
    private static readonly CompositeFormat BeyondMaximumFormat = CompositeFormat.Parse(Strings.ClipboardActionsBeyondMaximum);

    // How long the page waits for the host to say which shortcuts Windows refused.
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly ClipboardShortcutRefusals NoneRefused = new(false, false);

    private DispatcherQueueTimer? _statusTimer;
    private bool _readingStatus;
    private ClipboardShortcutRefusals _refused = NoneRefused;

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

    public bool SelectedText
    {
        get => Settings.SelectedText;
        set => Change(Settings.SelectedText, value, () => Settings with { SelectedText = value });
    }

    /// <summary>Whether Windows refused Windows + Alt + V, as the running feature last said.</summary>
    public bool CopiedShortcutRefused => _refused.CopiedText;

    /// <summary>Whether Windows refused Windows + Alt + S, as the running feature last said.</summary>
    public bool SelectedShortcutRefused => _refused.SelectedText;

    public ObservableCollection<ClipboardRuleItem> Rules { get; } = [];

    public bool HasNoRules => Rules.Count == 0;

    /// <summary>
    /// What keeps a rule out of the menu, or an empty string. Whether the program of a rule that
    /// runs one exists is given, since it's read off the UI thread.
    /// </summary>
    public static string ProblemOf(ClipboardRule rule, bool programFound)
    {
        var problem = ClipboardRuleCheck.Check(rule, out _) switch
        {
            RuleProblem.NoName => Strings.ClipboardActionsNameMissing,
            RuleProblem.NoPattern => Strings.ClipboardActionsPatternMissing,
            RuleProblem.InvalidPattern => Strings.ClipboardActionsPatternInvalid,
            RuleProblem.PatternNotSupported => Strings.ClipboardActionsPatternNotSupported,
            RuleProblem.UnknownAction => Strings.ClipboardActionsUnknownAction,
            RuleProblem.NoAddress => Strings.ClipboardActionsAddressMissing,
            RuleProblem.NotWebAddress => Strings.ClipboardActionsAddressNotWeb,
            RuleProblem.ProgramNotChosen => Strings.ClipboardActionsProgramNotChosen,
            RuleProblem.ProgramNotFullPath => Strings.ClipboardActionsProgramNotFullPath,
            RuleProblem.ProgramNotExe => Strings.ClipboardActionsProgramNotExe,
            RuleProblem.CommandInterpreter => Strings.ClipboardActionsProgramInterpreter,
            _ => string.Empty,
        };

        if (problem.Length == 0 && rule.KnownAction == ClipboardRuleAction.RunProgram && !programFound)
        {
            problem = Strings.ClipboardActionsProgramNotFound;
        }

        return problem;
    }

    /// <summary>
    /// Reads which shortcuts Windows refused from the host now and every 2 seconds, until
    /// <see cref="StopShortcutStatus"/>. Call it on the page's thread.
    /// </summary>
    public void StartShortcutStatus()
    {
        if (_statusTimer is not null)
        {
            return;
        }

        _statusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(2);
        _statusTimer.Tick += (_, _) => _ = RefreshShortcutStatusAsync();
        _statusTimer.Start();
        _ = RefreshShortcutStatusAsync();
    }

    public void StopShortcutStatus()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
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

    // The pipe is read off the UI thread; a read still going when the next tick comes is let finish.
    // While the feature is off, or the host doesn't answer, no shortcut is shown as refused.
    private async Task RefreshShortcutStatusAsync()
    {
        if (_readingStatus)
        {
            return;
        }

        _readingStatus = true;
        try
        {
            var snapshot = IsOn
                ? await Task.Run(() => SnapshotPipe.ReadAsync(ClipboardShortcutStatus.PipeName, StatusTimeout, ClipboardShortcutStatus.MaximumSnapshotLength)).ConfigureAwait(true)
                : null;
            var refused = (snapshot is null ? null : ClipboardShortcutStatus.FromJson(snapshot)) ?? NoneRefused;
            if (refused != _refused)
            {
                _refused = refused;
                OnPropertyChanged(nameof(CopiedShortcutRefused));
                OnPropertyChanged(nameof(SelectedShortcutRefused));
            }
        }
        finally
        {
            _readingStatus = false;
        }
    }

    private void SaveRules(IReadOnlyList<ClipboardRule> rules)
    {
        Save(Settings with { Rules = rules });
        RefreshRules();
    }

    // The list shows at once; the programs that weren't found are added once they've been looked for.
    private void RefreshRules()
    {
        Rules.Clear();
        var rules = Settings.Rules;
        for (var index = 0; index < rules.Count; index++)
        {
            Rules.Add(ItemOf(rules, index, programFound: true));
        }

        OnPropertyChanged(nameof(HasNoRules));
        _ = ShowMissingProgramsAsync(rules);
    }

    // Whether the rules' programs exist is read off the UI thread, since a program on a network
    // share that's offline can take seconds to find. A result for rules that changed meanwhile is dropped.
    private async Task ShowMissingProgramsAsync(IReadOnlyList<ClipboardRule> rules)
    {
        var missing = await Task.Run(() => Enumerable.Range(0, rules.Count)
            .Where(index => rules[index].KnownAction == ClipboardRuleAction.RunProgram
                && ClipboardRuleCheck.CheckProgram(rules[index].Program, File.Exists) == RuleProgramProblem.NotFound)
            .ToList()).ConfigureAwait(true);

        if (!ReferenceEquals(rules, Settings.Rules) || Rules.Count != rules.Count)
        {
            return;
        }

        foreach (var index in missing)
        {
            var item = ItemOf(rules, index, programFound: false);
            if (item != Rules[index])
            {
                Rules[index] = item;
            }
        }
    }

    // A rule after the first ones the menu uses is left out of it, whatever else it has.
    private static ClipboardRuleItem ItemOf(IReadOnlyList<ClipboardRule> rules, int index, bool programFound)
    {
        var rule = rules[index];
        var problem = index < ClipboardActionsSettings.MaximumRules
            ? ProblemOf(rule, programFound)
            : string.Format(CultureInfo.CurrentCulture, BeyondMaximumFormat, ClipboardActionsSettings.MaximumRules);
        var name = rule.Name.Trim();
        return new ClipboardRuleItem(
            index,
            rule,
            name,
            Summary(rule, problem),
            problem.Length > 0,
            string.Format(CultureInfo.CurrentCulture, EditFormat, name),
            string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
            index > 0,
            index < rules.Count - 1);
    }
}
