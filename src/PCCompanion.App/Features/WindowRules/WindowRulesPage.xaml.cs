using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.WindowRules;

namespace PCCompanion.App.Features.WindowRules;

public sealed partial class WindowRulesPage : Page
{
    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.WindowRulesDeleteTitle);

    public WindowRulesPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<WindowRulesViewModel>();
        InitializeComponent();
    }

    public WindowRulesViewModel ViewModel { get; }

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (await EditAsync(new WindowRule(), isNew: true, ViewModel.AllRules) is { } rule)
        {
            ViewModel.Add(rule);
        }

        AddRuleButton.Focus(FocusState.Programmatic);
    }

    private async void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0)
        {
            return;
        }

        var (rule, others) = ViewModel.RuleAt(index);
        if (await EditAsync(rule, isNew: false, others) is { } edited)
        {
            ViewModel.Replace(index, edited);
        }
    }

    // The editor shows again after a dialog it hid for choosing an app or a window.
    private async Task<WindowRule?> EditAsync(WindowRule rule, bool isNew, IReadOnlyList<WindowRule> others)
    {
        var editor = new WindowRuleEditor(rule, isNew, others);
        var loading = editor.LoadAsync();
        while (true)
        {
            var dialog = new WindowRuleDialog(editor) { XamlRoot = XamlRoot };
            var result = await dialog.ShowAsync();
            if (dialog.Pending is { } pending)
            {
                await loading;
                await pending(XamlRoot);
                continue;
            }

            return result == ContentDialogResult.Primary && editor.CanSave ? editor.ToRule() : null;
        }
    }

    private void OnRuleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: int index } toggle)
        {
            ViewModel.SetEnabled(index, toggle.IsOn);
        }
    }

    private async void OnApplyNowClick(object sender, RoutedEventArgs e) => await ViewModel.ApplyNowAsync(IndexOf(sender));

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), 1);

    private async void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0)
        {
            return;
        }

        var (rule, _) = ViewModel.RuleAt(index);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ReadingOrder.Format(DeleteTitleFormat, rule.Name.Length > 0 ? rule.Name : Strings.WindowRulesUnnamed),
            Content = Strings.WindowRulesDeleteText,
            PrimaryButtonText = Strings.WindowRulesDelete,
            CloseButtonText = Strings.WindowRulesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.Delete(index);
            AddRuleButton.Focus(FocusState.Programmatic);
        }
    }
}
