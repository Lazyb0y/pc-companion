using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

public sealed partial class ShortcutsAndMouseButtonsPage : Page
{
    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsDeleteTitle);

    public ShortcutsAndMouseButtonsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ShortcutsAndMouseButtonsViewModel>();
        InitializeComponent();

        // Which shortcuts Windows refused is read only while the page is shown.
        Loaded += (_, _) => ViewModel.StartShortcutStatus();
        Unloaded += (_, _) => ViewModel.StopShortcutStatus();
    }

    public ShortcutsAndMouseButtonsViewModel ViewModel { get; }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var item = sender is FrameworkElement { Tag: int index } ? ViewModel.Triggers.FirstOrDefault(trigger => trigger.Index == index) : null;
        if (item is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, DeleteTitleFormat, item.Input),
            PrimaryButtonText = Strings.ShortcutsAndMouseButtonsDelete,
            CloseButtonText = Strings.ShortcutsAndMouseButtonsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.Delete(item.Index);
        }
    }

    // A dialog shows in the page's window and follows its language's direction.
    private Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.FlowDirection = FlowDirection;
        dialog.RequestedTheme = ActualTheme;
        return dialog.ShowAsync().AsTask();
    }
}
