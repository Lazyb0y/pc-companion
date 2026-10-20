using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.ViewModels;

namespace PCCompanion.App.Pages;

public sealed partial class GeneralPage : Page
{
    private string _announcedUpdateStatus;

    public GeneralPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<GeneralViewModel>();
        _announcedUpdateStatus = ViewModel.UpdateStatus;

        // A new theme applies right away.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneralViewModel.SelectedTheme))
            {
                App.Current.MainWindow?.ApplyTheme(ViewModel.SelectedTheme.Theme);
            }
            else if (e.PropertyName == nameof(GeneralViewModel.UpdateStatus))
            {
                // Once the binding shows the new text.
                DispatcherQueue.TryEnqueue(AnnounceUpdateStatus);
            }
        };

        Unloaded += (_, _) => ViewModel.Dispose();
        InitializeComponent();
    }

    public GeneralViewModel ViewModel { get; }

    // Screen readers announce a new update status, such as when Check now finishes. The status is
    // raised again when nothing changed, which isn't announced.
    private void AnnounceUpdateStatus()
    {
        if (UpdateStatusText.Text == _announcedUpdateStatus)
        {
            return;
        }

        _announcedUpdateStatus = UpdateStatusText.Text;
        FrameworkElementAutomationPeer.CreatePeerForElement(UpdateStatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
