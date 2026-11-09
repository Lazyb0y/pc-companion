using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.EndApp;

public sealed partial class EndAppPage : Page
{
    public EndAppPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<EndAppViewModel>();
        InitializeComponent();
    }

    public EndAppViewModel ViewModel { get; }
}
