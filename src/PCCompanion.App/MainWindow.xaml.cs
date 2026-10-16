using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        Navigation.FlowDirection = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Navigation.SelectedItem = HomeItem;
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
