using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>What the conditions read of a file, from its folder's listing.</summary>
/// <param name="Name">The file's name, with its extension.</param>
/// <param name="Size">The size in bytes.</param>
public sealed record FileFacts(string Name, long Size, DateTimeOffset Created, DateTimeOffset Modified)
{
    /// <summary>The name without the extension.</summary>
    public string BaseName => Path.GetFileNameWithoutExtension(Name);

    /// <summary>The extension without the dot, or an empty text for a file without one.</summary>
    public string Extension => Path.GetExtension(Name) is { Length: > 1 } extension ? extension[1..] : string.Empty;
}

/// <summary>A condition of a rule, in the order the rule editor shows them.</summary>
public enum RuleCondition
{
    Name,
    Type,
    Size,
    Date,
    DownloadedFrom,
}

/// <summary>Whether a file matches one of a rule's conditions, for the rule editor.</summary>
public sealed record ConditionResult(RuleCondition Condition, bool Matches);

/// <summary>
/// A rule's conditions, ready to match files. Only a rule without a problem
/// (<see cref="FolderRuleCheck.Check"/>) is matched. See "Conditions" in docs/features/files/folder-rules.md.
/// </summary>
public sealed class RuleMatcher
{
    private readonly string[] _names;
    private readonly HashSet<string> _extensions;
    private readonly long? _largerThan;
    private readonly long? _smallerThan;
    private readonly DateCondition? _date;
    private readonly string[] _sites;

    private RuleMatcher(int number, FolderRule rule)
    {
        Number = number;
        Rule = rule;
        _names = [.. rule.Names.Select(name => name.Trim())];
        _extensions = new HashSet<string>(rule.Extensions.Select(FolderRuleCheck.TrimExtension), StringComparer.OrdinalIgnoreCase);
        _largerThan = Bytes(rule.Size?.LargerThan);
        _smallerThan = Bytes(rule.Size?.SmallerThan);
        _date = rule.Date;
        _sites = [.. rule.DownloadedFrom.Select(site => site.Trim())];
    }

    /// <summary>The rule's number in its folder's list, from 1.</summary>
    public int Number { get; }

    public FolderRule Rule { get; }

    /// <param name="number">The rule's number in its folder's list, from 1.</param>
    public static RuleMatcher Create(FolderRule rule, int number)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new RuleMatcher(number, rule);
    }

    /// <summary>
    /// The first of <paramref name="rules"/> whose conditions all match the file, or
    /// <see langword="null"/>. The Mark of the Web is read once at most, and only for a rule
    /// whose other conditions match.
    /// </summary>
    /// <param name="readMark">Reads the file's Mark of the Web; <see langword="null"/> when it has none.</param>
    public static RuleMatcher? FirstMatch(IEnumerable<RuleMatcher> rules, FileFacts file, DateTimeOffset now, Func<MarkOfTheWeb?> readMark)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(readMark);

        var mark = new Lazy<MarkOfTheWeb?>(readMark);
        return rules.FirstOrDefault(rule => rule.Matches(file, now, () => mark.Value));
    }

    /// <summary>Whether the file matches all the rule's conditions, reading the mark only when needed.</summary>
    public bool Matches(FileFacts file, DateTimeOffset now, Func<MarkOfTheWeb?> readMark)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(readMark);

        return MatchesName(file) && MatchesType(file) && MatchesSize(file) && MatchesDate(file, now) && MatchesSite(readMark);
    }

    /// <summary>Whether the file matches each condition that's on, for the rule editor.</summary>
    public IReadOnlyList<ConditionResult> Explain(FileFacts file, DateTimeOffset now, Func<MarkOfTheWeb?> readMark)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(readMark);

        var results = new List<ConditionResult>();
        if (_names.Length > 0)
        {
            results.Add(new ConditionResult(RuleCondition.Name, MatchesName(file)));
        }

        if (_extensions.Count > 0)
        {
            results.Add(new ConditionResult(RuleCondition.Type, MatchesType(file)));
        }

        if (Rule.Size is not null)
        {
            results.Add(new ConditionResult(RuleCondition.Size, MatchesSize(file)));
        }

        if (_date is not null)
        {
            results.Add(new ConditionResult(RuleCondition.Date, MatchesDate(file, now)));
        }

        if (_sites.Length > 0)
        {
            results.Add(new ConditionResult(RuleCondition.DownloadedFrom, MatchesSite(readMark)));
        }

        return results;
    }

    /// <summary>The number of bytes in <paramref name="size"/>, with 1 KB as 1,024 bytes, as File Explorer counts.</summary>
    public static long? Bytes(SizeLimit? size) =>
        size is null ? null : size.Amount * (1L << (10 * ((int)size.Unit + 1)));

    private bool MatchesName(FileFacts file) =>
        _names.Length == 0 || _names.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true));

    private bool MatchesType(FileFacts file) => _extensions.Count == 0 || _extensions.Contains(file.Extension);

    private bool MatchesSize(FileFacts file) =>
        (_largerThan is not { } larger || file.Size > larger) && (_smallerThan is not { } smaller || file.Size < smaller);

    private bool MatchesDate(FileFacts file, DateTimeOffset now)
    {
        if (_date is null)
        {
            return true;
        }

        var age = now - (_date.Which == FileDate.Created ? file.Created : file.Modified);
        return (_date.MoreThanDaysAgo is not { } more || age > TimeSpan.FromDays(more))
            && (_date.LessThanDaysAgo is not { } less || age < TimeSpan.FromDays(less));
    }

    // The site's name is matched as written and in its ASCII form, so that a pattern in either
    // form matches a name in other letters, such as münchen.example.
    private bool MatchesSite(Func<MarkOfTheWeb?> readMark)
    {
        if (_sites.Length == 0)
        {
            return true;
        }

        if (readMark()?.Source is not { } source)
        {
            return false;
        }

        return _sites.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(pattern, source.Host, ignoreCase: true)
            || FileSystemName.MatchesSimpleExpression(pattern, source.IdnHost, ignoreCase: true));
    }
}
