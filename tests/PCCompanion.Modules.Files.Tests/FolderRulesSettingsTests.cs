using System.Text.Json;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

public sealed class FolderRulesSettingsTests
{
    private const string Example = """
        {
          "enabled": true,
          "folders": [
            {
              "path": "C:\\Users\\Ana\\Downloads",
              "enabled": true,
              "rules": [
                {
                  "name": "Invoices",
                  "enabled": true,
                  "names": ["invoice*", "receipt*"],
                  "extensions": ["pdf"],
                  "downloadedFrom": ["*.billing.example.com"],
                  "action": "Move",
                  "destination": "C:\\Users\\Ana\\Documents\\Invoices\\{year}",
                  "newName": "{date} {name}",
                  "ifExists": "AddNumber"
                },
                {
                  "name": "Large videos",
                  "enabled": true,
                  "extensions": ["mp4", "mkv"],
                  "size": { "largerThan": { "amount": 1, "unit": "GB" } },
                  "action": "Move",
                  "destination": "D:\\Videos",
                  "ifExists": "Skip"
                },
                {
                  "name": "Old screenshots",
                  "enabled": false,
                  "names": ["Screenshot*"],
                  "date": { "which": "Created", "moreThanDaysAgo": 30 },
                  "action": "RecycleBin"
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoFolders()
    {
        var settings = FolderRulesSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Folders);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = FolderRulesSettings.Read(Section(Example));

        Assert.True(settings.Enabled);
        var folder = Assert.Single(settings.Folders);
        Assert.Equal(@"C:\Users\Ana\Downloads", folder.Path);
        Assert.Equal(3, folder.Rules.Count);

        var invoices = folder.Rules[0];
        Assert.Equal(["invoice*", "receipt*"], invoices.Names);
        Assert.Equal(["pdf"], invoices.Extensions);
        Assert.Equal(["*.billing.example.com"], invoices.DownloadedFrom);
        Assert.Equal(FolderRuleAction.Move, invoices.KnownAction);
        Assert.Equal(@"C:\Users\Ana\Documents\Invoices\{year}", invoices.Destination);
        Assert.Equal("{date} {name}", invoices.NewName);
        Assert.Equal(NameConflict.AddNumber, invoices.KnownIfExists);

        var videos = folder.Rules[1];
        Assert.Equal(new SizeCondition { LargerThan = new SizeLimit { Amount = 1, Unit = SizeUnit.GB } }, videos.Size);
        Assert.Equal(NameConflict.Skip, videos.KnownIfExists);

        var screenshots = folder.Rules[2];
        Assert.False(screenshots.Enabled);
        Assert.Equal(new DateCondition { Which = FileDate.Created, MoreThanDaysAgo = 30 }, screenshots.Date);
        Assert.Equal(FolderRuleAction.RecycleBin, screenshots.KnownAction);
    }

    [Fact]
    public void WrittenSettingsReadBackTheSame()
    {
        var settings = FolderRulesSettings.Read(Section(Example));

        Assert.Equal(settings, FolderRulesSettings.Read(settings.Write()));
    }

    [Fact]
    public void TurningItOffKeepsTheFolders()
    {
        var settings = FolderRulesSettings.Read(Section(Example)).WithEnabled(false);

        Assert.False(settings.Enabled);
        Assert.Single(settings.Folders);
    }

    [Fact]
    public void AFolderAndARuleWithoutTheirSwitchesAreOn()
    {
        var settings = FolderRulesSettings.Read(Section("""
            { "folders": [ { "path": "D:\\Scans", "rules": [ { "name": "PDFs", "extensions": ["pdf"] } ] } ] }
            """));

        var folder = Assert.Single(settings.Folders);
        Assert.True(folder.Enabled);
        var rule = Assert.Single(folder.Rules);
        Assert.True(rule.Enabled);
        Assert.Equal(FolderRuleAction.Move, rule.KnownAction);
        Assert.Equal(NameConflict.AddNumber, rule.KnownIfExists);
        Assert.Null(rule.Size);
        Assert.Null(rule.Date);
    }

    [Fact]
    public void AnActionThisVersionDoesntKnowIsKeptWhenSaved()
    {
        var settings = FolderRulesSettings.Read(Section("""
            { "folders": [ { "path": "D:\\Scans", "rules": [ { "name": "Archives", "extensions": ["zip"], "action": "Extract", "ifExists": "Replace" } ] } ] }
            """));

        var rule = settings.Folders[0].Rules[0];
        Assert.Null(rule.KnownAction);
        Assert.Null(rule.KnownIfExists);

        var saved = FolderRulesSettings.Read(settings.Write()).Folders[0].Rules[0];
        Assert.Equal("Extract", saved.Action);
        Assert.Equal("Replace", saved.IfExists);
    }

    [Fact]
    public void ActionsAreReadIgnoringCase()
    {
        var settings = FolderRulesSettings.Read(Section("""
            { "folders": [ { "path": "D:\\Scans", "rules": [ { "name": "Old", "extensions": ["tmp"], "action": "recyclebin", "ifExists": "skip" } ] } ] }
            """));

        Assert.Equal(FolderRuleAction.RecycleBin, settings.Folders[0].Rules[0].KnownAction);
        Assert.Equal(NameConflict.Skip, settings.Folders[0].Rules[0].KnownIfExists);
    }

    [Fact]
    public void ReadLeavesOutANullInAnyList()
    {
        var settings = FolderRulesSettings.Read(Section("""
            {
              "folders": [
                null,
                {
                  "path": "D:\\Scans",
                  "rules": [
                    null,
                    { "name": "PDFs", "names": [null, "scan*"], "extensions": ["pdf", null], "downloadedFrom": [null] }
                  ]
                }
              ]
            }
            """));

        var folder = Assert.Single(settings.Folders);
        var rule = Assert.Single(folder.Rules);
        Assert.Equal(["scan*"], rule.Names);
        Assert.Equal(["pdf"], rule.Extensions);
        Assert.Empty(rule.DownloadedFrom);
    }

    [Theory]
    [InlineData("""{ "folders": 5 }""")]
    [InlineData("""{ "folders": [ { "path": null } ] }""")]
    [InlineData("""{ "folders": [ { "path": "D:\\Scans", "rules": [ { "size": { "largerThan": { "amount": 1, "unit": "TB" } } } ] } ] }""")]
    public void ASectionThatDoesntMatchTheFormatCantBeRead(string json)
    {
        Assert.ThrowsAny<JsonException>(() => FolderRulesSettings.Read(Section(json)));
    }
}
