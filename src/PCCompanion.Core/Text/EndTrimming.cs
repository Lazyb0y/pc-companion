using System;

namespace PCCompanion.Core.Text;

/// <summary>
/// Shortens text that doesn't fit by keeping its end after an ellipsis, as in
/// <c>…\System32\charmap.exe</c>, for text whose end matters most, such as a path.
/// </summary>
public static class EndTrimming
{
    public const string Ellipsis = "…";

    /// <summary>
    /// Returns <paramref name="text"/> when it fits in <paramref name="width"/>, or the ellipsis
    /// and the longest end of it that fits.
    /// </summary>
    /// <param name="measure">The width of a text, in the unit of <paramref name="width"/>.</param>
    /// <param name="separators">
    /// Characters the kept end should start at, such as <c>\</c> for a path, so it doesn't begin
    /// in the middle of a name when a shorter end starts at one.
    /// </param>
    public static string KeepEnd(string text, double width, Func<string, double> measure, string separators = "")
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(separators);

        if (string.IsNullOrEmpty(text) || measure(text) <= width)
        {
            return text;
        }

        // The longest end that fits after the ellipsis; the width grows with the length.
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (measure(Ellipsis + text[^middle..]) <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var end = text[^low..];
        var startsAtBoundary = end.Length > 0 && (separators.Contains(end[0], StringComparison.Ordinal) || separators.Contains(text[^(low + 1)], StringComparison.Ordinal));
        if (!startsAtBoundary)
        {
            var separator = end.IndexOfAny(separators.ToCharArray());
            if (separator > 0)
            {
                end = end[separator..];
            }
        }

        return Ellipsis + end;
    }
}
