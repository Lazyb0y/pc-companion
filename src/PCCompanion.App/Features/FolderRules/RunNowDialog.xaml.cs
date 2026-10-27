using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>
/// Run now: the files in a folder now that its rules would act on, each with a box, all checked.
/// Run hands the checked ones to the host, and the dialog stays open, saying why, when it can't.
/// </summary>
public sealed partial class RunNowDialog : ContentDialog
{
    public RunNowDialog(RunNowViewModel list)
    {
        List = list;
        InitializeComponent();
        Opened += async (_, _) => await List.LoadAsync();
    }

    public RunNowViewModel List { get; }

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
