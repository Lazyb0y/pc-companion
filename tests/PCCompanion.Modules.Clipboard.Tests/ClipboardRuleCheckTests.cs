using System;
using System.Linq;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class ClipboardRuleCheckTests
{
    private const string Player = @"C:\Program Files\Example\player.exe";

    [Theory]
    [InlineData("1Z[0-9A-Z]{16}", "1Z999AA10123456784", true)]
    [InlineData("PC-[0-9]+", "see PC-1 here", false)]
    [InlineData("PC-[0-9]+", "PC-1", true)]
    [InlineData("a|ab", "ab", true)]
    [InlineData("a|b", "ab", false)]
    [InlineData("PC-[0-9]+", "PC-1\n", false)]
    public void APatternMustMatchTheWholeText(string pattern, string text, bool expected)
    {
        var regex = ClipboardRuleCheck.CompilePattern(pattern, matchCase: false, out var problem);

        Assert.Equal(PatternProblem.None, problem);
        Assert.Equal(expected, ClipboardRuleCheck.Matches(regex!, text));
    }

    [Fact]
    public void MatchCaseIsOffUnlessTheRuleTurnsItOn()
    {
        var anyCase = ClipboardRuleCheck.CompilePattern("pc-[0-9]+", matchCase: false, out _)!;
        var exactCase = ClipboardRuleCheck.CompilePattern("pc-[0-9]+", matchCase: true, out _)!;

        Assert.True(ClipboardRuleCheck.Matches(anyCase, "PC-12"));
        Assert.False(ClipboardRuleCheck.Matches(exactCase, "PC-12"));
        Assert.True(ClipboardRuleCheck.Matches(exactCase, "pc-12"));
    }

    [Fact]
    public void CaseIsComparedTheSameWayInEveryLanguage()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            var file = ClipboardRuleCheck.CompilePattern("file", matchCase: false, out _)!;

            Assert.True(ClipboardRuleCheck.Matches(file, "FILE"));
            Assert.False(ClipboardRuleCheck.Matches(file, "FİLE"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("", PatternProblem.Missing)]
    [InlineData("[0-9", PatternProblem.Invalid)]
    [InlineData("a)|(b", PatternProblem.Invalid)]
    [InlineData(@"abc\", PatternProblem.Invalid)]
    [InlineData("(?=a)a", PatternProblem.NotSupported)]
    [InlineData("(?<!b)a", PatternProblem.NotSupported)]
    [InlineData(@"(a)\1", PatternProblem.NotSupported)]
    [InlineData("(?>a+)b", PatternProblem.NotSupported)]
    public void PatternsThatCantBeUsedSayWhy(string pattern, PatternProblem expected)
    {
        Assert.Null(ClipboardRuleCheck.CompilePattern(pattern, matchCase: false, out var problem));
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void HarmfulPatternsStayFastOnTheLongestText()
    {
        var text = new string('a', 4095) + "!";
        var clock = System.Diagnostics.Stopwatch.StartNew();

        foreach (var pattern in new[] { "(a|aa)*b", "(a+)+b", @"(\w+\s?)*$" })
        {
            Assert.False(ClipboardRuleCheck.Matches(ClipboardRuleCheck.CompilePattern(pattern, false, out _)!, text));
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
    }

    [Theory]
    [InlineData("https://tracking.example.com/?number={text}", "1Z 99&9#/é", "https://tracking.example.com/?number=1Z%2099%269%23%2F%C3%A9")]
    [InlineData("http://example.com/{text}", "PC-12", "http://example.com/PC-12")]
    [InlineData("HTTPS://example.com/search?q={text}", "a b", "https://example.com/search?q=a%20b")]
    [InlineData("https://example.com/", "ignored", "https://example.com/")]
    public void TheTextIsEncodedIntoTheAddress(string address, string text, string expected)
    {
        Assert.Equal(expected, ClipboardRuleCheck.BuildAddress(address, text)?.AbsoluteUri);
    }

    [Theory]
    [InlineData("{text}", "https://example.com")]
    [InlineData("{text}", "file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:{text}", "privacy")]
    [InlineData("javascript:{text}", "alert(1)")]
    [InlineData("https://{text}", "a b")]
    public void AnAddressThatIsNoWebAddressWithTheTextIsntOpened(string address, string text)
    {
        Assert.Null(ClipboardRuleCheck.BuildAddress(address, text));
    }

    [Theory]
    [InlineData("", AddressProblem.Missing)]
    [InlineData("   ", AddressProblem.Missing)]
    [InlineData("www.example.com/{text}", AddressProblem.NotWebAddress)]
    [InlineData("file:///C:/{text}", AddressProblem.NotWebAddress)]
    [InlineData("search-ms:query={text}", AddressProblem.NotWebAddress)]
    [InlineData("https://example.com/?q={text}", AddressProblem.None)]
    public void AddressesMustBeWebAddresses(string address, AddressProblem expected)
    {
        Assert.Equal(expected, ClipboardRuleCheck.CheckAddress(address));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\POWERSHELL.EXE")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\Windows\System32\wscript.exe")]
    [InlineData(@"C:\Windows\System32\cscript.exe")]
    [InlineData(@"C:\Windows\System32\mshta.exe")]
    [InlineData(@"C:\Windows\System32\wsl.exe")]
    [InlineData(@"C:\Windows\System32\bash.exe")]
    [InlineData(@"C:\Users\Someone\AppData\Local\Microsoft\WindowsApps\wt.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Windows\System32\rundll32.exe")]
    public void CommandInterpretersDontRunFromARule(string program)
    {
        Assert.Equal(RuleProgramProblem.CommandInterpreter, ClipboardRuleCheck.CheckProgram(program, _ => true));
        Assert.Equal(RuleProgramProblem.CommandInterpreter, ClipboardRuleCheck.CheckProgram(program, _ => false));
    }

    [Theory]
    [InlineData(null, RuleProgramProblem.NotChosen)]
    [InlineData("player.exe", RuleProgramProblem.NotFullPath)]
    [InlineData(@"C:\Scripts\Open.bat", RuleProgramProblem.NotExe)]
    [InlineData(@"C:\Missing\player.exe", RuleProgramProblem.NotFound)]
    [InlineData(Player, RuleProgramProblem.None)]
    public void ProgramsFollowTheHotCornersRules(string? program, RuleProgramProblem expected)
    {
        Assert.Equal(expected, ClipboardRuleCheck.CheckProgram(program, path => path == Player));
    }

    [Theory]
    [InlineData("--fullscreen {text}", @"C:\Users\Someone\My File.txt", new[] { "--fullscreen", @"C:\Users\Someone\My File.txt" })]
    [InlineData("{text}", "say \"hi\" & del x; echo %PATH% $env:TEMP", new[] { "say \"hi\" & del x; echo %PATH% $env:TEMP" })]
    [InlineData("\"--title={text}\" --new", "two words", new[] { "--title=two words", "--new" })]
    [InlineData("", "ignored", new string[0])]
    public void TheTextStaysInsideOneArgument(string arguments, string text, string[] expected)
    {
        Assert.Equal(expected, ClipboardRuleCheck.BuildArguments(arguments, text).ToArray());
    }

    [Theory]
    [InlineData("", "x", "OpenAddress", "https://example.com/{text}", "", RuleProblem.NoName)]
    [InlineData("Name", "", "OpenAddress", "https://example.com/{text}", "", RuleProblem.NoPattern)]
    [InlineData("Name", "[x", "OpenAddress", "https://example.com/{text}", "", RuleProblem.InvalidPattern)]
    [InlineData("Name", "(?=x)x", "OpenAddress", "https://example.com/{text}", "", RuleProblem.PatternNeedsBacktracking)]
    [InlineData("Name", "x", "Translate", "https://example.com/{text}", "", RuleProblem.UnknownAction)]
    [InlineData("Name", "x", "OpenAddress", "", "", RuleProblem.NoAddress)]
    [InlineData("Name", "x", "OpenAddress", "file:///{text}", "", RuleProblem.NotWebAddress)]
    [InlineData("Name", "x", "RunProgram", "", "", RuleProblem.ProgramNotChosen)]
    [InlineData("Name", "x", "RunProgram", "", "player.exe", RuleProblem.ProgramNotFullPath)]
    [InlineData("Name", "x", "RunProgram", "", @"C:\Scripts\Open.cmd", RuleProblem.ProgramNotExe)]
    [InlineData("Name", "x", "RunProgram", "", @"C:\Windows\System32\cmd.exe", RuleProblem.CommandInterpreter)]
    [InlineData("Name", "x", "RunProgram", "", @"C:\Missing\player.exe", RuleProblem.None)]
    [InlineData("Name", "x", "OpenAddress", "https://example.com/{text}", "", RuleProblem.None)]
    public void ARuleSaysWhatKeepsItOutOfTheMenu(string name, string pattern, string action, string address, string program, RuleProblem expected)
    {
        var rule = new ClipboardRule { Name = name, Pattern = pattern, Action = action, Address = address, Program = program };

        Assert.Equal(expected, ClipboardRuleCheck.Check(rule, out var compiled));
        Assert.Equal(expected == RuleProblem.None, compiled is not null);
    }
}
