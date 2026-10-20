using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.SystemMeters;

public sealed partial class SystemMetersPage : Page
{
    public SystemMetersPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SystemMetersViewModel>();
        InitializeComponent();
    }

    public SystemMetersViewModel ViewModel { get; }
}
