using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>What an item of the menu does.</summary>
public enum OfferKind
{
    /// <summary>Open in browser, for a copied web link.</summary>
    OpenLink,

    OpenAddress,
    RunProgram,
}

/// <summary>An action the menu offers for the copied text, ready to run.</summary>
/// <param name="Name">The rule's name; empty for Open in browser.</param>
/// <param name="Rule">The rule's number in the list, from 1, or 0 for Open in browser.</param>
/// <param name="Address">The address to open, for <see cref="OfferKind.OpenLink"/> and <see cref="OfferKind.OpenAddress"/>.</param>
/// <param name="Program">The program to start, for <see cref="OfferKind.RunProgram"/>.</param>
/// <param name="Arguments">The program's arguments, with the text in place.</param>
public sealed record ClipboardOffer(OfferKind Kind, string Name, int Rule, Uri? Address = null, string Program = "", IReadOnlyList<string>? Arguments = null);

/// <summary>
/// The rules that can be used, compiled, and the actions they offer for a copied text. See
/// "Rules" in docs/features/clipboard/clipboard-actions.md.
/// </summary>
public sealed partial class ClipboardRules
{
    private readonly bool _openLinks;
    private readonly IReadOnlyList<CompiledRule> _rules;
    private readonly ILogger _logger;

    private ClipboardRules(bool openLinks, IReadOnlyList<CompiledRule> rules, ILogger logger)
    {
        _openLinks = openLinks;
        _rules = rules;
        _logger = logger;
    }

    /// <summary>How many rules can be used.</summary>
    public int Count => _rules.Count;

    /// <summary>
    /// Compiles the rules that can be used, up to <see cref="ClipboardActionsSettings.MaximumRules"/>,
    /// and logs a warning, with its number, for each that can't.
    /// </summary>
    public static ClipboardRules Compile(ClipboardActionsSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        var rules = new List<CompiledRule>();
        for (var index = 0; index < settings.Rules.Count; index++)
        {
            var number = index + 1;
            if (number > ClipboardActionsSettings.MaximumRules)
            {
                LogTooManyRules(logger, settings.Rules.Count, ClipboardActionsSettings.MaximumRules);
                break;
            }

            var rule = settings.Rules[index];
            var problem = ClipboardRuleCheck.Check(rule, out var pattern);
            if (problem != RuleProblem.None)
            {
                LogUnusableRule(logger, number, problem);
                continue;
            }

            rules.Add(new CompiledRule(number, rule, pattern!));
        }

        return new ClipboardRules(settings.OpenLinks, rules, logger);
    }

    /// <summary>
    /// The web link that <paramref name="text"/> is, or <see langword="null"/>: one <c>http</c> or
    /// <c>https</c> address, with no spaces or control characters.
    /// </summary>
    public static Uri? FindLink(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var link) && HostProcess.IsWebAddress(link) && link.Host.Length > 0 ? link : null;
    }

    /// <summary>
    /// The actions for <paramref name="text"/>, the copied text with spaces and line breaks at its
    /// ends removed: Open in browser for a web link, then the matching rules' actions, in the order
    /// of the list.
    /// </summary>
    public IReadOnlyList<ClipboardOffer> Match(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var offers = new List<ClipboardOffer>();
        if (_openLinks && FindLink(text) is { } link)
        {
            offers.Add(new ClipboardOffer(OfferKind.OpenLink, string.Empty, 0, link));
        }

        foreach (var (number, rule, pattern) in _rules)
        {
            if (!ClipboardRuleCheck.Matches(pattern, text))
            {
                continue;
            }

            if (rule.KnownAction == ClipboardRuleAction.RunProgram)
            {
                offers.Add(new ClipboardOffer(OfferKind.RunProgram, rule.Name, number, Program: rule.Program, Arguments: ClipboardRuleCheck.BuildArguments(rule.Arguments, text)));
            }
            else if (ClipboardRuleCheck.BuildAddress(rule.Address, text) is { } address)
            {
                offers.Add(new ClipboardOffer(OfferKind.OpenAddress, rule.Name, number, address));
            }
            else
            {
                LogNoAddressForText(_logger, number);
            }
        }

        return offers;
    }

    private sealed record CompiledRule(int Number, ClipboardRule Rule, Regex Pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions rule {Number} can't be used: {Problem}.")]
    private static partial void LogUnusableRule(ILogger logger, int number, RuleProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions has {Count} rules and uses the first {Maximum}.")]
    private static partial void LogTooManyRules(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions rule {Number} matched, but its address with the copied text isn't a web address.")]
    private static partial void LogNoAddressForText(ILogger logger, int number);
}
