using System.Globalization;
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
}
