using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.DropDownApps;

public sealed partial class DropDownAppsPage : Page
{
    public DropDownAppsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<DropDownAppsViewModel>();
        InitializeComponent();

        // The recent results are read only while the page is shown.
        Loaded += (_, _) =>
        {
            ViewModel.StartResults();
            ViewModel.StartShortcutStatus();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.StopResults();
            ViewModel.StopShortcutStatus();
        };
    }

    public DropDownAppsViewModel ViewModel { get; }
}
