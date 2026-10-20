using System.Text.Json;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class ClipboardActionsSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithOpenCopiedLinksOnAndNoRules()
    {
        var settings = ClipboardActionsSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.True(settings.OpenLinks);
        Assert.Empty(settings.Rules);
    }

    [Fact]
    public void TheSettingsReadBackAsWritten()
    {
        var settings = new ClipboardActionsSettings
        {
            Enabled = true,
            OpenLinks = false,
            Rules =
            [
                new ClipboardRule { Name = "Track the parcel", Pattern = "1Z[0-9A-Z]{16}", Address = "https://tracking.example.com/?number={text}" },
                new ClipboardRule { Name = "Play", Pattern = @"https?://\S+\.mp4", MatchCase = true, Action = "RunProgram", Program = @"C:\Program Files\Example\player.exe", Arguments = "--fullscreen {text}" },
            ],
        };

        var read = ClipboardActionsSettings.Read(settings.Write());

        Assert.Equal(settings, read);
    }

    [Fact]
    public void TheFormatMatchesTheSpec()
    {
        var json = JsonSerializer.Serialize(new ClipboardActionsSettings
        {
            Enabled = true,
            Rules = [new ClipboardRule { Name = "Track", Pattern = "1Z.*", Address = "https://example.com/?n={text}" }],
        }.Write());

        Assert.Equal(
            """{"enabled":true,"openLinks":true,"rules":[{"name":"Track","pattern":"1Z.*","matchCase":false,"action":"OpenAddress","address":"https://example.com/?n={text}","program":"","arguments":""}]}""",
            json);
    }

    [Fact]
    public void AnUnknownActionIsKeptAndKnownToBeUnknown()
    {
        using var document = JsonDocument.Parse("""{ "enabled": true, "rules": [ { "name": "New", "pattern": "x", "action": "Translate" } ] }""");

        var settings = ClipboardActionsSettings.Read(document.RootElement);

        var rule = Assert.Single(settings.Rules);
        Assert.Equal("Translate", rule.Action);
        Assert.Null(rule.KnownAction);
        Assert.Contains("\"Translate\"", JsonSerializer.Serialize(settings.Write()), System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openaddress", ClipboardRuleAction.OpenAddress)]
    [InlineData("RunProgram", ClipboardRuleAction.RunProgram)]
    public void ActionsAreReadWhateverTheirCase(string action, ClipboardRuleAction expected)
    {
        Assert.Equal(expected, new ClipboardRule { Action = action }.KnownAction);
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        using var document = JsonDocument.Parse("""{ "enabled": true, "rules": [ { "name": null } ] }""");

        Assert.Throws<JsonException>(() => ClipboardActionsSettings.Read(document.RootElement));
    }

    [Fact]
    public void TheSameSettingsReadTwiceAreEqual()
    {
        using var document = JsonDocument.Parse("""{ "enabled": true, "rules": [ { "name": "A", "pattern": "x" } ] }""");

        Assert.Equal(ClipboardActionsSettings.Read(document.RootElement), ClipboardActionsSettings.Read(document.RootElement));
    }
}
