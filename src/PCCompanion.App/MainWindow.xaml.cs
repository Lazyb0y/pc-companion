using System;
using System.Globalization;
using System.IO;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using PCCompanion.App.Pages;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;

namespace PCCompanion.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppDisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PCCompanion.ico"));

        Navigation.FlowDirection = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Navigation.SelectedItem = HomeItem;
        SetBackdrop();
    }

    /// <summary>
    /// Shows the window in front of other windows, restored if it was minimized.
    /// </summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>
    /// Shows the window, including its title bar, in <paramref name="theme"/>.
    /// </summary>
    public void ApplyTheme(AppTheme theme)
    {
        Navigation.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    // Mica needs Windows 11; Windows 10 has desktop acrylic. Without either, for example in some
    // remote sessions, the window gets the solid colors Mica itself falls back to.
    private void SetBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else
        {
            SetSolidBackground();
            Navigation.ActualThemeChanged += (_, _) => SetSolidBackground();
        }
    }

    private void SetSolidBackground() =>
        Navigation.Background = new SolidColorBrush(Navigation.ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(255, 0x20, 0x20, 0x20)
            : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.SelectedItemContainer == HomeItem ? typeof(HomePage)
            : args.SelectedItemContainer == GeneralItem ? typeof(GeneralPage)
            : null;

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
