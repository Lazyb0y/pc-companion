using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.WindowShading;

public sealed partial class WindowShadingPage : Page
{
    public WindowShadingPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<WindowShadingViewModel>();
        InitializeComponent();
    }

    public WindowShadingViewModel ViewModel { get; }
}
