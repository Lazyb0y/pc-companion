using System;
using System.Drawing;
using System.Globalization;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class PasteNoticesTests
{
    private static readonly DisplayDetails Display = new(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), 96);
    private static readonly Size Notice = new(200, 40);

    [Fact]
    public void EveryOutcomeButAPasteHasANotice()
    {
        foreach (var outcome in Enum.GetValues<PasteOutcome>())
        {
            var text = PasteNotices.TextOf(outcome);
            if (outcome == PasteOutcome.Pasted)
            {
                Assert.Null(text);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"{outcome} has no notice");
            }
        }
    }

    [Fact]
    public void TheLimitIsShownInTheUsersNumberFormat()
    {
        var culture = CultureInfo.CurrentCulture;
        var language = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Contains("65,536", PasteNotices.TextOf(PasteOutcome.TooLong), StringComparison.Ordinal);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Contains("65.536", PasteNotices.TextOf(PasteOutcome.TooLong), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = language;
        }
    }

    [Fact]
    public void TheNoticeGoesBelowThePointerStartingAtIt() =>
        Assert.Equal(new Rectangle(100, 120, 200, 40), PasteNotices.Place(Display, Notice, new Point(100, 100), rightToLeft: false));

    [Fact]
    public void InRightToLeftLanguagesItEndsAtThePointer() =>
        Assert.Equal(new Rectangle(300, 120, 200, 40), PasteNotices.Place(Display, Notice, new Point(500, 100), rightToLeft: true));

    [Fact]
    public void WithoutRoomBelowItGoesAbove() =>
        Assert.Equal(new Rectangle(100, 970, 200, 40), PasteNotices.Place(Display, Notice, new Point(100, 1030), rightToLeft: false));

    [Fact]
    public void ItStaysInsideTheWorkArea()
    {
        Assert.Equal(new Rectangle(1720, 120, 200, 40), PasteNotices.Place(Display, Notice, new Point(1900, 100), rightToLeft: false));
        Assert.Equal(new Rectangle(0, 120, 200, 40), PasteNotices.Place(Display, Notice, new Point(50, 100), rightToLeft: true));
    }

    [Fact]
    public void TheGapFollowsTheDisplaysScale() =>
        Assert.Equal(130, PasteNotices.Place(Display with { Dpi = 144 }, Notice, new Point(100, 100), rightToLeft: false).Y);
}
