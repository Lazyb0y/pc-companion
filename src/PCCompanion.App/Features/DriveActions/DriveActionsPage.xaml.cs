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
using PCCompanion.Modules.Files.DriveActions;

namespace PCCompanion.App.Features.DriveActions;

public sealed partial class DriveActionsPage : Page
{
    private const string DriveMoreId = "DriveMore";
    private const string ActionMoreId = "ActionMore";
    private const string ActionEditId = "ActionEdit";

    private static readonly CompositeFormat RemoveTitleFormat = CompositeFormat.Parse(Strings.DriveActionsRemoveTitle);
    private static readonly CompositeFormat RenameTitleFormat = CompositeFormat.Parse(Strings.DriveActionsRenameTitle);
    private static readonly CompositeFormat UseThisDriveTitleFormat = CompositeFormat.Parse(Strings.DriveActionsUseThisDriveTitle);

    // How often the page asks the host which drives are connected while it's open.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    // How long the host gets to start the run Run now asked for, before the history is shown again.
    private static readonly TimeSpan RunNowWait = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueueTimer _refresh;

    public DriveActionsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<DriveActionsViewModel>();
        InitializeComponent();
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = RefreshInterval;
        _refresh.Tick += async (_, _) => await ViewModel.RefreshConnectedAsync();
        Loaded += async (_, _) =>
        {
            _refresh.Start();
            await ViewModel.RefreshConnectedAsync();
            await ViewModel.History.LoadAsync();
        };
        Unloaded += (_, _) => _refresh.Stop();
    }

    public DriveActionsViewModel ViewModel { get; }

    // An action's menu items carry "drive:action".
    private static (int Drive, int Action) ActionOf(object sender) =>
        sender is FrameworkElement { Tag: string tag } && tag.Split(':') is [var drive, var action]
        && int.TryParse(drive, NumberStyles.None, CultureInfo.InvariantCulture, out var driveIndex)
        && int.TryParse(action, NumberStyles.None, CultureInfo.InvariantCulture, out var actionIndex)
            ? (driveIndex, actionIndex)
            : (-1, -1);

    private async void OnAddDriveClick(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Drives.Count;
        if (await ShowAsync(new AddDriveDialog(new AddDriveViewModel(ViewModel))) == ContentDialogResult.Primary && ViewModel.Drives.Count > count)
        {
            // Focus goes to the new drive, at the end of the list.
            Focus(DriveMoreId, ViewModel.Drives.Count - 1);
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Drives.Count)
        {
            return;
        }

        var box = new TextBox
        {
            Header = Strings.DriveActionsNameHeader,
            Text = ViewModel.Drives[index].Name,
            MaxLength = DriveActionsSettings.MaximumNameLength,
        };
        var problem = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, RenameTitleFormat, ViewModel.Drives[index].Name),
            Content = new StackPanel { Spacing = 8, MinWidth = 320, Children = { box, problem } },
            PrimaryButtonText = Strings.DriveActionsRename,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        void Check()
        {
            problem.Text = ViewModel.CheckName(index, box.Text);
            AutomationProperties.SetHelpText(box, problem.Text);
            dialog.IsPrimaryButtonEnabled = problem.Text.Length == 0;
        }

        box.TextChanged += (_, _) => Check();
        dialog.Opened += (_, _) =>
        {
            box.SelectAll();
            box.Focus(FocusState.Programmatic);
        };
        Check();
        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.Rename(index, box.Text);
            Focus(DriveMoreId, index);
        }
    }

    private async void OnRemoveDriveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Drives.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, RemoveTitleFormat, ViewModel.Drives[index].Name),
            Content = Strings.DriveActionsRemoveText,
            PrimaryButtonText = Strings.DriveActionsRemove,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            // Focus goes to the next drive, or the one before the last.
            ViewModel.RemoveDrive(index);
            Focus(DriveMoreId, Math.Min(index, ViewModel.Drives.Count - 1));
        }
    }

    // Recording another drive's serial numbers lets it receive this drive's backups, so the user
    // confirms that it's theirs.
    private async void OnUseThisDriveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Drives.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, UseThisDriveTitleFormat, ViewModel.Drives[index].Name),
            Content = Strings.DriveActionsUseThisDriveText,
            PrimaryButtonText = Strings.DriveActionsUseThisDrive,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.UseThisDriveAsync(index);
            Focus(DriveMoreId, index);
        }
    }

    private async void OnAddAgainClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            await ViewModel.AddAgainAsync(index);
            Focus(DriveMoreId, index);
        }
    }

    private void OnWhenConnectedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { Tag: int index } box)
        {
            ViewModel.SetWhenConnected(index, box.SelectedIndex);
        }
    }

    private void OnEjectToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: int index } toggle)
        {
            ViewModel.SetEjectWhenDone(index, toggle.IsOn);
        }
    }

    private async void OnAddActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int drive } || drive >= ViewModel.Drives.Count)
        {
            return;
        }

        var editor = ViewModel.NewEditor(drive);
        if (await ShowAsync(new DriveActionDialog(editor)) == ContentDialogResult.Primary)
        {
            // Focus goes to the new action, at the end of the drive's list.
            ViewModel.AddAction(drive, editor.ToAction());
            FocusAction(drive, ViewModel.Drives[drive].Actions.Count - 1, ActionEditId);
        }
    }

    private async void OnEditActionClick(object sender, RoutedEventArgs e)
    {
        var (drive, action) = ActionOf(sender);
        if (drive < 0 || drive >= ViewModel.Drives.Count || action >= ViewModel.Drives[drive].Actions.Count)
        {
            return;
        }

        var editor = ViewModel.EditorFor(drive, action);
        if (await ShowAsync(new DriveActionDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.ReplaceAction(drive, action, editor.ToAction());
            FocusAction(drive, action, ActionEditId);
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(sender, 1);

    // Focus stays on the action that moves.
    private void Move(object sender, int offset)
    {
        var (drive, action) = ActionOf(sender);
        ViewModel.MoveAction(drive, action, offset);
        FocusAction(drive, action + offset, ActionMoreId);
    }

    private async void OnRemoveActionClick(object sender, RoutedEventArgs e)
    {
        var (drive, action) = ActionOf(sender);
        if (drive < 0 || drive >= ViewModel.Drives.Count || action >= ViewModel.Drives[drive].Actions.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Strings.DriveActionsRemoveActionTitle,
            Content = ViewModel.Drives[drive].Actions[action].Summary,
            PrimaryButtonText = Strings.DriveActionsRemove,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.RemoveAction(drive, action);
            Focus(DriveMoreId, drive);
        }
    }

    private async void OnRunNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index >= ViewModel.Drives.Count || ViewModel.RunNowFor(index) is not { } list)
        {
            return;
        }

        if (await ShowAsync(new DriveRunNowDialog(list)) == ContentDialogResult.Primary)
        {
            await ViewModel.RefreshConnectedAsync();
            await Task.Delay(RunNowWait);
            await ViewModel.History.RanAsync();
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopAsync();
        AddDriveButton.Focus(FocusState.Programmatic);
    }

    private async void OnRefreshHistoryClick(object sender, RoutedEventArgs e) => await ViewModel.History.LoadAsync();

    private void OnOpenRecycleBinClick(object sender, RoutedEventArgs e) => DriveActionsHistoryViewModel.OpenRecycleBin();

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.DriveActionsClearHistoryTitle,
            Content = Strings.DriveActionsClearHistoryText,
            PrimaryButtonText = Strings.DriveActionsClearHistory,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.History.ClearAsync();
        }
    }

    // Undo of a whole run asks first; Undo can wait for Windows' question about a copy too big for
    // the Recycle Bin. The run's Undo is gone once the history is shown again, so focus goes to Refresh.
    private async void OnUndoRunClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || ViewModel.History.RunAt(index) is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Strings.DriveActionsUndoRunTitle,
            Content = Strings.DriveActionsUndoRunText,
            PrimaryButtonText = Strings.DriveActionsUndo,
            CloseButtonText = Strings.DriveActionsCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await ViewModel.History.UndoAsync(index);
            RefreshHistoryButton.Focus(FocusState.Programmatic);
        }
    }

    private async void OnUndoFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && tag.Split(':', 2) is [var run, var path]
            && int.TryParse(run, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            await ViewModel.History.UndoAsync(index, path);
            RefreshHistoryButton.Focus(FocusState.Programmatic);
        }
    }

    private void OnShowMoreClick(object sender, RoutedEventArgs e) => ViewModel.History.ShowMore();

    // The list is built again after a change, so focus goes to the button given in the new one, or
    // to Add a drive when it's gone. It moves once the dialog or menu that made the change has put
    // the focus back where it was, which is gone.
    private void Focus(string id, object tag) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            DriveList.UpdateLayout();
            Control target = FindButton(DriveList, id, tag) is { } button ? button : AddDriveButton;
            target.Focus(FocusState.Programmatic);
        });

    // A drive's actions are made as they come into view, by the list in its expander, so the
    // action's is made first, wherever the page is scrolled.
    private void FocusAction(int drive, int action, string id) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            DriveList.UpdateLayout();
            var element = DriveList.ContainerFromIndex(drive) is DependencyObject container && Find<ItemsRepeater>(container) is { } actions
                && action >= 0 && action < ViewModel.Drives[drive].Actions.Count
                    ? actions.GetOrCreateElement(action)
                    : null;
            element?.UpdateLayout();
            Control target = element is not null && FindButton(element, id, FormattableString.Invariant($"{drive}:{action}")) is { } button ? button : AddDriveButton;
            target.Focus(FocusState.Programmatic);
        });

    private static T? Find<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++)
        {
            var element = VisualTreeHelper.GetChild(parent, child);
            if (element is T found)
            {
                return found;
            }

            if (Find<T>(element) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

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
