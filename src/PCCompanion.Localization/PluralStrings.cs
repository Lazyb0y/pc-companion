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

        // Use the plural rules of the language the string comes from. Without a translation,
        // that is the source language rather than the user interface language. The resource
        // manager can't tell: it caches the source language's strings under every language
        // that falls back to them.
        var source = Languages.Resolve(language);
        var form = Strings.ResourceManager.GetString($"{key}_{PluralRules.Select(source, count)}", source)
            ?? Strings.ResourceManager.GetString(key + "_other", source)
            ?? throw new ArgumentException($"There is no string named {key}_other.", nameof(key));

        return string.Format(formatting, form, count.ToString("N0", formatting));
    }
}
