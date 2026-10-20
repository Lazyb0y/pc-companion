using System;
using System.Linq;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class ClipboardMenuTests
{
    private const char Lre = '\u202A';
    private const char Rle = '\u202B';
    private const char Pdf = '\u202C';

    private static readonly ClipboardOffer Link = new(OfferKind.OpenLink, string.Empty, 0, new Uri("https://example.com/clip.mp4"));
    private static readonly ClipboardOffer Play = new(OfferKind.RunProgram, "Play", 1, Program: @"C:\Program Files\Example\player.exe", Arguments: ["https://example.com/clip.mp4"]);
    private static readonly ClipboardOffer Track = new(OfferKind.OpenAddress, "Track the parcel", 2, new Uri("https://tracking.example.com/?n=1"));

    [Fact]
    public void TheMenuShowsTheTextTheActionsAndSettings()
    {
        var plan = ClipboardMenu.Build(Text("https://example.com/clip.mp4\r\n"), [Link, Play], isAdministrator: false);

        Assert.Equal(
            [
                (Lre + "https://example.com/clip.mp4" + Pdf, false),
                ("-", false),
                (Strings.ClipboardActionsOpenInBrowser, true),
                ("Play", true),
                ("-", false),
                (Strings.TraySettings, true),
            ],
            plan.Items.Select(Describe));
        Assert.Equal(ClipboardMenu.SettingsCommand, plan.Items[^1].Id);
        Assert.Equal(Link, plan.Offers[plan.Items[2].Id]);
        Assert.Equal(Play, plan.Offers[plan.Items[3].Id]);
        Assert.Equal(2, plan.Offers.Count);
    }

    [Fact]
    public void WithoutActionsANoteSaysSo()
    {
        var plan = ClipboardMenu.Build(Text("PC-12"), [], isAdministrator: false);

        Assert.Equal([(Lre + "PC-12" + Pdf, false), ("-", false), (Strings.ClipboardActionsNoActions, false), ("-", false), (Strings.TraySettings, true)], plan.Items.Select(Describe));
        Assert.Empty(plan.Offers);
    }

    [Theory]
    [InlineData(ClipboardTextStatus.NoText)]
    [InlineData(ClipboardTextStatus.Private)]
    [InlineData(ClipboardTextStatus.TooLong)]
    [InlineData(ClipboardTextStatus.Busy)]
    [InlineData(ClipboardTextStatus.Unavailable)]
    public void WithoutTextANoteSaysWhy(ClipboardTextStatus status)
    {
        var expected = status switch
        {
            ClipboardTextStatus.Private => Strings.ClipboardActionsPrivate,
            ClipboardTextStatus.TooLong => Strings.ClipboardActionsTooLong,
            ClipboardTextStatus.NoText => Strings.ClipboardActionsNoText,
            _ => Strings.ClipboardActionsCantRead,
        };

        var plan = ClipboardMenu.Build(new ClipboardTextResult(status), [], isAdministrator: false);

        Assert.Equal([(expected, false), ("-", false), (Strings.TraySettings, true)], plan.Items.Select(Describe));
    }

    [Fact]
    public void OnlySpacesCountAsNoText()
    {
        var plan = ClipboardMenu.Build(Text(" \r\n\t"), [], isAdministrator: false);

        Assert.Equal(Strings.ClipboardActionsNoText, plan.Items[0].Text);
    }

    [Fact]
    public void WhileRunningAsAdministratorProgramsShowDimmed()
    {
        var plan = ClipboardMenu.Build(Text("https://example.com/clip.mp4"), [Link, Play, Track], isAdministrator: true);

        Assert.Equal([true, false, true], plan.Items.Skip(2).Take(3).Select(item => item.IsEnabled));
    }

    [Fact]
    public void ThePreviewShowsTheStartOfTheFirstLine()
    {
        Assert.Equal(Lre + new string('a', 50) + "\u2026" + Pdf, ClipboardMenu.Preview(new string('a', 60)));
        Assert.Equal(Lre + new string('a', 50) + Pdf, ClipboardMenu.Preview(new string('a', 50)));
        Assert.Equal(Lre + "first line\u2026" + Pdf, ClipboardMenu.Preview("first line\r\nsecond line"));
        Assert.Equal(Lre + "tab here" + Pdf, ClipboardMenu.Preview("tab\there"));
        Assert.Equal(Lre + "Tom && Jerry" + Pdf, ClipboardMenu.Preview("Tom & Jerry"));
    }

    [Theory]
    [InlineData("مرحبا بالعالم (2) x.txt", Rle)]
    [InlineData("שלום (2) x.txt", Rle)]
    [InlineData("(2) مرحبا", Rle)]
    [InlineData("report (final) v2.txt", Lre)]
    [InlineData("12345", Lre)]
    public void ThePreviewKeepsTheDirectionOfItsFirstLetter(string text, char embedding)
    {
        var preview = ClipboardMenu.Preview(text);

        Assert.Equal(embedding, preview[0]);
        Assert.Equal(Pdf, preview[^1]);
    }

    [Fact]
    public void NamesAreShortenedAndShowAmpersandsAsWritten()
    {
        Assert.Equal("Search && copy", ClipboardMenu.Label(Track with { Name = "  Search & copy " }));
        Assert.Equal(new string('n', 60) + "\u2026", ClipboardMenu.Label(Track with { Name = new string('n', 70) }));
    }

    private static ClipboardTextResult Text(string text) => new(ClipboardTextStatus.Text, text);

    private static (string Text, bool Enabled) Describe(PopupMenuItem item) => item.IsSeparator ? ("-", false) : (item.Text, item.IsEnabled);
}
