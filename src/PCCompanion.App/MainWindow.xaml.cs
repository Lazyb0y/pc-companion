using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.Pages;
using PCCompanion.Localization;

namespace PCCompanion.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppDisplayName;
        Navigation.SelectedItem = HomeItem;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.SelectedItemContainer == HomeItem ? typeof(HomePage)
            : args.SelectedItemContainer == GeneralItem ? typeof(GeneralPage)
            : null;

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
