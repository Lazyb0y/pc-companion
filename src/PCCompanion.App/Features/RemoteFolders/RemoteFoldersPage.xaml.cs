using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.App.Features.RemoteFolders;

public sealed partial class RemoteFoldersPage : Page
{
    // The automation ID of a server's More options button, where focus goes after an edit.
    private const string MoreId = "More";

    private static readonly CompositeFormat RemoveTitleFormat = CompositeFormat.Parse(Strings.RemoteFoldersRemoveTitle);
    private static readonly CompositeFormat TrustTitleFormat = CompositeFormat.Parse(Strings.RemoteFoldersTrustTitle);

    // Where keyboard focus goes once the server list is rebuilt, if it was in the list.
    private ServerButton? _focus;

    public RemoteFoldersPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<RemoteFoldersViewModel>();
        InitializeComponent();

        // The servers' states are asked for only while the page shows.
        Loaded += (_, _) => ViewModel.Start();
        Unloaded += (_, _) => ViewModel.Stop();

        // Rebuilding the list replaces its buttons, which would send keyboard focus back to the
        // start of the window: it goes to the same button of the same server instead.
        ViewModel.ServersRebuilding += (_, _) => _focus ??= FocusedServerButton();
        ViewModel.ServersRebuilt += (_, _) =>
        {
            if (_focus is { } focus)
            {
                _focus = null;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FocusServerButton(focus));
            }
        };

        // A server's new state is read out, wherever the focus is.
        ViewModel.StateChanged += (_, state) =>
            (FrameworkElementAutomationPeer.FromElement(ServerList) ?? FrameworkElementAutomationPeer.CreatePeerForElement(ServerList))?
                .RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, state, "RemoteFoldersState");
    }

    public RemoteFoldersViewModel ViewModel { get; }

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    // A server's button, by its automation ID, if it shows and can be used.
    private static Button? FindButton(DependencyObject parent, string id)
    {
        for (int child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++)
        {
            var element = VisualTreeHelper.GetChild(parent, child);
            if (element is Button { Visibility: Visibility.Visible, IsEnabled: true } button && AutomationProperties.GetAutomationId(button) == id)
            {
                return button;
            }

            if (FindButton(element, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private ServerButton? FocusedServerButton() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button { Tag: int index } button
            && AutomationProperties.GetAutomationId(button) is { Length: > 0 } id
            ? new ServerButton(index, id)
            : null;

    // The server's button, or its More options when that one is gone; Add server when no server is left.
    private void FocusServerButton(ServerButton focus)
    {
        ServerList.UpdateLayout();
        int index = Math.Min(focus.Index, ViewModel.Servers.Count - 1);
        Control? target = AddServerButton;
        if (index >= 0 && ServerList.ContainerFromIndex(index) is DependencyObject container)
        {
            target = FindButton(container, focus.Id) ?? FindButton(container, MoreId);
        }

        target?.Focus(FocusState.Programmatic);
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        using var editor = NewEditor(null, -1);
        if (await ShowAsync(new RemoteServerDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Add(editor.ToServer(), editor.Secret);
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e) => await EditAsync(IndexOf(sender), signInAgain: false);

    private async void OnSignInAgainClick(object sender, RoutedEventArgs e) => await EditAsync(IndexOf(sender), signInAgain: true);

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ViewModel.OpenFolder(IndexOf(sender));

    private async void OnSyncNowClick(object sender, RoutedEventArgs e) => await ViewModel.SyncNowAsync(IndexOf(sender));

    private async void OnTrustKeyClick(object sender, RoutedEventArgs e)
    {
        int index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Servers.Count || ViewModel.Servers[index] is not { PresentedKey: not null } item)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, TrustTitleFormat, item.Name),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Strings.RemoteFoldersTrustMessage, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = item.KeyChange, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new FontFamily("Consolas") },
                },
            },
            PrimaryButtonText = Strings.RemoteFoldersTrustKey,
            CloseButtonText = Strings.RemoteFoldersCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            _focus = new ServerButton(index, MoreId);
            await ViewModel.TrustNewKeyAsync(index);
            _focus = null;
        }
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        int index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Servers.Count)
        {
            return;
        }

        var item = ViewModel.Servers[index];
        string message = Strings.RemoteFoldersRemoveMessage;
        if (item.ChangesWaiting > 0)
        {
            message += Environment.NewLine + Environment.NewLine + PluralStrings.Format("RemoteFoldersRemoveChangesWaiting", item.ChangesWaiting);
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, RemoveTitleFormat, item.Name),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = item.ChangesWaiting > 0 ? Strings.RemoteFoldersRemoveAnyway : Strings.RemoteFoldersRemove,
            CloseButtonText = Strings.RemoteFoldersCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            // Focus goes to the next server, or the one before the last.
            _focus = new ServerButton(index, MoreId);
            ViewModel.Remove(index);
            _focus = null;
        }
    }

    private async Task EditAsync(int index, bool signInAgain)
    {
        if (index < 0 || index >= ViewModel.Servers.Count)
        {
            return;
        }

        var server = ViewModel.Servers[index].Server;
        using var editor = NewEditor(server, index, signInAgain);
        if (await ShowAsync(new RemoteServerDialog(editor)) != ContentDialogResult.Primary)
        {
            return;
        }

        _focus = new ServerButton(index, MoreId);
        ViewModel.Replace(index, editor.ToServer(), editor.Secret);
        _focus = null;
        if (signInAgain || editor.Secret.Change)
        {
            await ViewModel.SignInAgainAsync(server);
        }
    }

    private RemoteServerEditor NewEditor(RemoteServer? server, int index, bool signInAgain = false) => new(
        server,
        ViewModel.NamesExcept(index),
        ViewModel.CanConnect,
        RemoteFoldersViewModel.AskAsync,
        () => server is null ? null : ViewModel.SavedSecret(server),
        File.Exists,
        signInAgain);

    // A dialog shows in the page's window and follows its language's direction.
    private Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.FlowDirection = FlowDirection;
        dialog.RequestedTheme = ActualTheme;
        return dialog.ShowAsync().AsTask();
    }

    // A button of the server at Index in the list, by its automation ID.
    private readonly record struct ServerButton(int Index, string Id);
}
