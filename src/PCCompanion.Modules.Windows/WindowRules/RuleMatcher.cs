using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>Why a rule can't be used, for the host's log and the rule editor.</summary>
public enum RuleProblem
{
    None,

    /// <summary>The rule names no app, or one with neither or both an app ID and a program.</summary>
    NoApp,

    /// <summary>Every action is off.</summary>
    NoAction,

    /// <summary>The title condition has no text.</summary>
    NoTitleText,

    /// <summary>The title's regular expression isn't valid, or uses what .NET can't match without backtracking.</summary>
    BadPattern,

    /// <summary>A custom place with a size of zero, or edges outside the work area.</summary>
    BadPlace,

    /// <summary>A chosen display with neither a model nor a connection to recognize it by.</summary>
    BadDisplay,

    /// <summary>Open as a tab of an app with neither or both an app ID and a program.</summary>
    BadTabOf,

    /// <summary>A choice this version doesn't know, such as one from a newer version.</summary>
    UnknownChoice,
}

/// <summary>What a rule matches a window by: its app, its title and its class.</summary>
/// <param name="App">The window's app, as the taskbar groups apps.</param>
/// <param name="Title">The window's title, read without a message.</param>
/// <param name="ClassName">The class name the window's app registered.</param>
public sealed record WindowFacts(AppOfWindow App, string Title, string ClassName);

/// <summary>Whether a window's title matches a rule's.</summary>
public enum TitleResult
{
    Matches,
    DoesNotMatch,

    /// <summary>The regular expression took longer than <see cref="RuleMatcher.MatchTimeout"/>, which counts as not matching.</summary>
    TimedOut,
}

/// <summary>
/// One rule, ready to match windows, or why it can't be used. See "Matching" in
/// docs/features/windows/window-rules.md. It holds no window and can be used on any one thread.
/// </summary>
public sealed class RuleMatcher
{
    /// <summary>How long a title pattern may take to match before it counts as not matching.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex? _pattern;

    public RuleMatcher(WindowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Rule = rule;
        Problem = Check(rule, out _pattern);
    }

    public WindowRule Rule { get; }

    /// <summary>Why the rule can't be used, or <see cref="RuleProblem.None"/>.</summary>
    public RuleProblem Problem { get; }

    /// <summary>Whether the rule is on and can be used.</summary>
    public bool IsUsable => Rule.Enabled && Problem == RuleProblem.None;

    /// <summary>Whether the rule has a title condition, which a window may meet only once its app sets the title.</summary>
    public bool HasTitle => Rule.Title is not null;

    /// <summary>
    /// Why <paramref name="rule"/> can't be used, or <see cref="RuleProblem.None"/>, for the rule
    /// editor; the first problem found, in the order the editor shows its boxes.
    /// </summary>
    public static RuleProblem Check(WindowRule rule) => Check(rule, out _);

    /// <summary>Whether the window's app and class are the rule's; the title is checked apart.</summary>
    public bool MatchesApp(WindowFacts window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Rule.App is { } app && app.Matches(window.App)
            && (Rule.WindowClass is not { } className || string.Equals(className, window.ClassName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether <paramref name="title"/> meets the rule's title condition; a rule without one matches any title.</summary>
    public TitleResult MatchesTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (Rule.Title is not { } condition)
        {
            return TitleResult.Matches;
        }

        if (_pattern is not null)
        {
            try
            {
                return _pattern.IsMatch(title) ? TitleResult.Matches : TitleResult.DoesNotMatch;
            }
            catch (RegexMatchTimeoutException)
            {
                return TitleResult.TimedOut;
            }
        }

        var matches = condition.KnownMatch == TitleMatch.Is
            ? string.Equals(title, condition.Text, StringComparison.OrdinalIgnoreCase)
            : title.Contains(condition.Text, StringComparison.OrdinalIgnoreCase);
        return matches ? TitleResult.Matches : TitleResult.DoesNotMatch;
    }

    private static RuleProblem Check(WindowRule rule, out Regex? pattern)
    {
        pattern = null;
        if (rule.App is not { IsValid: true })
        {
            return RuleProblem.NoApp;
        }

        if (rule.Title is { } title)
        {
            if (title.KnownMatch is not { } match)
            {
                return RuleProblem.UnknownChoice;
            }

            if (string.IsNullOrEmpty(title.Text))
            {
                return RuleProblem.NoTitleText;
            }

            if (match == TitleMatch.Pattern)
            {
                pattern = Compile(title.Text);
                if (pattern is null)
                {
                    return RuleProblem.BadPattern;
                }
            }
        }

        if (!rule.HasAction)
        {
            return RuleProblem.NoAction;
        }

        if ((rule.Display is { KnownWhich: null }) || (rule.Place is { KnownWhich: null }) || (rule.Open is not null && rule.KnownOpen is null)
            || (rule.Opacity is { KnownWhich: null }))
        {
            return RuleProblem.UnknownChoice;
        }

        if (rule.Display is { KnownWhich: DisplayChoice.Chosen } display && string.IsNullOrWhiteSpace(display.Model) && string.IsNullOrWhiteSpace(display.Connection))
        {
            return RuleProblem.BadDisplay;
        }

        if (rule.Place is { KnownWhich: PlaceChoice.Custom } place && !IsCustomPlace(place))
        {
            return RuleProblem.BadPlace;
        }

        return rule.TabOf is { IsValid: false } ? RuleProblem.BadTabOf : RuleProblem.None;
    }

    // Percent of the work area: an edge inside it, a size above zero, and no more than all of it.
    private static bool IsCustomPlace(RulePlace place) =>
        double.IsFinite(place.Left) && double.IsFinite(place.Top) && double.IsFinite(place.Width) && double.IsFinite(place.Height)
        && place.Left is >= 0 and < 100 && place.Top is >= 0 and < 100 && place.Width is > 0 and <= 100 && place.Height is > 0 and <= 100;

    // Anywhere in the title, without backtracking, so no pattern can take long, and with the
    // invariant culture, whatever the user's language.
    private static Regex? Compile(string text)
    {
        try
        {
            return new Regex(text, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// The actions a window gets from all the rules that match it, combined action by action: for each,
/// the first rule in the list that sets it wins. See "Several rules" in
/// docs/features/windows/window-rules.md.
/// </summary>
public sealed record RuleOutcome
{
    public RuleDisplay? Display { get; init; }

    public RulePlace? Place { get; init; }

    /// <summary>The number in the list of the rule whose place this is, for a remembered place.</summary>
    public int PlaceRule { get; init; } = -1;

    /// <summary>The key of the rule whose place this is, which a remembered place is kept by.</summary>
    public string? PlaceKey { get; init; }

    public OpenChoice? Open { get; init; }

    public bool OnTop { get; init; }

    public RuleOpacity? Opacity { get; init; }

    /// <summary>The number in the list of the rule whose opacity this is, for an opacity from the wheel.</summary>
    public int OpacityRule { get; init; } = -1;

    /// <summary>The key of the rule whose opacity this is, which an opacity from the wheel is kept by.</summary>
    public string? OpacityKey { get; init; }

    public bool HideFromTaskbar { get; init; }

    public ListedApp? TabOf { get; init; }

    /// <summary>Whether a rule that matched places windows again after the displays change.</summary>
    public bool ApplyOnDisplayChange { get; init; }

    /// <summary>The numbers in the list of the rules that matched, in order.</summary>
    public IReadOnlyList<int> Rules { get; init; } = [];

    /// <summary>Whether the window is placed: a display, a place or how it opens.</summary>
    public bool Places => Display is not null || Place is not null || Open is not null;

    /// <summary>Whether the window gets an action that holds while it's open.</summary>
    public bool Holds => OnTop || Opacity is not null || HideFromTaskbar;

    /// <summary>
    /// Combines the rules at <paramref name="matched"/>, numbers in <paramref name="rules"/> in the
    /// list's order: for each action, the first that sets it wins.
    /// </summary>
    public static RuleOutcome Combine(IReadOnlyList<WindowRule> rules, IReadOnlyList<int> matched)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(matched);

        var outcome = new RuleOutcome { Rules = matched };
        foreach (var index in matched)
        {
            var rule = rules[index];
            outcome = outcome with
            {
                Display = outcome.Display ?? rule.Display,
                Place = outcome.Place ?? rule.Place,
                PlaceRule = outcome.Place is null && rule.Place is not null ? index : outcome.PlaceRule,
                PlaceKey = outcome.Place is null && rule.Place is not null ? RememberedPlaces.KeyOf(rule) : outcome.PlaceKey,
                Open = outcome.Open ?? rule.KnownOpen,
                OnTop = outcome.OnTop || rule.OnTop,
                Opacity = outcome.Opacity ?? rule.Opacity,
                OpacityRule = outcome.Opacity is null && rule.Opacity is not null ? index : outcome.OpacityRule,
                OpacityKey = outcome.Opacity is null && rule.Opacity is not null ? RememberedPlaces.KeyOf(rule) : outcome.OpacityKey,
                HideFromTaskbar = outcome.HideFromTaskbar || rule.HideFromTaskbar,
                TabOf = outcome.TabOf ?? rule.TabOf,
                ApplyOnDisplayChange = outcome.ApplyOnDisplayChange || rule.ApplyOnDisplayChange,
            };
        }

        return outcome;
    }
}
