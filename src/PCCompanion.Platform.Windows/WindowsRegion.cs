using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>The user's region settings in Windows (Time &amp; language > Language &amp; region).</summary>
public static class WindowsRegion
{
    // Codes are two letters, such as "BD", or three digits, such as "419" for Latin America.
    private const int CodeLength = 8;

    /// <summary>
    /// The code of the user's <b>Country or region</b>: two letters from ISO 3166, such as
    /// <c>BD</c>, or three digits from UN M.49 for a wider region, such as <c>419</c>;
    /// <see langword="null"/> when Windows doesn't give one.
    /// </summary>
    public static unsafe string? CountryOrRegion()
    {
        var code = stackalloc char[CodeLength];
        var written = PInvoke.GetUserDefaultGeoName(code, CodeLength);
        return written > 1 ? new string(code, 0, written - 1) : null;
    }
}
