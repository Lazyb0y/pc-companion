using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.LauncherActions;

public sealed partial class LauncherActionsPage : Page
{
    public LauncherActionsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<LauncherActionsViewModel>();
        InitializeComponent();
    }

    public LauncherActionsViewModel ViewModel { get; }
}
