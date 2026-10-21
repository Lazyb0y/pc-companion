using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card for a setting that's on or off: its icon, header and description, and a switch
/// that screen readers name after the header.
/// </summary>
public sealed partial class SwitchCard : SettingsCard
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SwitchCard), new PropertyMetadata(string.Empty, (sender, e) => ((SwitchCard)sender).OnGlyphChanged((string)e.NewValue)));

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(SwitchCard), new PropertyMetadata(false, (sender, e) => ((SwitchCard)sender)._switch.IsOn = (bool)e.NewValue));

    public static readonly DependencyProperty IsSwitchEnabledProperty = DependencyProperty.Register(
        nameof(IsSwitchEnabled), typeof(bool), typeof(SwitchCard), new PropertyMetadata(true, (sender, e) => ((SwitchCard)sender)._switch.IsEnabled = (bool)e.NewValue));

    private readonly ToggleSwitch _switch = new();

    public SwitchCard()
    {
        Content = _switch;
        _switch.Toggled += (_, _) => IsOn = _switch.IsOn;
        RegisterPropertyChangedCallback(HeaderProperty, (_, _) => AutomationProperties.SetName(_switch, Header as string ?? string.Empty));
    }

    /// <summary>The card's icon, a glyph of the icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Whether the setting is on; the switch changes it.</summary>
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>Whether the switch can be used. The card's header and description stay as they are.</summary>
    public bool IsSwitchEnabled
    {
        get => (bool)GetValue(IsSwitchEnabledProperty);
        set => SetValue(IsSwitchEnabledProperty, value);
    }

    private void OnGlyphChanged(string glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            ClearValue(HeaderIconProperty);
        }
        else
        {
            HeaderIcon = new FontIcon { Glyph = glyph };
        }
    }
}
