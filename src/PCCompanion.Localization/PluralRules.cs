using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PCCompanion.Localization;

/// <summary>
/// CLDR plural rules, provided by the ICU library built into Windows.
/// </summary>
public static class PluralRules
{
    /// <summary>
    /// Returns the CLDR plural category of <paramref name="count"/> in <paramref name="language"/>:
    /// <c>zero</c>, <c>one</c>, <c>two</c>, <c>few</c>, <c>many</c> or <c>other</c>.
    /// </summary>
    public static string Select(CultureInfo language, int count)
    {
        ArgumentNullException.ThrowIfNull(language);

        var status = 0;
        var rules = Icu.OpenPluralRules(language.Name.Replace('-', '_'), ref status);
        ThrowIfFailed(status, "uplrules_open");

        try
        {
            Span<ushort> keyword = stackalloc ushort[8];
            var length = Icu.SelectPluralKeyword(rules, count, ref MemoryMarshal.GetReference(keyword), keyword.Length, ref status);
            ThrowIfFailed(status, "uplrules_select");

            return new string(MemoryMarshal.Cast<ushort, char>(keyword[..length]));
        }
        finally
        {
            Icu.ClosePluralRules(rules);
        }
    }

    private static void ThrowIfFailed(int status, string function)
    {
        if (Icu.Failed(status))
        {
            throw new InvalidOperationException($"ICU {function} failed with error {status}.");
        }
    }
}
