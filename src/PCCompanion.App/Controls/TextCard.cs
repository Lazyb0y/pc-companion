using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card for a value that's shown but not changed here, such as a fixed shortcut: its
/// icon, header and description, and the value as text.
/// </summary>
public sealed partial class TextCard : IconCard
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TextCard), new PropertyMetadata(string.Empty, (sender, e) => ((TextCard)sender)._text.Text = (string)e.NewValue));

    private readonly TextBlock _text = new();

    public TextCard()
    {
        Content = _text;
    }

    /// <summary>The value, as text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
