using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>Which rules matched a window, and which wait for its title.</summary>
/// <param name="Matched">The numbers in the list of the rules that match the window, in order.</param>
/// <param name="Waiting">The numbers of the rules whose app and class match, but not yet the title.</param>
/// <param name="TimedOut">The numbers of the rules whose title pattern took too long, which don't match.</param>
public sealed record RuleMatch(IReadOnlyList<int> Matched, IReadOnlyList<int> Waiting, IReadOnlyList<int> TimedOut)
{
    public static RuleMatch None { get; } = new([], [], []);
}

/// <summary>
/// The rules in use, up to <see cref="WindowRulesSettings.MaximumRules"/>, ready to match windows.
/// See "Rules" and "Matching" in docs/features/windows/window-rules.md.
/// </summary>
public sealed class RuleSet
{
    public RuleSet(IReadOnlyList<WindowRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Matchers = [.. rules.Take(WindowRulesSettings.MaximumRules).Select(rule => new RuleMatcher(rule))];
        Rules = [.. Matchers.Select(matcher => matcher.Rule)];
        LeftOut = Math.Max(0, rules.Count - WindowRulesSettings.MaximumRules);
    }

    public static RuleSet Empty { get; } = new([]);

    public IReadOnlyList<RuleMatcher> Matchers { get; }

    /// <summary>The rules past the limit, which aren't used.</summary>
    public int LeftOut { get; }

    /// <summary>The rules as written, in the list's order.</summary>
    public IReadOnlyList<WindowRule> Rules { get; }

    /// <summary>Whether any rule is on and can be used.</summary>
    public bool HasUsable => Matchers.Any(matcher => matcher.IsUsable);

    /// <summary>The numbers of the rules that are on but can't be used, with why.</summary>
    public IEnumerable<(int Index, RuleProblem Problem)> Problems =>
        Matchers.Select((matcher, index) => (index, matcher)).Where(pair => pair.matcher.Rule.Enabled && pair.matcher.Problem != RuleProblem.None)
            .Select(pair => (pair.index, pair.matcher.Problem));

    /// <summary>Whether any usable rule's app and class are the window's, whatever its title.</summary>
    public bool AnyApp(WindowFacts window) => Matchers.Any(matcher => matcher.IsUsable && matcher.MatchesApp(window));

    /// <summary>
    /// Which usable rules match <paramref name="window"/>, and which match all but its title. Only
    /// the rules at <paramref name="only"/> are checked, when given, as for the ones still waiting.
    /// </summary>
    public RuleMatch Match(WindowFacts window, IReadOnlyCollection<int>? only = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        List<int>? matched = null;
        List<int>? waiting = null;
        List<int>? timedOut = null;
        for (var index = 0; index < Matchers.Count; index++)
        {
            var matcher = Matchers[index];
            if ((only is not null && !only.Contains(index)) || !matcher.IsUsable || !matcher.MatchesApp(window))
            {
                continue;
            }

            switch (matcher.MatchesTitle(window.Title))
            {
                case TitleResult.Matches:
                    (matched ??= []).Add(index);
                    break;
                case TitleResult.DoesNotMatch:
                    (waiting ??= []).Add(index);
                    break;
                default:
                    (timedOut ??= []).Add(index);
                    break;
            }
        }

        return matched is null && waiting is null && timedOut is null
            ? RuleMatch.None
            : new RuleMatch(matched ?? [], waiting ?? [], timedOut ?? []);
    }

    /// <summary>The actions of the rules at <paramref name="matched"/>, combined.</summary>
    public RuleOutcome Combine(IReadOnlyList<int> matched) => RuleOutcome.Combine(Rules, matched);
}
