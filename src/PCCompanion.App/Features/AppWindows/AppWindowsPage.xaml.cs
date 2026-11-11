using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.AppWindows;

public sealed partial class AppWindowsPage : Page
{
    public AppWindowsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<AppWindowsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.StartShortcutStatus();
        Unloaded += (_, _) => ViewModel.StopShortcutStatus();
    }

    public AppWindowsViewModel ViewModel { get; }
}
