using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;

namespace PCCompanion.App.Features.NetworkProfiles;

public sealed partial class NetworkProfilesPage : Page
{
    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.NetworkProfilesDeleteTitle);

    public NetworkProfilesPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<NetworkProfilesViewModel>();
        InitializeComponent();

        // In use follows Windows' settings only while the page shows.
        Loaded += (_, _) => ViewModel.Start();
        Unloaded += (_, _) => ViewModel.Stop();
    }

    public NetworkProfilesViewModel ViewModel { get; }

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    private async void OnAddClick(object sender, RoutedEventArgs e) => await AddAsync(new NetworkProfile());

    private async void OnAddCurrentClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.CurrentSettingsAsync() is { } current)
        {
            await AddAsync(current);
        }
    }

    private async void OnUseClick(object sender, RoutedEventArgs e) => await ViewModel.UseAsync(IndexOf(sender));

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Profiles.Count)
        {
            return;
        }

        var editor = new NetworkProfileEditor(ViewModel.Profiles[index].Profile, isNew: false, ViewModel.NamesExcept(index));
        if (await ShowAsync(new NetworkProfileDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Replace(index, editor.ToProfile());
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => ViewModel.Move(IndexOf(sender), 1);

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Profiles.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, DeleteTitleFormat, ViewModel.Profiles[index].Name),
            PrimaryButtonText = Strings.NetworkProfilesDelete,
            CloseButtonText = Strings.NetworkProfilesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.Delete(index);
        }
    }

    private async Task AddAsync(NetworkProfile profile)
    {
        var editor = new NetworkProfileEditor(profile, isNew: true, ViewModel.NamesExcept(-1));
        if (await ShowAsync(new NetworkProfileDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Add(editor.ToProfile());
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
