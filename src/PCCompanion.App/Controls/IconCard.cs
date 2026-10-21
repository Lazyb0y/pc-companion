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
        nameof(Glyph), typeof(string), typeof(IconCard), new PropertyMetadata(string.Empty, (sender, e) => GlyphIcon.Set(sender, HeaderIconProperty, (string)e.NewValue)));

    /// <summary>The card's icon, a glyph of the icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gives <paramref name="control"/>, the card's control, the card's header as its screen reader name.</summary>
    protected void NameAfterHeader(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        RegisterPropertyChangedCallback(HeaderProperty, (_, _) => AutomationProperties.SetName(control, Header as string ?? string.Empty));
    }
}
