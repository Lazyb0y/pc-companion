using System;
using System.Globalization;
using System.Linq;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed class LanguagesTests
{
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-EG", "ar")]
    [InlineData("de-AT", "de")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("en-GB", "en")]
    [InlineData("zh-TW", "en")]
    [InlineData("he", "en")]
    [InlineData("", "en")]
    public void ResolveReturnsTheLanguageTheStringsComeFrom(string language, string expected)
    {
        Assert.Equal(expected, Languages.Resolve(CultureInfo.GetCultureInfo(language)).Name);
    }

    [Theory]
    [InlineData("bn", "bn-BD", "bn", "bn-IN")]
    [InlineData("zh-Hans", "zh-CN", "zh-Hans")]
    [InlineData("ar", "ar-SA", "ar")]
    [InlineData("de-AT", "de-AT", "de")]
    public void ForWindowsResourcesStartsWithTheMainRegionAndTheLanguage(string language, params string[] expectedStart)
    {
        var names = Languages.ForWindowsResources(CultureInfo.GetCultureInfo(language));

        Assert.Equal(expectedStart, names.Take(expectedStart.Length));
        Assert.InRange(names.Count, expectedStart.Length, 5);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
