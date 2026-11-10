using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.MiddleClickPaste;

public sealed partial class MiddleClickPastePage : Page
{
    public MiddleClickPastePage()
    {
        ViewModel = App.Current.Services.GetRequiredService<MiddleClickPasteViewModel>();
        InitializeComponent();
    }

    public MiddleClickPasteViewModel ViewModel { get; }
}
