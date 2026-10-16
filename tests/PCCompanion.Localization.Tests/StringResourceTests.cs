using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed partial class StringResourceTests
{
    public static TheoryData<string> Translations { get; } =
        [.. Languages.Available.Where(language => !language.Equals(Languages.Source)).Select(language => language.Name)];

    [Fact]
    public void EnglishStringsAreNotEmpty()
    {
        Assert.Empty(ReadStrings(Languages.Source).Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key));
    }

    [Fact]
    public void TranslationsAreAvailable()
    {
        Assert.Equal("en", Languages.Source.Name);
        Assert.Contains(Languages.Source, Languages.Available);
        Assert.NotEmpty(Translations);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasEveryStringAndNoOthers(string language)
    {
        var english = ReadStrings(Languages.Source).Keys.Order();
        var translated = ReadStrings(CultureInfo.GetCultureInfo(language)).Keys.Order();

        Assert.Equal(english, translated);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationIsNotEmpty(string language)
    {
        var strings = ReadStrings(CultureInfo.GetCultureInfo(language));

        Assert.Empty(strings.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationKeepsThePlaceholders(string language)
    {
        var english = ReadStrings(Languages.Source);
        var translated = ReadStrings(CultureInfo.GetCultureInfo(language));

        var mismatches = translated
            .Where(entry => english.ContainsKey(entry.Key))
            .Where(entry => !Placeholders(entry.Value).SetEquals(Placeholders(english[entry.Key])))
            .Select(entry => entry.Key);

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData("de", "Allgemein")]
    [InlineData("de-AT", "Allgemein")]
    [InlineData("zh-CN", "常规")]
    [InlineData("en-GB", "General")]
    [InlineData("ja", "General")]
    public void StringsFollowTheUserInterfaceLanguage(string language, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);

            Assert.Equal(expected, Strings.NavigationGeneral);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static Dictionary<string, string> ReadStrings(CultureInfo language)
    {
        var resources = Strings.ResourceManager.GetResourceSet(
            language.Equals(Languages.Source) ? CultureInfo.InvariantCulture : language,
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resources);
        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string ?? string.Empty);
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Groups["index"].Value)];

    // Composite format items such as {0} and {0:N0}; {{ and }} are escaped braces.
    [GeneratedRegex(@"(?<!\{)\{(?<index>\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
