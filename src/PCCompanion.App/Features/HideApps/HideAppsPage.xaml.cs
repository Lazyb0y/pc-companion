using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.HideApps;

public sealed partial class HideAppsPage : Page
{
    public HideAppsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HideAppsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.StartShortcutStatus();
        Unloaded += (_, _) => ViewModel.StopShortcutStatus();
    }

    public HideAppsViewModel ViewModel { get; }
}
