using System.Runtime.InteropServices;

namespace PCCompanion.Localization;

/// <summary>
/// The parts of the ICU library built into Windows (10 version 1903 and later) that the app uses.
/// </summary>
internal static partial class Icu
{
    private const string Library = "icu.dll";

    /// <summary>ICU reports errors as positive <c>UErrorCode</c> values and warnings as negative ones.</summary>
    public static bool Failed(int status) => status > 0;

    [LibraryImport(Library, EntryPoint = "uplrules_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint OpenPluralRules(string locale, ref int status);

    [LibraryImport(Library, EntryPoint = "uplrules_select")]
    public static partial int SelectPluralKeyword(nint rules, double number, ref ushort keyword, int capacity, ref int status);

    [LibraryImport(Library, EntryPoint = "uplrules_close")]
    public static partial void ClosePluralRules(nint rules);
}
