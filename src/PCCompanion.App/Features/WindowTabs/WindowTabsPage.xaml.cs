using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.WindowTabs;

public sealed partial class WindowTabsPage : Page
{
    public WindowTabsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<WindowTabsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.StartShortcutStatus();
        Unloaded += (_, _) => ViewModel.StopShortcutStatus();
    }

    public WindowTabsViewModel ViewModel { get; }
}
