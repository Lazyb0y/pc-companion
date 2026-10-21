using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// A note on a settings page, which the user can't close: shown and informational unless the page
/// binds <see cref="InfoBar.IsOpen"/> or sets <see cref="InfoBar.Severity"/>, with an optional button.
/// </summary>
public sealed partial class Note : InfoBar
{
    public static readonly DependencyProperty ButtonContentProperty = DependencyProperty.Register(
        nameof(ButtonContent), typeof(object), typeof(Note), new PropertyMetadata(null, (sender, _) => ((Note)sender).OnButtonChanged()));

    public static readonly DependencyProperty ButtonCommandProperty = DependencyProperty.Register(
        nameof(ButtonCommand), typeof(ICommand), typeof(Note), new PropertyMetadata(null, (sender, _) => ((Note)sender).OnButtonChanged()));

    public Note()
    {
        IsClosable = false;
        IsOpen = true;
    }

    /// <summary>The button's text, or null for no button.</summary>
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

    private void OnButtonChanged()
    {
        if (ButtonContent is null)
        {
            ClearValue(ActionButtonProperty);
            return;
        }

        if (ActionButton is not Button button)
        {
            button = new Button();
            ActionButton = button;
        }

        button.Content = ButtonContent;
        button.Command = ButtonCommand;
    }
}
