using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.MinimizeToTray;

public sealed partial class MinimizeToTrayPage : Page
{
    public MinimizeToTrayPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<MinimizeToTrayViewModel>();
        InitializeComponent();
    }

    public MinimizeToTrayViewModel ViewModel { get; }
}
