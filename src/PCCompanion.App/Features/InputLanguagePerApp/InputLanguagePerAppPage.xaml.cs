using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.InputLanguagePerApp;

public sealed partial class InputLanguagePerAppPage : Page
{
    public InputLanguagePerAppPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<InputLanguagePerAppViewModel>();
        InitializeComponent();
    }

    public InputLanguagePerAppViewModel ViewModel { get; }
}
