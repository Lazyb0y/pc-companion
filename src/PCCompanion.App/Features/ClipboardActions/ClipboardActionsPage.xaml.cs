using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;

namespace PCCompanion.App.Features.ClipboardActions;

public sealed partial class ClipboardActionsPage : Page
{
    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.ClipboardActionsDeleteTitle);

    public ClipboardActionsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ClipboardActionsViewModel>();
        InitializeComponent();
    }

    public ClipboardActionsViewModel ViewModel { get; }

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var editor = new ClipboardRuleEditor(null);
        if (await ShowAsync(new ClipboardRuleDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Add(editor.ToRule());
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Rules.Count)
        {
            return;
        }

        var editor = new ClipboardRuleEditor(ViewModel.Rules[index].Rule);
        if (await ShowAsync(new ClipboardRuleDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Replace(index, editor.ToRule());
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), 1);

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Rules.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, DeleteTitleFormat, ViewModel.Rules[index].Name),
            PrimaryButtonText = Strings.ClipboardActionsDelete,
            CloseButtonText = Strings.ClipboardActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.Delete(index);
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
