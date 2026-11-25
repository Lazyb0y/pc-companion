using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.WindowRules;

namespace PCCompanion.App.Features.WindowRules;

/// <summary>A rule in the page's list.</summary>
/// <param name="Index">The rule's place in the list, from 0.</param>
/// <param name="Summary">What the rule does, or why it isn't used.</param>
public sealed record RuleItem(
    int Index,
    string Name,
    string Summary,
    bool IsEnabled,
    string SwitchName,
    string EditName,
    string MoreName,
    bool CanMoveUp,
    bool CanMoveDown,
    bool CanApplyNow);

/// <summary>
/// The Window Rules page. Every change is saved right away; Apply now asks the host through its pipe.
/// See "Making rules" in docs/features/windows/window-rules.md.
/// </summary>
public sealed class WindowRulesViewModel : FeatureViewModel<WindowRulesSettings>
{
    private static readonly CompositeFormat SwitchFormat = CompositeFormat.Parse(Strings.WindowRulesRuleSwitchName);
    private static readonly CompositeFormat EditFormat = CompositeFormat.Parse(Strings.WindowRulesEditName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.WindowRulesMoreName);
    private static readonly CompositeFormat OpacityFormat = CompositeFormat.Parse(Strings.WindowRulesSummaryOpacity);
    private static readonly CompositeFormat TabFormat = CompositeFormat.Parse(Strings.WindowRulesSummaryTab);
    private static readonly CompositeFormat AppFormat = CompositeFormat.Parse(Strings.WindowRulesSummaryApp);
    private static readonly CompositeFormat NotUsedFormat = CompositeFormat.Parse(Strings.WindowRulesSummaryNotUsed);

    private readonly AppSettings _appSettings;
    private string _applyNowResult = string.Empty;

    public WindowRulesViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, WindowRulesFeature.Definition)
    {
        ArgumentNullException.ThrowIfNull(appSettings);

        _appSettings = appSettings;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOn))
            {
                Refresh();
            }
        };
        Refresh();
    }

    public ObservableCollection<RuleItem> Rules { get; } = [];

    public bool HasNoRules => Rules.Count == 0;

    public bool CanAddRule => Settings.Rules.Count < WindowRulesSettings.MaximumRules;

    /// <summary>Which windows rules leave alone, following whether PC Companion runs as administrator.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.WindowRulesAdministratorInfoOn
        : Strings.WindowRulesAdministratorInfo;

    /// <summary>How the last Apply now went, or an empty string.</summary>
    public string ApplyNowResult
    {
        get => _applyNowResult;
        private set
        {
            if (SetProperty(ref _applyNowResult, value))
            {
                OnPropertyChanged(nameof(HasApplyNowResult));
            }
        }
    }

    public bool HasApplyNowResult => _applyNowResult.Length > 0;

    /// <summary>The rule at <paramref name="index"/>, and the others, for the editor.</summary>
    public (WindowRule Rule, IReadOnlyList<WindowRule> Others) RuleAt(int index) =>
        (Settings.Rules[index], [.. Settings.Rules.Where((_, other) => other != index)]);

    public IReadOnlyList<WindowRule> AllRules => Settings.Rules;

    public void Add(WindowRule rule)
    {
        if (CanAddRule)
        {
            Save(Settings with { Rules = [.. Settings.Rules, rule] });
            Refresh();
        }
    }

    public void Replace(int index, WindowRule rule) => Update(index, _ => rule);

    public void SetEnabled(int index, bool enabled)
    {
        if (index >= 0 && index < Settings.Rules.Count && Settings.Rules[index].Enabled != enabled)
        {
            Update(index, rule => rule with { Enabled = enabled });
        }
    }

    public void Move(int index, int by)
    {
        var to = index + by;
        if (index < 0 || index >= Settings.Rules.Count || to < 0 || to >= Settings.Rules.Count)
        {
            return;
        }

        var rules = Settings.Rules.ToList();
        (rules[index], rules[to]) = (rules[to], rules[index]);
        Save(Settings with { Rules = rules });
        Refresh();
    }

    public void Delete(int index)
    {
        if (index >= 0 && index < Settings.Rules.Count)
        {
            Save(Settings with { Rules = [.. Settings.Rules.Where((_, other) => other != index)] });
            Refresh();
        }
    }

    /// <summary>Asks the host to apply the rule at <paramref name="index"/> to the open windows, and says how it went.</summary>
    public async Task ApplyNowAsync(int index)
    {
        if (index < 0 || index >= Settings.Rules.Count)
        {
            return;
        }

        var request = new WindowRulesRequest { Rule = Settings.Rules[index] with { Enabled = true } };
        var answer = await Task.Run(() => WindowRulesPipe.AskAsync(WindowRulesPipe.Name, request, WindowRulesPipe.Timeout)).ConfigureAwait(true);
        ApplyNowResult = ReadingOrder.Line(answer is null ? Strings.WindowRulesApplyNowNoAnswer : PluralStrings.Format("WindowRulesApplyNowDone", answer.Windows));
    }

    private void Update(int index, Func<WindowRule, WindowRule> change)
    {
        if (index < 0 || index >= Settings.Rules.Count)
        {
            return;
        }

        var rules = Settings.Rules.ToList();
        rules[index] = change(rules[index]);
        Save(Settings with { Rules = rules });
        Refresh();
    }

    private void Refresh()
    {
        Rules.Clear();
        var count = Settings.Rules.Count;
        for (var index = 0; index < count; index++)
        {
            var rule = Settings.Rules[index];
            var name = rule.Name.Length > 0 ? rule.Name : Strings.WindowRulesUnnamed;
            Rules.Add(new RuleItem(
                index,
                name,
                Summary(rule, index),
                rule.Enabled,
                string.Format(CultureInfo.CurrentCulture, SwitchFormat, name),
                string.Format(CultureInfo.CurrentCulture, EditFormat, name),
                string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
                index > 0,
                index < count - 1,
                IsOn));
        }

        OnPropertyChanged(nameof(HasNoRules));
        OnPropertyChanged(nameof(CanAddRule));
    }

    // What the rule does, or why it isn't used: past the limit, or with a problem.
    private static string Summary(WindowRule rule, int index)
    {
        if (index >= WindowRulesSettings.MaximumRules)
        {
            return ReadingOrder.Format(NotUsedFormat, Strings.WindowRulesProblemTooMany);
        }

        var problem = RuleMatcher.Check(rule);
        if (problem != RuleProblem.None)
        {
            return ReadingOrder.Format(NotUsedFormat, ProblemText(problem));
        }

        var actions = new List<string>();
        if (rule.Display?.KnownWhich is { } display)
        {
            actions.Add(display switch
            {
                DisplayChoice.Main => Strings.WindowRulesDisplayMain,
                DisplayChoice.UnderPointer => Strings.WindowRulesDisplayPointer,
                _ => rule.Display.BuiltIn ? Strings.WindowRulesDisplayBuiltIn : rule.Display.Name,
            });
        }

        if (rule.Place?.KnownWhich is { } place)
        {
            actions.Add(PlaceName(place));
        }

        if (rule.KnownOpen is { } open)
        {
            actions.Add(open switch
            {
                OpenChoice.Maximized => Strings.WindowRulesOpenMaximized,
                OpenChoice.Minimized => Strings.WindowRulesOpenMinimized,
                _ => Strings.WindowRulesOpenNormal,
            });
        }

        if (rule.OnTop)
        {
            actions.Add(Strings.WindowRulesSummaryOnTop);
        }

        if (rule.Opacity is { } opacity)
        {
            actions.Add(opacity.KnownWhich == OpacityChoice.Wheel
                ? Strings.WindowRulesOpacityWheel
                : string.Format(CultureInfo.CurrentCulture, OpacityFormat, opacity.Percent));
        }

        if (rule.HideFromTaskbar)
        {
            actions.Add(Strings.WindowRulesSummaryTaskbar);
        }

        if (rule.TabOf is { } tabOf)
        {
            actions.Add(string.Format(CultureInfo.CurrentCulture, TabFormat, tabOf.Name));
        }

        return ReadingOrder.Format(AppFormat, rule.App!.Name, string.Join(Strings.WindowRulesSummarySeparator, actions));
    }

    private static string PlaceName(PlaceChoice place) => place switch
    {
        PlaceChoice.Centered => Strings.WindowRulesPlaceCentered,
        PlaceChoice.LeftHalf => Strings.WindowRulesPlaceLeftHalf,
        PlaceChoice.RightHalf => Strings.WindowRulesPlaceRightHalf,
        PlaceChoice.TopHalf => Strings.WindowRulesPlaceTopHalf,
        PlaceChoice.BottomHalf => Strings.WindowRulesPlaceBottomHalf,
        PlaceChoice.TopLeft => Strings.WindowRulesPlaceTopLeft,
        PlaceChoice.TopRight => Strings.WindowRulesPlaceTopRight,
        PlaceChoice.BottomLeft => Strings.WindowRulesPlaceBottomLeft,
        PlaceChoice.BottomRight => Strings.WindowRulesPlaceBottomRight,
        PlaceChoice.WorkArea => Strings.WindowRulesPlaceWorkArea,
        PlaceChoice.Remembered => Strings.WindowRulesPlaceRemembered,
        _ => Strings.WindowRulesPlaceCustom,
    };

    private static string ProblemText(RuleProblem problem) => problem switch
    {
        RuleProblem.NoApp => Strings.WindowRulesAppMissing,
        RuleProblem.NoAction => Strings.WindowRulesNoAction,
        RuleProblem.NoTitleText => Strings.WindowRulesTitleMissing,
        RuleProblem.BadPattern => Strings.WindowRulesTitleBadPattern,
        RuleProblem.BadPlace => Strings.WindowRulesPlaceProblem,
        RuleProblem.BadDisplay => Strings.WindowRulesProblemDisplay,
        RuleProblem.BadTabOf => Strings.WindowRulesTabMissing,
        _ => Strings.WindowRulesProblemUnknown,
    };
}
