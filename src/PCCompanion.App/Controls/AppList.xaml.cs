using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Controls;

/// <summary>
/// A page's list of apps, under a header and a description: each app with its icon and a Remove
/// button, and Add an app, which adds a running app or a program file.
/// </summary>
public sealed partial class AppList : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(AppList), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(AppList), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AppsProperty = DependencyProperty.Register(
        nameof(Apps), typeof(AppListViewModel), typeof(AppList), new PropertyMetadata(null));

    public AppList()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>What the listed apps get, below the header.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The page's list.</summary>
    public AppListViewModel? Apps
    {
        get => (AppListViewModel?)GetValue(AppsProperty);
        set => SetValue(AppsProperty, value);
    }

    private async void OnAddRunningAppClick(object sender, RoutedEventArgs e)
    {
        if (Apps is not { } apps)
        {
            return;
        }

        var dialog = new RunningAppDialog(apps.Lists)
        {
            XamlRoot = XamlRoot,
            FlowDirection = FlowDirection,
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Chosen is { } app)
        {
            apps.Add(app);
        }
    }

    // Only the full path of an .exe file can be an app's program. Whether it exists, and its name,
    // are read off the UI thread.
    private async void OnAddProgramFileClick(object sender, RoutedEventArgs e)
    {
        if (Apps is not { } apps
            || App.Current.MainWindow is not { } window
            || OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.ProgramDialogTitle, Strings.ProgramDialogFilter, "*.exe") is not { } program)
        {
            return;
        }

        var name = await Task.Run(() => ProgramFile.Check(program, File.Exists) == ProgramProblem.None ? AppDisplay.NameOfProgram(program) : null).ConfigureAwait(true);
        if (name is not null)
        {
            apps.Add(new ListedApp { Name = name, Program = program });
        }
    }

    private void OnRemoveAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            Apps?.Remove(index);
        }
    }
}
