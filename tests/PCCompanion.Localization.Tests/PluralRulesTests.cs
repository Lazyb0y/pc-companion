using System.Globalization;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed class PluralRulesTests
{
    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("bn", 0, "one")]
    [InlineData("bn", 2, "other")]
    [InlineData("de-AT", 1, "one")]
    [InlineData("zh-Hans", 1, "other")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 100, "other")]
    public void SelectReturnsTheCldrCategory(string language, int count, string expected)
    {
        Assert.Equal(expected, PluralRules.Select(CultureInfo.GetCultureInfo(language), count));
    }

    [Fact]
    public void SelectFallsBackToTheRootRulesForUnknownLanguages()
    {
        Assert.Equal("other", PluralRules.Select(CultureInfo.InvariantCulture, 1));
    }
}
