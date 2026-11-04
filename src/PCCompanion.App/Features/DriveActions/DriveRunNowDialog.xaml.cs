using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>
/// Run now: what a connected drive's actions would do now, the imports' files each with a box,
/// those imported before unchecked. Run hands the checked ones to the host, and the dialog stays
/// open, saying why, when it can't.
/// </summary>
public sealed partial class DriveRunNowDialog : ContentDialog
{
    public DriveRunNowDialog(DriveRunNowViewModel list)
    {
        List = list;
        InitializeComponent();
        Opened += async (_, _) => await List.LoadAsync();
    }

    public DriveRunNowViewModel List { get; }

    private async void OnRunClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await List.RunAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
