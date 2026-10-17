using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.ViewModels;

namespace PCCompanion.App.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<GeneralViewModel>();

        // A new theme applies right away.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneralViewModel.SelectedTheme))
            {
                App.Current.MainWindow?.ApplyTheme(ViewModel.SelectedTheme.Theme);
            }
        };

        InitializeComponent();
    }

    public GeneralViewModel ViewModel { get; }
}
