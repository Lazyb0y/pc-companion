using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.TitleBarActions;

public sealed partial class TitleBarActionsPage : Page
{
    public TitleBarActionsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<TitleBarActionsViewModel>();
        InitializeComponent();
    }

    public TitleBarActionsViewModel ViewModel { get; }
}
