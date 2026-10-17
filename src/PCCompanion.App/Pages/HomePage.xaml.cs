using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.ViewModels;

namespace PCCompanion.App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    private void OnFeatureCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeatureCardViewModel card })
        {
            App.Current.MainWindow?.ShowFeature(card.Id);
        }
    }
}
