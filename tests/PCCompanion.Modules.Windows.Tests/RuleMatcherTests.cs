using System.Linq;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class RuleMatcherTests
{
    private static readonly ListedApp Notepad = new() { Name = "Notepad", AppId = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" };
    private static readonly ListedApp Mail = new() { Name = "Mail", Program = @"C:\Mail\Mail.exe" };

    [Fact]
    public void ARuleMatchesItsAppByAppIdBeforeItsProgram()
    {
        var byAppId = new RuleMatcher(new WindowRule { App = Notepad, OnTop = true });
        var byProgram = new RuleMatcher(new WindowRule { App = Mail, OnTop = true });

        Assert.True(byAppId.MatchesApp(Window(new AppOfWindow(null, Notepad.AppId, @"C:\Notepad.exe", 1))));
        Assert.True(byProgram.MatchesApp(Window(new AppOfWindow(null, null, @"c:\mail\MAIL.exe", 1))));
        Assert.False(byProgram.MatchesApp(Window(new AppOfWindow("Other.App", null, @"C:\Other.exe", 1))));
    }

    [Fact]
    public void AClassMustBeTheWindowsWhateverItsCase()
    {
        var matcher = new RuleMatcher(new WindowRule { App = Mail, WindowClass = "MailWindow", OnTop = true });

        Assert.True(matcher.MatchesApp(Window(MailApp, className: "mailwindow")));
        Assert.False(matcher.MatchesApp(Window(MailApp, className: "MailDialog")));
    }

    [Theory]
    [InlineData("Contains", "inbox", "Inbox - Mail", TitleResult.Matches)]
    [InlineData("Contains", "Sent", "Inbox - Mail", TitleResult.DoesNotMatch)]
    [InlineData("Is", "inbox - mail", "Inbox - Mail", TitleResult.Matches)]
    [InlineData("Is", "Inbox", "Inbox - Mail", TitleResult.DoesNotMatch)]
    [InlineData("Pattern", "^inbox\\b", "Inbox - Mail", TitleResult.Matches)]
    [InlineData("Pattern", "Mail$", "Mail - Inbox", TitleResult.DoesNotMatch)]
    public void TitlesAreComparedWithTheirCaseIgnored(string match, string text, string title, TitleResult expected)
    {
        var matcher = new RuleMatcher(new WindowRule { App = Mail, Title = new RuleTitle { Match = match, Text = text }, OnTop = true });

        Assert.Equal(expected, matcher.MatchesTitle(title));
    }

    [Fact]
    public void ARuleWithoutATitleMatchesAnyTitle()
    {
        Assert.Equal(TitleResult.Matches, new RuleMatcher(new WindowRule { App = Mail, OnTop = true }).MatchesTitle(string.Empty));
    }

    [Theory]
    [InlineData("(", RuleProblem.BadPattern)]
    [InlineData("(?<=a)b", RuleProblem.BadPattern)]
    [InlineData("", RuleProblem.NoTitleText)]
    public void AnInvalidPatternOrAnEmptyTitleKeepsTheRuleFromBeingUsed(string text, RuleProblem expected)
    {
        var matcher = new RuleMatcher(new WindowRule { App = Mail, Title = new RuleTitle { Match = "Pattern", Text = text }, OnTop = true });

        Assert.Equal(expected, matcher.Problem);
        Assert.False(matcher.IsUsable);
    }

    [Fact]
    public void ARuleNeedsAnAppAndAnAction()
    {
        Assert.Equal(RuleProblem.NoApp, RuleMatcher.Check(new WindowRule { OnTop = true }));
        Assert.Equal(RuleProblem.NoApp, RuleMatcher.Check(new WindowRule { App = new ListedApp { AppId = "A", Program = @"C:\A.exe" }, OnTop = true }));
        Assert.Equal(RuleProblem.NoAction, RuleMatcher.Check(new WindowRule { App = Mail }));
        Assert.Equal(RuleProblem.None, RuleMatcher.Check(new WindowRule { App = Mail, HideFromTaskbar = true }));
    }

    [Theory]
    [InlineData(0, 0, 0, 50)]
    [InlineData(-1, 0, 50, 50)]
    [InlineData(100, 0, 50, 50)]
    [InlineData(0, 0, 101, 50)]
    [InlineData(double.NaN, 0, 50, 50)]
    public void ACustomPlaceMustBeInsideTheWorkArea(double left, double top, double width, double height)
    {
        var rule = new WindowRule { App = Mail, Place = new RulePlace { Which = "Custom", Left = left, Top = top, Width = width, Height = height } };

        Assert.Equal(RuleProblem.BadPlace, RuleMatcher.Check(rule));
    }

    [Fact]
    public void AChosenDisplayNeedsAModelOrAConnection()
    {
        Assert.Equal(RuleProblem.BadDisplay, RuleMatcher.Check(new WindowRule { App = Mail, Display = new RuleDisplay { Which = "Chosen" } }));
        Assert.Equal(RuleProblem.None, RuleMatcher.Check(new WindowRule { App = Mail, Display = new RuleDisplay { Which = "Chosen", Connection = @"\\?\DISPLAY#X" } }));
    }

    [Fact]
    public void OpenAsATabOfNeedsAnApp()
    {
        Assert.Equal(RuleProblem.BadTabOf, RuleMatcher.Check(new WindowRule { App = Mail, TabOf = new ListedApp { Name = "Nothing" } }));
    }

    [Fact]
    public void ARuleThatIsOffIsNotUsable()
    {
        Assert.False(new RuleMatcher(new WindowRule { App = Mail, OnTop = true, Enabled = false }).IsUsable);
    }

    [Fact]
    public void TheSetSaysWhichRulesMatchAndWhichWaitForTheTitle()
    {
        var set = new RuleSet(
        [
            new WindowRule { App = Mail, Title = new RuleTitle { Text = "Inbox" }, OnTop = true },
            new WindowRule { App = Mail, HideFromTaskbar = true },
            new WindowRule { App = Notepad, OnTop = true },
            new WindowRule { App = Mail, OnTop = true, Enabled = false },
        ]);

        var match = set.Match(Window(MailApp, title: "Mail"));

        Assert.Equal([1], match.Matched);
        Assert.Equal([0], match.Waiting);
        Assert.Equal([0], set.Match(Window(MailApp, title: "Inbox - Mail"), only: [0]).Matched);
        Assert.Same(RuleMatch.None, set.Match(Window(new AppOfWindow(null, null, @"C:\Other.exe", 1))));
    }

    [Fact]
    public void RulesPastTheLimitAreLeftOut()
    {
        var set = new RuleSet([.. Enumerable.Range(0, WindowRulesSettings.MaximumRules + 3).Select(_ => new WindowRule { App = Mail, OnTop = true })]);

        Assert.Equal(WindowRulesSettings.MaximumRules, set.Matchers.Count);
        Assert.Equal(3, set.LeftOut);
    }

    [Fact]
    public void ActionsCombineAndTheFirstRuleThatSetsEachWins()
    {
        var right = new RulePlace { Which = "RightHalf" };
        var left = new RulePlace { Which = "LeftHalf" };
        var rules = new[]
        {
            new WindowRule { App = Mail, Place = right, OnTop = true },
            new WindowRule { App = Mail, Place = left, Opacity = new RuleOpacity { Percent = 70 }, Open = "Maximized" },
            new WindowRule { App = Mail, Opacity = new RuleOpacity { Percent = 30 }, HideFromTaskbar = true, ApplyOnDisplayChange = true },
        };

        var outcome = RuleOutcome.Combine(rules, [0, 1, 2]);

        Assert.Same(right, outcome.Place);
        Assert.Equal(0, outcome.PlaceRule);
        Assert.Equal(70, outcome.Opacity!.Percent);
        Assert.Equal(1, outcome.OpacityRule);
        Assert.Equal(OpenChoice.Maximized, outcome.Open);
        Assert.True(outcome.OnTop);
        Assert.True(outcome.HideFromTaskbar);
        Assert.True(outcome.ApplyOnDisplayChange);
        Assert.True(outcome.Places);
        Assert.True(outcome.Holds);
        Assert.Equal([0, 1, 2], outcome.Rules);
    }

    private static readonly AppOfWindow MailApp = new(null, null, @"C:\Mail\Mail.exe", 7);

    private static WindowFacts Window(AppOfWindow app, string title = "Window", string className = "AppWindow") => new(app, title, className);
}
