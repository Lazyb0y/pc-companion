using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed partial class StringResourceTests
{
    // Enough whole numbers to reach every plural category a language uses for counts.
    private const int PluralSampleSize = 1001;

    public static TheoryData<string> AllLanguages { get; } =
        [.. Languages.Available.Select(language => language.Name)];

    public static TheoryData<string> Translations { get; } =
        [.. Languages.Available.Where(language => !language.Equals(Languages.Source)).Select(language => language.Name)];

    [Fact]
    public void TranslationsAreAvailable()
    {
        Assert.Equal("en", Languages.Source.Name);
        Assert.Contains(Languages.Source, Languages.Available);
        Assert.NotEmpty(Translations);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void StringsAreNotEmpty(string language)
    {
        var strings = ReadStrings(CultureInfo.GetCultureInfo(language));

        Assert.Empty(strings.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasEveryStringAndNoOthers(string language)
    {
        var english = ReadStrings(Languages.Source).Keys.Select(BaseKey).Distinct().Order();
        var translated = ReadStrings(CultureInfo.GetCultureInfo(language)).Keys.Select(BaseKey).Distinct().Order();

        Assert.Equal(english, translated);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void PluralStringsHaveTheFormsTheLanguageUses(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var categories = Enumerable.Range(0, PluralSampleSize).Select(count => PluralRules.Select(culture, count)).ToHashSet();

        var wrong = ReadStrings(culture).Keys
            .Select(key => PluralKeyPattern().Match(key))
            .Where(match => match.Success)
            .GroupBy(match => match.Groups["base"].Value, match => match.Groups["category"].Value)
            .Where(forms => !categories.SetEquals(forms))
            .Select(forms => forms.Key);

        Assert.Empty(wrong);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void StringsKeepTheEnglishPlaceholders(string language)
    {
        var english = ReadStrings(Languages.Source);

        var mismatches = ReadStrings(CultureInfo.GetCultureInfo(language))
            .Where(entry => !HasEnglishPlaceholders(entry.Key, entry.Value, english))
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

    // A plural form may leave out the number (Arabic "two days" is one word), but "other" must keep it.
    private static bool HasEnglishPlaceholders(string key, string value, Dictionary<string, string> english)
    {
        var plural = PluralKeyPattern().Match(key);
        var englishValue = plural.Success ? english.GetValueOrDefault(plural.Groups["base"].Value + "_other") : english.GetValueOrDefault(key);
        if (englishValue is null)
        {
            return true;
        }

        var expected = Placeholders(englishValue);
        var actual = Placeholders(value);
        return plural.Success && plural.Groups["category"].Value != "other" ? actual.IsSubsetOf(expected) : actual.SetEquals(expected);
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

    private static string BaseKey(string key)
    {
        var plural = PluralKeyPattern().Match(key);
        return plural.Success ? plural.Groups["base"].Value : key;
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Groups["index"].Value)];

    [GeneratedRegex("^(?<base>.+)_(?<category>zero|one|two|few|many|other)$")]
    private static partial Regex PluralKeyPattern();

    // Composite format items such as {0} and {0:N0}; {{ and }} are escaped braces.
    [GeneratedRegex(@"(?<!\{)\{(?<index>\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
