using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using Windows.Globalization.NumberFormatting;
using WinRT.Interop;

namespace PCCompanion.App.Features.DynamicWallpaper;

public sealed partial class DynamicWallpaperPage : Page
{
    // How often the page asks the host for its state while it's open.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherQueueTimer _refresh;

    public DynamicWallpaperPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<DynamicWallpaperViewModel>();
        InitializeComponent();
        LatitudeBox.NumberFormatter = CoordinateFormatter();
        LongitudeBox.NumberFormatter = CoordinateFormatter();
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = RefreshInterval;
        _refresh.Tick += async (_, _) => await ViewModel.RefreshHostAsync();
        Loaded += async (_, _) =>
        {
            _refresh.Start();
            await ViewModel.LookAsync();
        };
        Unloaded += (_, _) => _refresh.Stop();
    }

    public DynamicWallpaperViewModel ViewModel { get; }

    private static PeriodItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as PeriodItem;

    // Coordinates with one decimal, in the user's regional format.
    private static DecimalFormatter CoordinateFormatter() => new()
    {
        IntegerDigits = 1,
        FractionDigits = 1,
        NumberRounder = new IncrementNumberRounder { Increment = 0.1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero },
    };

    private static nint Owner() => App.Current.MainWindow is { } window ? WindowNative.GetWindowHandle(window) : 0;

    private void OnChoosePictureClick(object sender, RoutedEventArgs e)
    {
        var pattern = string.Join(';', DynamicWallpaperCheck.PictureExtensions.Select(extension => "*" + extension));
        if (ItemOf(sender) is { } item
            && OpenDialog.PickFile(Owner(), Strings.DynamicWallpaperPictureDialogTitle, Strings.DynamicWallpaperPictureDialogFilter, pattern) is { } picture)
        {
            ViewModel.ChoosePicture(item, picture);
        }
    }

    private void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && OpenDialog.PickFolder(Owner(), Strings.DynamicWallpaperFolderDialogTitle) is { } folder)
        {
            ViewModel.ChooseFolder(item, folder);
        }
    }

    private void OnChooseNoneClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.ChooseNone(item);
        }
    }

    private void OnChooseSameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.ChooseSame(item);
        }
    }

    private void OnAddTimeClick(object sender, RoutedEventArgs e) => ViewModel.AddTime();

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.RemoveTime(item);
        }
    }

    private void OnRemoveDisplayClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayGroup group)
        {
            ViewModel.RemoveDisplay(group);
        }
    }
}
