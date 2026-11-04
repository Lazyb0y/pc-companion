using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>
/// Add a drive: Camera cards and the drives connected now, each with its button. Add adds the
/// chosen one, and the dialog stays open, saying why, when it can't.
/// </summary>
public sealed partial class AddDriveDialog : ContentDialog
{
    public AddDriveDialog(AddDriveViewModel list)
    {
        List = list;
        InitializeComponent();
        Opened += async (_, _) => await List.LoadAsync();
    }

    public AddDriveViewModel List { get; }

    private void OnChoiceChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: int index })
        {
            List.Select(index);
        }
    }

    private async void OnAddClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await List.AddAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
