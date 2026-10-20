using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class ClipboardRulesTests
{
    private const string Player = @"C:\Program Files\Example\player.exe";

    private static readonly ClipboardRule Parcel = new() { Name = "Track the parcel", Pattern = "1Z[0-9A-Z]{16}", Address = "https://tracking.example.com/?number={text}" };
    private static readonly ClipboardRule Video = new() { Name = "Play", Pattern = @"https?://\S+\.mp4", Action = "RunProgram", Program = Player, Arguments = "--fullscreen {text}" };

    private readonly RecordingLogger<ClipboardRulesTests> _logger = new();

    [Fact]
    public void ALinkGetsOpenInBrowserFirstAndThenTheMatchingRulesInOrder()
    {
        var rules = Compile(new ClipboardActionsSettings { Rules = [Parcel, Video, Video with { Name = "Play again" }] });

        var offers = rules.Match("https://example.com/clip.mp4");

        Assert.Equal(
            [
                new ClipboardOffer(OfferKind.OpenLink, string.Empty, 0, new Uri("https://example.com/clip.mp4")),
                new ClipboardOffer(OfferKind.RunProgram, "Play", 2, Program: Player),
                new ClipboardOffer(OfferKind.RunProgram, "Play again", 3, Program: Player),
            ],
            offers.Select(offer => offer with { Arguments = null }));
        Assert.Equal(["--fullscreen", "https://example.com/clip.mp4"], offers[1].Arguments!);
    }

    [Fact]
    public void AMatchingAddressRuleOffersItsAddressWithTheText()
    {
        var offer = Assert.Single(Compile(new ClipboardActionsSettings { Rules = [Parcel] }).Match("1Z999AA10123456784"));

        Assert.Equal(new ClipboardOffer(OfferKind.OpenAddress, "Track the parcel", 1, new Uri("https://tracking.example.com/?number=1Z999AA10123456784")), offer);
    }

    [Fact]
    public void WithOpenCopiedLinksOffALinkGetsOnlyItsRules()
    {
        var rules = Compile(new ClipboardActionsSettings { OpenLinks = false, Rules = [Video] });

        Assert.Equal([OfferKind.RunProgram], rules.Match("https://example.com/clip.mp4").Select(offer => offer.Kind));
        Assert.Empty(rules.Match("https://example.com/"));
    }

    [Theory]
    [InlineData("https://example.com/a path")]
    [InlineData("see https://example.com")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("www.example.com")]
    [InlineData("https://")]
    [InlineData("https://example.com/\u0007")]
    public void OnlyOneWebLinkIsALink(string text)
    {
        Assert.Null(ClipboardRules.FindLink(text));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("HTTP://EXAMPLE.COM/Page?q=1#top")]
    public void WebLinksAreLinks(string text)
    {
        Assert.NotNull(ClipboardRules.FindLink(text));
    }

    [Fact]
    public void RulesThatCantBeUsedAreLeftOutAndLoggedByNumber()
    {
        var rules = Compile(new ClipboardActionsSettings
        {
            Rules = [Parcel with { Pattern = "[0-9" }, Parcel, Parcel with { Action = "Translate" }, Video with { Program = @"C:\Windows\System32\cmd.exe" }],
        });

        Assert.Equal(1, rules.Count);
        Assert.Equal(
            [
                (LogLevel.Warning, "Clipboard Actions rule 1 can't be used: InvalidPattern."),
                (LogLevel.Warning, "Clipboard Actions rule 3 can't be used: UnknownAction."),
                (LogLevel.Warning, "Clipboard Actions rule 4 can't be used: CommandInterpreter."),
            ],
            _logger.Entries);
    }

    [Fact]
    public void OnlyTheFirstFiftyRulesAreUsed()
    {
        var rules = Compile(new ClipboardActionsSettings { Rules = [.. Enumerable.Range(1, 60).Select(number => Parcel with { Name = $"Rule {number}" })] });

        Assert.Equal(ClipboardActionsSettings.MaximumRules, rules.Count);
        Assert.Equal("Rule 50", rules.Match("1Z999AA10123456784")[^1].Name);
        Assert.Contains((LogLevel.Warning, "Clipboard Actions has 60 rules and uses the first 50."), _logger.Entries);
    }

    [Fact]
    public void ARuleWhoseAddressBreaksWithTheTextOffersNothing()
    {
        var rules = Compile(new ClipboardActionsSettings { Rules = [new ClipboardRule { Name = "Site", Pattern = ".+", Address = "https://{text}" }] });

        Assert.Empty(rules.Match("not a host"));
        Assert.Single(rules.Match("example"));
    }

    [Fact]
    public void LogsNeverHoldTheTextPatternsOrNames()
    {
        var rules = Compile(new ClipboardActionsSettings { Rules = [Parcel with { Name = "Secret name", Pattern = "secret-[0-9" }] });
        rules.Match("secret-123");

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private ClipboardRules Compile(ClipboardActionsSettings settings) => ClipboardRules.Compile(settings, _logger);
}
