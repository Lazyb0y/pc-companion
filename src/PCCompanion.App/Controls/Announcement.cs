using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// Has screen readers announce a text block's text as it appears or changes, without moving focus
/// to it, such as a problem below a box: set <c>Announcement.IsLive="True"</c> on the text block.
/// A text that's hidden or empty isn't announced, and the same text isn't announced twice in a row.
/// </summary>
public static class Announcement
{
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.RegisterAttached(
        "IsLive", typeof(bool), typeof(Announcement), new PropertyMetadata(false, OnIsLiveChanged));

    // The text announced last, or an empty string once the text was hidden.
    private static readonly DependencyProperty AnnouncedProperty = DependencyProperty.RegisterAttached(
        "Announced", typeof(string), typeof(Announcement), new PropertyMetadata(string.Empty));

    public static bool GetIsLive(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsLiveProperty);
    }

    public static void SetIsLive(TextBlock element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsLiveProperty, value);
    }

    private static void OnIsLiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock text || e.NewValue is not true)
        {
            return;
        }

        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Announce(text));
        text.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Announce(text));
    }

    // Once the bindings have set both the text and whether it shows, which change one at a time.
    private static void Announce(TextBlock text) => text.DispatcherQueue.TryEnqueue(() =>
    {
        var shown = text.Visibility == Visibility.Visible ? text.Text : string.Empty;
        if (shown == (string)text.GetValue(AnnouncedProperty))
        {
            return;
        }

        text.SetValue(AnnouncedProperty, shown);
        if (shown.Length > 0)
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    });
}
