using System;
using System.Globalization;

namespace PCCompanion.Localization;

/// <summary>
/// Strings that change with a number. Each is stored once per CLDR plural category that its
/// language uses, as <c>Key_one</c>, <c>Key_few</c> and so on; every language has <c>Key_other</c>.
/// </summary>
public static class PluralStrings
{
    /// <summary>
    /// Formats the plural string <paramref name="key"/> for <paramref name="count"/> in the
    /// user interface language, with the number in the user's regional format.
    /// </summary>
    public static string Format(string key, int count) =>
        Format(key, count, CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);

    /// <param name="language">The user interface language.</param>
    /// <param name="formatting">The regional format of the number.</param>
    public static string Format(string key, int count, CultureInfo language, IFormatProvider formatting)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(language);

        var otherKey = key + "_other";

        // Use the plural rules of the language the string comes from. Without a translation,
        // that is the source language rather than the user interface language.
        for (var culture = language; ; culture = culture.Parent)
        {
            var strings = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            if (strings?.GetString(otherKey) is { } other)
            {
                var rules = culture.Equals(CultureInfo.InvariantCulture) ? Languages.Source : culture;
                var form = strings.GetString($"{key}_{PluralRules.Select(rules, count)}") ?? other;
                return string.Format(formatting, form, count.ToString("N0", formatting));
            }

            if (culture.Equals(CultureInfo.InvariantCulture))
            {
                throw new ArgumentException($"There is no string named {otherKey}.", nameof(key));
            }
        }
    }
}
