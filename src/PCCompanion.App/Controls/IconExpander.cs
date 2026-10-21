using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;

namespace PCCompanion.App.Controls;

/// <summary>
/// A group of settings cards that opens and closes, with its icon from a glyph of the icon font
/// like the cards' own.
/// </summary>
public sealed partial class IconExpander : SettingsExpander
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconExpander), new PropertyMetadata(string.Empty, (sender, e) => GlyphIcon.Set(sender, HeaderIconProperty, (string)e.NewValue)));

    /// <summary>The group's icon, a glyph of the icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
