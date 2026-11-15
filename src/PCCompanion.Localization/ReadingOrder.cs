using System;
using System.Globalization;
using System.Text;

namespace PCCompanion.Localization;

/// <summary>
/// Lays out lines of text right to left in right-to-left languages. Most of Windows takes a line's
/// direction from its first letter, so an Arabic line that starts with a name, path or address in
/// Latin letters, such as "Photos على files.example.com", would read left to right, with its parts
/// in the wrong order. A right-to-left mark (U+200F), which shows as nothing, at the start of the
/// line makes it read right to left.
/// </summary>
public static class ReadingOrder
{
    /// <summary>The right-to-left mark, U+200F.</summary>
    public const char RightToLeftMark = '\u200F';

    /// <summary>
    /// Returns <paramref name="text"/> as a line shown on its own: starting with a right-to-left mark
    /// when the user interface language's strings are written right to left, otherwise unchanged.
    /// An empty text stays empty.
    /// </summary>
    public static string Line(string text) => LineIn(CultureInfo.CurrentUICulture, text);

    /// <summary>
    /// Returns <paramref name="text"/> as a line shown on its own when the user interface language is
    /// <paramref name="language"/>.
    /// </summary>
    public static string LineIn(CultureInfo language, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        // A language without a translation shows English, left to right.
        return text.Length == 0 || text[0] == RightToLeftMark || !Languages.Resolve(language).TextInfo.IsRightToLeft
            ? text
            : RightToLeftMark + text;
    }

    /// <summary>
    /// Formats a line shown on its own, such as a page's description of a server, with the user's
    /// regional format, as <see cref="Line(string)"/> returns it.
    /// </summary>
    public static string Format(CompositeFormat format, params ReadOnlySpan<object?> values)
    {
        ArgumentNullException.ThrowIfNull(format);

        return Line(string.Format(CultureInfo.CurrentCulture, format, values));
    }
}
