using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.FolderRules;

public sealed partial class FolderRulesPage : Page
{
    private const string FolderMoreId = "FolderMore";
    private const string RuleMoreId = "RuleMore";
    private const string RuleEditId = "RuleEdit";

    private static readonly CompositeFormat RemoveTitleFormat = CompositeFormat.Parse(Strings.FolderRulesRemoveTitle);
    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.FolderRulesDeleteTitle);

    // How long the host gets to act on the first of the files Run now handed it, before the
    // history is shown again.
    private static readonly TimeSpan RunNowWait = TimeSpan.FromSeconds(3);

    public FolderRulesPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<FolderRulesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.History.LoadAsync();
    }

    public FolderRulesViewModel ViewModel { get; }

    // A rule's switch and menu items carry "folder:rule".
    private static (int Folder, int Rule) RuleOf(object sender) =>
        sender is FrameworkElement { Tag: string tag } && tag.Split(':') is [var folder, var rule]
        && int.TryParse(folder, NumberStyles.None, CultureInfo.InvariantCulture, out var folderIndex)
        && int.TryParse(rule, NumberStyles.None, CultureInfo.InvariantCulture, out var ruleIndex)
            ? (folderIndex, ruleIndex)
            : (-1, -1);

    private void OnAddKnownFolderClick(object sender, RoutedEventArgs e)
    {
        var path = (sender as FrameworkElement)?.Tag switch
        {
            "Downloads" => KnownFolders.Downloads,
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            _ => null,
        };

        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.AddFolder(path);
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is { } window && OpenDialog.PickFolder(WindowNative.GetWindowHandle(window), Strings.FolderRulesBrowseTitle) is { } path)
        {
            ViewModel.AddFolder(path);
        }
    }

    private void OnFolderToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: int index } toggle)
        {
            ViewModel.SetFolderEnabled(index, toggle.IsOn);
        }
    }

    private async void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Folders.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, RemoveTitleFormat, ViewModel.Folders[index].Name),
            Content = Strings.FolderRulesRemoveText,
            PrimaryButtonText = Strings.FolderRulesRemove,
            CloseButtonText = Strings.FolderRulesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            // Focus goes to the next folder, or the one before the last.
            ViewModel.RemoveFolder(index);
            Focus(FolderMoreId, Math.Min(index, ViewModel.Folders.Count - 1));
        }
    }

    // The menu item's automation ID names the template, and its tag the folder.
    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: int folder } item || folder >= ViewModel.Folders.Count
            || !Enum.TryParse<RuleTemplate>(AutomationProperties.GetAutomationId(item), out var template))
        {
            return;
        }

        var path = ViewModel.PathOf(folder);
        var editor = new FolderRuleEditor(FolderRuleTemplates.Create(template, path), isNew: true, path, ViewModel.Places);
        if (await ShowAsync(new FolderRuleDialog(editor)) == ContentDialogResult.Primary)
        {
            // Focus goes to the new rule, at the end of its folder's list.
            ViewModel.AddRule(folder, editor.ToRule());
            Focus(RuleEditId, FormattableString.Invariant($"{folder}:{ViewModel.Folders[folder].Rules.Count - 1}"));
        }
    }

    private async void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        var (folder, rule) = RuleOf(sender);
        if (folder < 0 || folder >= ViewModel.Folders.Count || rule >= ViewModel.Folders[folder].Rules.Count)
        {
            return;
        }

        var path = ViewModel.PathOf(folder);
        var editor = new FolderRuleEditor(ViewModel.RuleAt(folder, rule), isNew: false, path, ViewModel.Places);
        if (await ShowAsync(new FolderRuleDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.ReplaceRule(folder, rule, editor.ToRule());
            Focus(RuleEditId, FormattableString.Invariant($"{folder}:{rule}"));
        }
    }

    private void OnRuleToggled(object sender, RoutedEventArgs e)
    {
        var (folder, rule) = RuleOf(sender);
        if (folder >= 0 && sender is ToggleSwitch toggle)
        {
            ViewModel.SetRuleEnabled(folder, rule, toggle.IsOn);
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(sender, 1);

    // Focus stays on the rule that moves.
    private void Move(object sender, int offset)
    {
        var (folder, rule) = RuleOf(sender);
        ViewModel.MoveRule(folder, rule, offset);
        Focus(RuleMoreId, FormattableString.Invariant($"{folder}:{rule + offset}"));
    }

    private async void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        var (folder, rule) = RuleOf(sender);
        if (folder < 0 || folder >= ViewModel.Folders.Count || rule >= ViewModel.Folders[folder].Rules.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, DeleteTitleFormat, ViewModel.Folders[folder].Rules[rule].Name),
            PrimaryButtonText = Strings.FolderRulesDelete,
            CloseButtonText = Strings.FolderRulesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.DeleteRule(folder, rule);
            Focus(FolderMoreId, folder);
        }
    }

    private async void OnRunNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Folders.Count)
        {
            return;
        }

        var list = new RunNowViewModel(ViewModel.PathOf(index), ViewModel.Folders[index].Name);
        if (await ShowAsync(new RunNowDialog(list)) == ContentDialogResult.Primary)
        {
            await Task.Delay(RunNowWait);
            await ViewModel.History.RanAsync();
        }
    }

    private async void OnRefreshHistoryClick(object sender, RoutedEventArgs e) => await ViewModel.History.LoadAsync();

    private void OnOpenRecycleBinClick(object sender, RoutedEventArgs e) => FolderRulesHistoryViewModel.OpenRecycleBin();

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.FolderRulesClearHistoryTitle,
            Content = Strings.FolderRulesClearHistoryText,
            PrimaryButtonText = Strings.FolderRulesClearHistory,
            CloseButtonText = Strings.FolderRulesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.History.ClearAsync();
        }
    }

    // Undo can wait for Windows' question about a copy too big for the Recycle Bin. The entry's
    // Undo is gone once the history is shown again, so focus goes to Refresh.
    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            await ViewModel.History.UndoAsync(index);
            RefreshHistoryButton.Focus(FocusState.Programmatic);
        }
    }

    private void OnShowMoreClick(object sender, RoutedEventArgs e) => ViewModel.History.ShowMore();

    // The list is built again after a change, so focus goes to the button given in the new one, or
    // to Add folder when it's gone. It moves once the dialog or menu that made the change has put
    // the focus back where it was, which is gone.
    private void Focus(string id, object tag) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            FolderList.UpdateLayout();
            Control target = FindButton(FolderList, id, tag) is { } button ? button : AddFolderButton;
            target.Focus(FocusState.Programmatic);
        });

    private static Button? FindButton(DependencyObject parent, string id, object tag)
    {
        for (var child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++)
        {
            var element = VisualTreeHelper.GetChild(parent, child);
            if (element is Button button && AutomationProperties.GetAutomationId(button) == id && Equals(button.Tag, tag))
            {
                return button;
            }

            if (FindButton(element, id, tag) is { } found)
            {
                return found;
            }
        }

        return null;
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
