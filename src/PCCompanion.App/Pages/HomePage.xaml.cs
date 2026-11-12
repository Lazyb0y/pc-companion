using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.ViewModels;

namespace PCCompanion.App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();
        ViewModel.FeatureOpenRequested += (_, id) => App.Current.MainWindow?.ShowFeature(id);
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }
}
