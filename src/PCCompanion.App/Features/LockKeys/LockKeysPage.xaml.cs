using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Features.LockKeys;

public sealed partial class LockKeysPage : Page
{
    public LockKeysPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<LockKeysViewModel>();
        InitializeComponent();
    }

    public LockKeysViewModel ViewModel { get; }
}
