using System;
using System.Globalization;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed class PluralStringsTests
{
    private const string Key = "GeneralLogsRetention";

    [Theory]
    [InlineData("en", 1, "Logs are kept for 1 day.")]
    [InlineData("en", 14, "Logs are kept for 14 days.")]
    [InlineData("en-GB", 1, "Logs are kept for 1 day.")]
    [InlineData("fr", 0, "Les journaux sont conservés pendant 0 jour.")]
    [InlineData("ar", 2, "يتم الاحتفاظ بالسجلات لمدة يومين.")]
    [InlineData("ar", 14, "يتم الاحتفاظ بالسجلات لمدة 14 يومًا.")]
    [InlineData("zh-CN", 1, "日志将保留 1 天。")]
    public void FormatUsesThePluralFormOfTheLanguage(string language, int count, string expected)
    {
        Assert.Equal(expected, PluralStrings.Format(Key, count, CultureInfo.GetCultureInfo(language), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatUsesTheSourceLanguageRulesWhenThereIsNoTranslation()
    {
        // Japanese uses "other" for every number, but the string comes from English.
        Assert.Equal("Logs are kept for 1 day.", PluralStrings.Format(Key, 1, CultureInfo.GetCultureInfo("ja"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatUsesTheRegionalFormatForTheNumber()
    {
        Assert.Equal(
            "Logs are kept for 1.000 days.",
            PluralStrings.Format(Key, 1000, CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void FormatUsesTheCurrentLanguageAndRegionalFormat()
    {
        var (previousLanguage, previousFormat) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("Protokolle werden 1,000 Tage lang aufbewahrt.", PluralStrings.Format(Key, 1000));
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (previousLanguage, previousFormat);
        }
    }

    [Fact]
    public void FormatRejectsUnknownStrings()
    {
        Assert.Throws<ArgumentException>(() => PluralStrings.Format("Missing", 1, CultureInfo.GetCultureInfo("en"), CultureInfo.InvariantCulture));
    }
}
