using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.MinimizeToTray;

public sealed partial class MinimizeToTrayPage : Page
{
    public MinimizeToTrayPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<MinimizeToTrayViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.StartShortcutStatus();
        Unloaded += (_, _) => ViewModel.StopShortcutStatus();
    }

    public MinimizeToTrayViewModel ViewModel { get; }
}
