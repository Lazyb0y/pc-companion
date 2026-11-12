using System;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card whose icon is a glyph of the icon font. The card controls, such as
/// <see cref="SwitchCard"/>, derive from it. A card that does something when clicked sets
/// <c>IsClickEnabled</c> and a <c>Command</c>, not a <c>Click</c> handler: a screen reader opens
/// the card through its automation peer, which runs the command and can't raise <c>Click</c>.
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

    protected override AutomationPeer OnCreateAutomationPeer() => new IconCardAutomationPeer(this);

    /// <summary>
    /// The toolkit card's peer, which offers screen readers Invoke on a clickable card but has
    /// nothing to carry it out, so invoking the card failed. This one runs the card's command, as a
    /// click does.
    /// </summary>
    private sealed partial class IconCardAutomationPeer(IconCard owner) : SettingsCardAutomationPeer(owner), IInvokeProvider
    {
        // UIA_E_ELEMENTNOTENABLED, which UI Automation gives a screen reader as "not enabled".
        private const int ElementNotEnabled = unchecked((int)0x80040200);

        public void Invoke()
        {
            if (!IsEnabled() || Owner is not IconCard { IsClickEnabled: true } card)
            {
                Marshal.ThrowExceptionForHR(ElementNotEnabled, new IntPtr(-1));
                return;
            }

            if (ListenerExists(AutomationEvents.InvokePatternOnInvoked))
            {
                RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
            }

            if (card.Command is { } command && command.CanExecute(card.CommandParameter))
            {
                command.Execute(card.CommandParameter);
            }
        }
    }
}
