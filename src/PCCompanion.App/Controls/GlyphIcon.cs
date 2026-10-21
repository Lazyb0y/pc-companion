using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// Sets the icon of a card or a group of cards from a glyph of the icon font.
/// </summary>
internal static class GlyphIcon
{
    /// <summary>Sets <paramref name="iconProperty"/> of <paramref name="owner"/> to <paramref name="glyph"/>, or clears it when there's none.</summary>
    public static void Set(DependencyObject owner, DependencyProperty iconProperty, string? glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            owner.ClearValue(iconProperty);
        }
        else
        {
            owner.SetValue(iconProperty, new FontIcon { Glyph = glyph });
        }
    }
}
