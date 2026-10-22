using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.NetworkProfiles;

/// <summary>
/// The profile editor: a dialog laid out like Windows' Proxy page. Save is available only for a
/// profile that can be used.
/// </summary>
public sealed partial class NetworkProfileDialog : ContentDialog
{
    public NetworkProfileDialog(NetworkProfileEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public NetworkProfileEditor Editor { get; }
}
