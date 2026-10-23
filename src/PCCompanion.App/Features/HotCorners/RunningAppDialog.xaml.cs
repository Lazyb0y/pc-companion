using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Core;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.HotCorners;

/// <summary>An app in the Running app dialog.</summary>
public sealed class RunningAppItem(ExcludedApp app, ImageSource? image)
{
    public ExcludedApp App { get; } = app;

    /// <summary>The app's icon, or <see langword="null"/> for the plain app icon.</summary>
    public ImageSource? Image { get; } = image;

    public Visibility PlainIconVisibility => Image is null ? Visibility.Visible : Visibility.Collapsed;

    public string Name => App.Name;

    // The list's items are named after this for screen readers.
    public override string ToString() => Name;
}

/// <summary>
/// Lists the apps with a window open, without PC Companion and the apps already listed, so one
/// can be added to the apps that turn corners off. The windows, names and icons are read off the
/// UI thread.
/// </summary>
public sealed partial class RunningAppDialog : ContentDialog
{
    private readonly HotCornersViewModel _page;

    public RunningAppDialog(HotCornersViewModel page)
    {
        _page = page;
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    public ObservableCollection<RunningAppItem> Items { get; } = [];

    /// <summary>The chosen app, with its name, or <see langword="null"/>.</summary>
    public ExcludedApp? Chosen => (Apps.SelectedItem as RunningAppItem)?.App;

    private async Task LoadAsync()
    {
        string[] leftOut = [Environment.ProcessPath ?? string.Empty, HostProcess.ExecutablePath];
        var running = await Task.Run(() => RunningApps.Of(TopLevelWindows.List(), new WindowsAppIdentityReader(), leftOut)).ConfigureAwait(true);
        var unlisted = running.Where(app => !_page.Lists(app.App)).ToList();
        var looks = await Task.Run(() => unlisted
            .Select(app => (App: app.App with { Name = AppLooks.ReadName(app.App, app.Program) }, Icon: AppLooks.ReadIcon(app.App, app.Program)))
            .OrderBy(app => app.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList()).ConfigureAwait(true);

        foreach (var (app, icon) in looks)
        {
            Items.Add(new RunningAppItem(app, await AppLooks.ToImageAsync(icon).ConfigureAwait(true)));
        }

        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        if (Items.Count == 0)
        {
            NoApps.Visibility = Visibility.Visible;
            return;
        }

        Apps.Visibility = Visibility.Visible;
        Apps.SelectedIndex = 0;
        Apps.Focus(FocusState.Programmatic);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => IsPrimaryButtonEnabled = Apps.SelectedItem is not null;
}
