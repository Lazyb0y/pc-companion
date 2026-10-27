using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>
/// The name dialog of Add from current setup and Rename: the profile's name, and the setup it
/// holds. Save is available only for a name the profile can have.
/// </summary>
public sealed partial class DisplayProfileNameDialog : ContentDialog
{
    public DisplayProfileNameDialog(DisplayProfileNameEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public DisplayProfileNameEditor Editor { get; }
}
