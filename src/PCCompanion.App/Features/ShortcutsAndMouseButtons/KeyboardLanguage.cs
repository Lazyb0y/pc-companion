using System.Globalization;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>The language of a keyboard layout, as the AltGr warnings name it.</summary>
internal static class KeyboardLanguage
{
    /// <summary>The language of <paramref name="layout"/>, such as "Polish (Poland)", or <see langword="null"/> when it has no name.</summary>
    public static string? NameOf(nint layout)
    {
        try
        {
            return CultureInfo.GetCultureInfo((int)(layout & 0xFFFF)).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
