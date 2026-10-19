using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.AppWindows;

public sealed partial class AppWindowsPage : Page
{
    public AppWindowsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<AppWindowsViewModel>();
        InitializeComponent();
    }

    public AppWindowsViewModel ViewModel { get; }
}
