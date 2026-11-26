using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.HotCorners;

public sealed partial class HotCornersPage : Page
{
    // The width the picture needs: a corner, an edge and a corner, with the spacing.
    private const double PictureWidth = 220 + 12 + 220 + 12 + 220;

    // How many rows the list needs, one for each corner and edge, beside the picture's three.
    private const int ListRows = 8;

    private bool _cornersListed;

    public HotCornersPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<HotCornersViewModel>();
        InitializeComponent();

        // The picture is always left to right; the text in each corner and edge follows the language.
        Loaded += (_, _) =>
        {
            foreach (var place in Places)
            {
                place.FlowDirection = FlowDirection;
            }

            ViewModel.StartResults();
        };

        // The recent results are read only while the page is shown.
        Unloaded += (_, _) => ViewModel.StopResults();
    }

    public HotCornersViewModel ViewModel { get; }

    // The corners and edges in reading order, for the list.
    private ContentControl[] Places => [TopLeftCorner, TopEdge, TopRightCorner, LeftEdge, RightEdge, BottomLeftCorner, BottomEdge, BottomRightCorner];

    // Without room for the picture, as in a narrow window, the corners and edges are listed one below
    // another, without the screen, and follow the language's direction.
    private void OnCornersPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var listed = e.NewSize.Width < PictureWidth;
        if (listed == _cornersListed)
        {
            return;
        }

        _cornersListed = listed;
        while (Corners.RowDefinitions.Count < (listed ? ListRows : 3))
        {
            Corners.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        while (Corners.RowDefinitions.Count > (listed ? ListRows : 3))
        {
            Corners.RowDefinitions.RemoveAt(Corners.RowDefinitions.Count - 1);
        }

        Corners.HorizontalAlignment = listed ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Corners.FlowDirection = listed ? FlowDirection : FlowDirection.LeftToRight;
        Screen.Visibility = listed ? Visibility.Collapsed : Visibility.Visible;
        if (listed)
        {
            var row = 0;
            foreach (var place in Places)
            {
                Place(place, row++, 0);
            }

            return;
        }

        Place(TopLeftCorner, 0, 0);
        Place(TopEdge, 0, 1);
        Place(TopRightCorner, 0, 2);
        Place(LeftEdge, 1, 0);
        Place(RightEdge, 1, 2);
        Place(BottomLeftCorner, 2, 0);
        Place(BottomEdge, 2, 1);
        Place(BottomRightCorner, 2, 2);
    }

    private static void Place(FrameworkElement corner, int row, int column)
    {
        Grid.SetRow(corner, row);
        Grid.SetColumn(corner, column);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CornerViewModel corner } && App.Current.MainWindow is { } window)
        {
            var program = OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.ProgramDialogTitle, Strings.ProgramDialogFilter, "*.exe");
            if (program is not null)
            {
                corner.Program = program;
            }
        }
    }
}
