using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card whose icon is a glyph of the icon font. The card controls, such as
/// <see cref="SwitchCard"/>, derive from it.
/// </summary>
public partial class IconCard : SettingsCard
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconCard), new PropertyMetadata(string.Empty, (sender, _) => ((IconCard)sender).ShowIcon()));

    public static readonly DependencyProperty DrawnIconProperty = DependencyProperty.Register(
        nameof(DrawnIcon), typeof(string), typeof(IconCard), new PropertyMetadata(null, (sender, _) => ((IconCard)sender).ShowIcon()));

    /// <summary>The card's icon, a glyph of the icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>
    /// The name of a feature's drawing, such as <c>hot-corners</c>, which the card shows instead of
    /// <see cref="Glyph"/>; see <see cref="FeatureIcon"/>.
    /// </summary>
    public string? DrawnIcon
    {
        get => (string?)GetValue(DrawnIconProperty);
        set => SetValue(DrawnIconProperty, value);
    }

    // A card's icon is 20 pixels high.
    private void ShowIcon()
    {
        if (string.IsNullOrEmpty(DrawnIcon))
        {
            GlyphIcon.Set(this, HeaderIconProperty, Glyph);
        }
        else
        {
            FeatureIcon.Show(this, icon => HeaderIcon = icon, DrawnIcon, Glyph, 20);
        }
    }

    /// <summary>Gives <paramref name="control"/>, the card's control, the card's header as its screen reader name.</summary>
    protected void NameAfterHeader(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        RegisterPropertyChangedCallback(HeaderProperty, (_, _) => AutomationProperties.SetName(control, Header as string ?? string.Empty));
    }
}
