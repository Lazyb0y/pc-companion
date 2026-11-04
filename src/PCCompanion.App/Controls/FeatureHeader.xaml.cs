using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// The top of a feature page: the feature's icon, name and description, its on/off switch, and a
/// note when a change on the page couldn't be saved.
/// </summary>
public sealed partial class FeatureHeader : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(FeatureHeader), new PropertyMetadata(string.Empty, (sender, _) => ((FeatureHeader)sender).ShowIcon()));

    public static readonly DependencyProperty DrawnIconProperty = DependencyProperty.Register(
        nameof(DrawnIcon), typeof(string), typeof(FeatureHeader), new PropertyMetadata(null, (sender, _) => ((FeatureHeader)sender).ShowIcon()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FeatureHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(FeatureHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(FeatureHeader), new PropertyMetadata(false));

    public static readonly DependencyProperty SaveFailedProperty = DependencyProperty.Register(
        nameof(SaveFailed), typeof(bool), typeof(FeatureHeader), new PropertyMetadata(false));

    public FeatureHeader()
    {
        InitializeComponent();
    }

    /// <summary>The feature's icon, a glyph of the icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>
    /// The name of the feature's drawing, such as <c>hot-corners</c>, which the header shows instead
    /// of <see cref="Glyph"/>; see <see cref="FeatureIcon"/>.
    /// </summary>
    public string? DrawnIcon
    {
        get => (string?)GetValue(DrawnIconProperty);
        set => SetValue(DrawnIconProperty, value);
    }

    /// <summary>The feature's name, which is also the name screen readers give the switch.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What the feature does, below its name.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Whether the feature is on; the switch changes it.</summary>
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>True to show that a change on the page couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => (bool)GetValue(SaveFailedProperty);
        set => SetValue(SaveFailedProperty, value);
    }

    // The header's icon is 32 pixels high.
    private void ShowIcon() => FeatureIcon.Show(this, icon => IconHost.Child = icon, DrawnIcon, Glyph, 32);
}
