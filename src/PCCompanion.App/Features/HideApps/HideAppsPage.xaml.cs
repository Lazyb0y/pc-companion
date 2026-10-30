using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.HideApps;

public sealed partial class HideAppsPage : Page
{
    public HideAppsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HideAppsViewModel>();
        InitializeComponent();
    }

    public HideAppsViewModel ViewModel { get; }
}
