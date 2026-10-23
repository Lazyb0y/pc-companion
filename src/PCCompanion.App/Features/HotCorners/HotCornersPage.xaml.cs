using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.HotCorners;

public sealed partial class HotCornersPage : Page
{
    // The width the corner picture needs: two corners and the screen between them, with the spacing.
    private const double PictureWidth = 220 + 12 + 200 + 12 + 220;

    private bool _cornersListed;

    public HotCornersPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HotCornersViewModel>();
        InitializeComponent();

        // The corner picture is always left to right; the text in each corner follows the language.
        Loaded += (_, _) =>
        {
            foreach (var corner in new[] { TopLeftCorner, TopRightCorner, BottomLeftCorner, BottomRightCorner })
            {
                corner.FlowDirection = FlowDirection;
            }
        };
    }

    public HotCornersViewModel ViewModel { get; }

    // Without room for the picture, as in a narrow window, the corners are listed one below another,
    // without the screen, and follow the language's direction.
    private void OnCornersPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var listed = e.NewSize.Width < PictureWidth;
        if (listed == _cornersListed)
        {
            return;
        }

        _cornersListed = listed;
        if (listed)
        {
            Corners.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            Corners.RowDefinitions.RemoveAt(Corners.RowDefinitions.Count - 1);
        }

        Corners.HorizontalAlignment = listed ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Corners.FlowDirection = listed ? FlowDirection : FlowDirection.LeftToRight;
        Screen.Visibility = listed ? Visibility.Collapsed : Visibility.Visible;
        Place(TopLeftCorner, 0, 0);
        Place(TopRightCorner, listed ? 1 : 0, listed ? 0 : 2);
        Place(BottomLeftCorner, 2, 0);
        Place(BottomRightCorner, listed ? 3 : 2, listed ? 0 : 2);
    }

    private static void Place(FrameworkElement corner, int row, int column)
    {
        Grid.SetRow(corner, row);
        Grid.SetColumn(corner, column);
    }

    private async void OnAddRunningAppClick(object sender, RoutedEventArgs e)
    {
        var dialog = new RunningAppDialog(ViewModel)
        {
            XamlRoot = XamlRoot,
            FlowDirection = FlowDirection,
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Chosen is { } app)
        {
            ViewModel.AddExcludedApp(app);
        }
    }

    // Only the full path of an .exe file can be the app in front.
    private async void OnAddProgramFileClick(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is not { } window
            || OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.HotCornersProgramDialogTitle, Strings.HotCornersProgramFilter, "*.exe") is not { } program
            || ProgramFile.Check(program, File.Exists) != ProgramProblem.None)
        {
            return;
        }

        var name = await Task.Run(() => AppDisplay.NameOfProgram(program)).ConfigureAwait(true);
        ViewModel.AddExcludedApp(new ExcludedApp { Name = name, Program = program });
    }

    private void OnRemoveAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            ViewModel.RemoveExcludedApp(index);
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CornerViewModel corner } && App.Current.MainWindow is { } window)
        {
            var program = OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.HotCornersProgramDialogTitle, Strings.HotCornersProgramFilter, "*.exe");
            if (program is not null)
            {
                corner.Program = program;
            }
        }
    }
}
