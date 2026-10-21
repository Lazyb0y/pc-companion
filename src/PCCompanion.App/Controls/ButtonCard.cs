using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card for an action: its icon, header and description, and a button, with an optional
/// second button before it and a progress ring while the action runs. Screen readers name each
/// button by its text.
/// </summary>
public sealed partial class ButtonCard : IconCard
{
    public static readonly DependencyProperty ButtonContentProperty = DependencyProperty.Register(
        nameof(ButtonContent), typeof(object), typeof(ButtonCard), new PropertyMetadata(null, (sender, e) => ((ButtonCard)sender)._button.Content = e.NewValue));

    public static readonly DependencyProperty ButtonCommandProperty = DependencyProperty.Register(
        nameof(ButtonCommand), typeof(ICommand), typeof(ButtonCard), new PropertyMetadata(null, (sender, e) => ((ButtonCard)sender)._button.Command = (ICommand)e.NewValue));

    public static readonly DependencyProperty SecondButtonContentProperty = DependencyProperty.Register(
        nameof(SecondButtonContent), typeof(object), typeof(ButtonCard), new PropertyMetadata(null, (sender, e) => ((ButtonCard)sender)._secondButton.Content = e.NewValue));

    public static readonly DependencyProperty SecondButtonCommandProperty = DependencyProperty.Register(
        nameof(SecondButtonCommand), typeof(ICommand), typeof(ButtonCard), new PropertyMetadata(null, (sender, e) => ((ButtonCard)sender)._secondButton.Command = (ICommand)e.NewValue));

    public static readonly DependencyProperty IsSecondButtonShownProperty = DependencyProperty.Register(
        nameof(IsSecondButtonShown), typeof(bool), typeof(ButtonCard), new PropertyMetadata(false, (sender, e) => ((ButtonCard)sender)._secondButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public static readonly DependencyProperty IsWorkingProperty = DependencyProperty.Register(
        nameof(IsWorking), typeof(bool), typeof(ButtonCard), new PropertyMetadata(false, (sender, e) => ((ButtonCard)sender).OnIsWorkingChanged((bool)e.NewValue)));

    private readonly ProgressRing _progress = new() { Width = 20, Height = 20, Margin = new Thickness(0, 0, 4, 0), IsActive = false, Visibility = Visibility.Collapsed };
    private readonly Button _secondButton = new() { Visibility = Visibility.Collapsed };
    private readonly Button _button = new();

    public ButtonCard()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_progress);
        buttons.Children.Add(_secondButton);
        buttons.Children.Add(_button);
        Content = buttons;
    }

    /// <summary>The button's text.</summary>
    public object? ButtonContent
    {
        get => GetValue(ButtonContentProperty);
        set => SetValue(ButtonContentProperty, value);
    }

    /// <summary>What the button does.</summary>
    public ICommand? ButtonCommand
    {
        get => (ICommand?)GetValue(ButtonCommandProperty);
        set => SetValue(ButtonCommandProperty, value);
    }

    /// <summary>The second button's text.</summary>
    public object? SecondButtonContent
    {
        get => GetValue(SecondButtonContentProperty);
        set => SetValue(SecondButtonContentProperty, value);
    }

    /// <summary>What the second button does.</summary>
    public ICommand? SecondButtonCommand
    {
        get => (ICommand?)GetValue(SecondButtonCommandProperty);
        set => SetValue(SecondButtonCommandProperty, value);
    }

    /// <summary>Whether the second button shows, before the button.</summary>
    public bool IsSecondButtonShown
    {
        get => (bool)GetValue(IsSecondButtonShownProperty);
        set => SetValue(IsSecondButtonShownProperty, value);
    }

    /// <summary>Whether the action is running, which shows a progress ring before the buttons.</summary>
    public bool IsWorking
    {
        get => (bool)GetValue(IsWorkingProperty);
        set => SetValue(IsWorkingProperty, value);
    }

    private void OnIsWorkingChanged(bool working)
    {
        _progress.IsActive = working;
        _progress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
    }
}
