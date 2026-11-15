using System.Globalization;
using System.Text;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed class ReadingOrderTests
{
    private const string Mark = "\u200F";

    [Theory]
    [InlineData("ar")]
    [InlineData("ar-EG")]
    public void LineStartsWithTheRightToLeftMarkInARightToLeftLanguage(string language)
    {
        Assert.Equal(Mark + "Photos على files.example.com", ReadingOrder.LineIn(CultureInfo.GetCultureInfo(language), "Photos على files.example.com"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]
    public void LineStaysTheSameInALeftToRightLanguage(string language)
    {
        Assert.Equal("Photos on files.example.com", ReadingOrder.LineIn(CultureInfo.GetCultureInfo(language), "Photos on files.example.com"));
    }

    [Fact]
    public void LineStaysTheSameInARightToLeftLanguageWithoutATranslation()
    {
        // Hebrew has no strings, so the line is English, left to right.
        Assert.Equal("Photos on files.example.com", ReadingOrder.LineIn(CultureInfo.GetCultureInfo("he"), "Photos on files.example.com"));
    }

    [Fact]
    public void LineKeepsAnEmptyTextEmpty()
    {
        Assert.Equal(string.Empty, ReadingOrder.LineIn(CultureInfo.GetCultureInfo("ar"), string.Empty));
    }

    [Fact]
    public void LineAddsTheMarkOnlyOnce()
    {
        var line = ReadingOrder.LineIn(CultureInfo.GetCultureInfo("ar"), "Photos على files.example.com");

        Assert.Equal(line, ReadingOrder.LineIn(CultureInfo.GetCultureInfo("ar"), line));
    }

    [Fact]
    public void FormatUsesTheUserInterfaceLanguageAndTheRegionalFormat()
    {
        var language = CultureInfo.CurrentUICulture;
        var formatting = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal(Mark + "1,234 على files.example.com", ReadingOrder.Format(CompositeFormat.Parse("{0:N0} على {1}"), 1234, "files.example.com"));

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

            Assert.Equal("1,234 on files.example.com", ReadingOrder.Format(CompositeFormat.Parse("{0:N0} on {1}"), 1234, "files.example.com"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = language;
            CultureInfo.CurrentCulture = formatting;
        }
    }
}
