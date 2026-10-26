using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>What keeps a rule's pattern from being used.</summary>
public enum PatternProblem
{
    None,
    Missing,

    /// <summary>It isn't a valid regular expression.</summary>
    Invalid,

    /// <summary>
    /// Non-backtracking matching can't run it: it needs backtracking, such as a lookaround or a
    /// backreference, or repeats a part so many times that its matcher would be too large.
    /// </summary>
    NotSupported,
}

/// <summary>What keeps a rule's address from being opened.</summary>
public enum AddressProblem
{
    None,
    Missing,

    /// <summary>It isn't an <c>http</c> or <c>https</c> address.</summary>
    NotWebAddress,
}

/// <summary>What keeps a rule's program from starting.</summary>
public enum RuleProgramProblem
{
    None,
    NotChosen,
    NotFullPath,
    NotExe,
    NotFound,

    /// <summary>It reads its arguments as commands, such as cmd or PowerShell.</summary>
    CommandInterpreter,
}

/// <summary>What keeps a rule out of the menu.</summary>
public enum RuleProblem
{
    None,
    NoName,
    NoPattern,
    InvalidPattern,
    PatternNotSupported,
    UnknownAction,
    NoAddress,
    NotWebAddress,
    ProgramNotChosen,
    ProgramNotFullPath,
    ProgramNotExe,
    CommandInterpreter,
}

/// <summary>
/// Checks rules and builds their actions, for the host and for the rule editor. See "Rules" and
/// "Run a program" in docs/features/clipboard/clipboard-actions.md.
/// </summary>
public static class ClipboardRuleCheck
{
    /// <summary>How long a pattern may take to match before it counts as not matching.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    // Programs that read their arguments as commands: copied text, which can come from any web
    // page, would run commands of its own through them.
    private static readonly HashSet<string> CommandInterpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "wsl.exe", "bash.exe", "wt.exe", "explorer.exe", "rundll32.exe",
    };

    // Set once PrepareMatching has run.
    private static int s_matchingPrepared;

    /// <summary>
    /// Compiles a pattern that the whole text must match, as if it started with <c>^</c> and ended
    /// with <c>$</c>, without backtracking, with the invariant culture, and with
    /// <see cref="MatchTimeout"/>. Returns <see langword="null"/> when it can't be used.
    /// </summary>
    public static Regex? CompilePattern(string? pattern, bool matchCase, out PatternProblem problem)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            problem = PatternProblem.Missing;
            return null;
        }

        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            // Checked on its own first: unbalanced, the pattern could close the group that anchors it.
            _ = new Regex(pattern, options, MatchTimeout);
            var whole = new Regex($@"\A(?:{pattern})\z", options, MatchTimeout);
            PrepareMatching();
            problem = PatternProblem.None;
            return whole;
        }
        catch (NotSupportedException)
        {
            problem = PatternProblem.NotSupported;
            return null;
        }
        catch (ArgumentException)
        {
            problem = PatternProblem.Invalid;
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> matches a pattern from <see cref="CompilePattern"/>; false
    /// when matching took longer than <see cref="MatchTimeout"/>.
    /// </summary>
    public static bool Matches(Regex pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    // .NET prepares its non-backtracking matching code at the first match in a process. That takes
    // about 10 ms, counted toward MatchTimeout, and much longer on a busy PC: the first rule to
    // match could run out of time and be left out of the menu. A match of our own, without a time
    // limit, prepares it before any rule's match is timed.
    private static void PrepareMatching()
    {
        if (Interlocked.Exchange(ref s_matchingPrepared, 1) == 0)
        {
            _ = new Regex(@"\A(?:x)\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).IsMatch("x");
        }
    }

    /// <summary>Checks a rule's address, with a sample text in place of <c>{text}</c>.</summary>
    public static AddressProblem CheckAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return AddressProblem.Missing;
        }

        return BuildAddress(address, "text") is null ? AddressProblem.NotWebAddress : AddressProblem.None;
    }

    /// <summary>
    /// The address with <paramref name="text"/> in place of <c>{text}</c>, encoded for addresses so
    /// it can't change the address's other parts, or <see langword="null"/> when the result isn't an
    /// <c>http</c> or <c>https</c> address.
    /// </summary>
    public static Uri? BuildAddress(string address, string text)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(text);

        var built = address.Trim().Replace(ClipboardRule.TextPlaceholder, Uri.EscapeDataString(text), StringComparison.Ordinal);
        return Uri.TryCreate(built, UriKind.Absolute, out var result) && HostProcess.IsWebAddress(result) && result.Host.Length > 0 ? result : null;
    }

    /// <summary>
    /// Checks a rule's program: the rules of <see cref="ProgramFile.Check"/>, and no command interpreter.
    /// </summary>
    public static RuleProgramProblem CheckProgram(string? program, Func<string, bool> fileExists)
    {
        var problem = ProgramFile.Check(program, fileExists);
        if ((problem is ProgramProblem.None or ProgramProblem.NotFound) && IsCommandInterpreter(program!))
        {
            return RuleProgramProblem.CommandInterpreter;
        }

        return problem switch
        {
            ProgramProblem.NotChosen => RuleProgramProblem.NotChosen,
            ProgramProblem.NotFullPath => RuleProgramProblem.NotFullPath,
            ProgramProblem.NotExe => RuleProgramProblem.NotExe,
            ProgramProblem.NotFound => RuleProgramProblem.NotFound,
            _ => RuleProgramProblem.None,
        };
    }

    /// <summary>Whether the program reads its arguments as commands, such as cmd or PowerShell.</summary>
    public static bool IsCommandInterpreter(string program) => CommandInterpreters.Contains(Path.GetFileName(program));

    /// <summary>
    /// A rule's arguments for <paramref name="text"/>: split by the Windows command-line rules
    /// first, then with the text in place of <c>{text}</c> in each, so the text stays inside one
    /// argument, whatever spaces or quotes it holds.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string? arguments, string text) =>
        [.. CommandLine.SplitArguments(arguments).Select(argument => argument.Replace(ClipboardRule.TextPlaceholder, text, StringComparison.Ordinal))];

    /// <summary>
    /// What keeps a rule out of the menu, with its compiled pattern when nothing does. The
    /// program's file isn't looked for: that's checked when it runs.
    /// </summary>
    public static RuleProblem Check(ClipboardRule rule, out Regex? pattern)
    {
        ArgumentNullException.ThrowIfNull(rule);

        pattern = null;
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            return RuleProblem.NoName;
        }

        var compiled = CompilePattern(rule.Pattern, rule.MatchCase, out var patternProblem);
        switch (patternProblem)
        {
            case PatternProblem.Missing:
                return RuleProblem.NoPattern;
            case PatternProblem.Invalid:
                return RuleProblem.InvalidPattern;
            case PatternProblem.NotSupported:
                return RuleProblem.PatternNotSupported;
        }

        var problem = rule.KnownAction switch
        {
            ClipboardRuleAction.OpenAddress => CheckAddress(rule.Address) switch
            {
                AddressProblem.Missing => RuleProblem.NoAddress,
                AddressProblem.NotWebAddress => RuleProblem.NotWebAddress,
                _ => RuleProblem.None,
            },
            ClipboardRuleAction.RunProgram => CheckProgram(rule.Program, _ => true) switch
            {
                RuleProgramProblem.NotChosen => RuleProblem.ProgramNotChosen,
                RuleProgramProblem.NotFullPath => RuleProblem.ProgramNotFullPath,
                RuleProgramProblem.NotExe => RuleProblem.ProgramNotExe,
                RuleProgramProblem.CommandInterpreter => RuleProblem.CommandInterpreter,
                _ => RuleProblem.None,
            },
            _ => RuleProblem.UnknownAction,
        };

        pattern = problem == RuleProblem.None ? compiled : null;
        return problem;
    }
}
