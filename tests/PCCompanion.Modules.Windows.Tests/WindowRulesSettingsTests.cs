using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowRulesSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoRules()
    {
        var settings = WindowRulesSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Rules);
    }

    [Fact]
    public void ReadReadsTheSpecsExample()
    {
        var settings = WindowRulesSettings.Read(Section("""
            {
              "enabled": true,
              "rules": [
                {
                  "name": "Notes on the right",
                  "enabled": true,
                  "app": { "name": "Notepad", "appId": "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" },
                  "title": { "match": "Contains", "text": "Notes" },
                  "display": { "which": "Chosen", "name": "DELL U2720Q", "model": "DEL41A3", "serial": "4c1f9e0b2a7d3e85", "builtIn": false },
                  "place": { "which": "RightHalf" },
                  "open": "Normal",
                  "onTop": true,
                  "opacity": { "which": "Fixed", "percent": 90 },
                  "applyOnDisplayChange": false
                },
                {
                  "name": "Explorer in one group",
                  "enabled": true,
                  "app": { "name": "File Explorer", "program": "C:\\Windows\\explorer.exe" },
                  "windowClass": "CabinetWClass",
                  "tabOf": { "name": "File Explorer", "program": "C:\\Windows\\explorer.exe" }
                },
                {
                  "name": "Music where I left it",
                  "enabled": true,
                  "app": { "name": "Music player", "program": "C:\\Program Files\\Music\\Music.exe" },
                  "place": { "which": "Remembered" },
                  "hideFromTaskbar": true,
                  "opacity": { "which": "Wheel" }
                }
              ]
            }
            """));

        Assert.True(settings.Enabled);
        Assert.Equal(3, settings.Rules.Count);
        var notes = settings.Rules[0];
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", notes.App!.AppId);
        Assert.Equal(TitleMatch.Contains, notes.Title!.KnownMatch);
        Assert.Equal(DisplayChoice.Chosen, notes.Display!.KnownWhich);
        Assert.Equal(new KnownDisplay("DEL41A3", "4c1f9e0b2a7d3e85", string.Empty, false), notes.Display.AsKnown);
        Assert.Equal(PlaceChoice.RightHalf, notes.Place!.KnownWhich);
        Assert.Equal(OpenChoice.Normal, notes.KnownOpen);
        Assert.True(notes.OnTop);
        Assert.Equal(90, notes.Opacity!.Percent);
        Assert.Equal("CabinetWClass", settings.Rules[1].WindowClass);
        Assert.Equal(@"C:\Windows\explorer.exe", settings.Rules[1].TabOf!.Program);
        Assert.Equal(PlaceChoice.Remembered, settings.Rules[2].Place!.KnownWhich);
        Assert.Equal(OpacityChoice.Wheel, settings.Rules[2].Opacity!.KnownWhich);
        Assert.True(settings.Rules[2].HideFromTaskbar);
        Assert.All(settings.Rules, rule => Assert.Equal(RuleProblem.None, RuleMatcher.Check(rule)));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new WindowRulesSettings
        {
            Enabled = true,
            Rules =
            [
                new WindowRule
                {
                    Name = "Mail",
                    Enabled = false,
                    App = new ListedApp { Name = "Mail", Program = @"C:\Mail\Mail.exe" },
                    Title = new RuleTitle { Match = nameof(TitleMatch.Pattern), Text = "^Inbox" },
                    Display = new RuleDisplay { Which = nameof(DisplayChoice.UnderPointer) },
                    Place = new RulePlace { Which = nameof(PlaceChoice.Custom), Left = 10, Top = 12.5, Width = 50, Height = 75 },
                    Open = nameof(OpenChoice.Maximized),
                    Opacity = new RuleOpacity { Percent = 60 },
                    HideFromTaskbar = true,
                    ApplyOnDisplayChange = true,
                },
            ],
        };

        var read = WindowRulesSettings.Read(settings.Write());

        Assert.Equal(settings, read);
        Assert.False(read.Rules[0].Enabled);
    }

    [Fact]
    public void ActionsThatAreOffAreLeftOutOfTheFile()
    {
        var settings = new WindowRulesSettings { Rules = [new WindowRule { Name = "Top", App = new ListedApp { Program = @"C:\A.exe" }, OnTop = true }] };

        var rule = settings.Write().GetProperty("rules")[0];

        Assert.False(rule.TryGetProperty("place", out _));
        Assert.False(rule.TryGetProperty("open", out _));
        Assert.True(rule.GetProperty("onTop").GetBoolean());
        Assert.True(rule.TryGetProperty("enabled", out _));
    }

    [Fact]
    public void ANullRuleIsLeftOutAndBlankAppsCountAsNone()
    {
        var settings = WindowRulesSettings.Read(Section("""
            { "rules": [ null, { "app": { "name": "A", "appId": " ", "program": "C:\\A.exe" }, "windowClass": " ", "onTop": true } ] }
            """));

        var rule = Assert.Single(settings.Rules);
        Assert.Null(rule.App!.AppId);
        Assert.Equal(@"C:\A.exe", rule.App.Program);
        Assert.Null(rule.WindowClass);
    }

    [Fact]
    public void UnknownChoicesAreKeptAsWritten()
    {
        var settings = WindowRulesSettings.Read(Section("""
            { "rules": [ { "app": { "program": "C:\\A.exe" }, "place": { "which": "Spiral" }, "open": "FullScreen" } ] }
            """));

        var rule = settings.Rules[0];
        Assert.Null(rule.Place!.KnownWhich);
        Assert.Null(rule.KnownOpen);
        Assert.Contains("Spiral", settings.Write().GetRawText(), System.StringComparison.Ordinal);
        Assert.Equal(RuleProblem.UnknownChoice, RuleMatcher.Check(rule));
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        Assert.Throws<JsonException>(() => WindowRulesSettings.Read(Section("""{ "rules": [ { "onTop": "yes" } ] }""")));
    }

    [Fact]
    public void WithEnabledChangesOnlyEnabled()
    {
        var settings = new WindowRulesSettings { Rules = [new WindowRule { Name = "A" }] };

        var on = settings.WithEnabled(true);

        Assert.True(on.Enabled);
        Assert.Equal("A", on.Rules.Single().Name);
    }
}
