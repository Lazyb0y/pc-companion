using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using PCCompanion.App.Features;
using PCCompanion.App.Pages;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App;

public sealed partial class MainWindow : Window
{
    // The height of the row the pane's button takes at the top of a folded-away pane.
    private const double PaneButtonRowHeight = 48;

    // How long the window may stay out of sight while its content is drawn, in case it never is.
    private static readonly TimeSpan LongestHiddenStart = TimeSpan.FromSeconds(1);

    private static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PCCompanion.ico");

    private readonly AppSettings _settings;
    private readonly UpdateCheckWatcher _updateCheck;

    public MainWindow(AppSettings settings, UpdateCheckWatcher updateCheck)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(updateCheck);

        _settings = settings;
        _updateCheck = updateCheck;
        InitializeComponent();
        Title = Strings.AppDisplayName;
        AppWindow.SetIcon(IconPath);

        Navigation.FlowDirection = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        AddFeatures();
        Navigation.SelectedItem = HomeItem;
        Navigation.Loaded += (_, _) => PlacePageBelowPaneButton(Navigation.DisplayMode);
        Navigation.DisplayModeChanged += (_, e) => PlacePageBelowPaneButton(e.DisplayMode);
        SetBackdrop();
        HideWhileNotDrawn();

        ShowUpdateBadge();
        updateCheck.Changed += OnUpdateCheckChanged;
        updateCheck.Seen += OnUpdateCheckChanged;
        Closed += (_, _) =>
        {
            updateCheck.Changed -= OnUpdateCheckChanged;
            updateCheck.Seen -= OnUpdateCheckChanged;
        };
    }

    /// <summary>
    /// Shows the page of the feature with the ID <paramref name="featureId"/>.
    /// </summary>
    public void ShowFeature(string featureId)
    {
        foreach (var item in Navigation.MenuItems)
        {
            if (item is NavigationViewItem { Tag: CatalogFeature feature } && feature.Info.Id == featureId)
            {
                Navigation.SelectedItem = item;
                return;
            }
        }
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

        // Activate alone leaves a window that's already active in this app behind other apps' windows.
        Foreground.Bring(WindowNative.GetWindowHandle(this));
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

    // Windows shows the window when it's activated, but WinUI draws its content only after its first
    // layout pass, about 160 ms later on a typical PC, so the window would show empty until then.
    // It stays cloaked until its content has been drawn, or LongestHiddenStart has passed. When it
    // closes, it's cloaked first, so it isn't seen empty while its content is taken down.
    private void HideWhileNotDrawn()
    {
        var handle = WindowNative.GetWindowHandle(this);
        if (!WindowCloak.Set(handle, cloaked: true))
        {
            return;
        }

        var framesDrawn = 0;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = LongestHiddenStart;
        timer.IsRepeating = false;

        void Show()
        {
            CompositionTarget.Rendering -= OnRendering;
            timer.Stop();
            WindowCloak.Set(handle, cloaked: false);

            // Set again once the window can be seen, so the taskbar redraws its button: on its first
            // start after an install, the taskbar can show a generic icon until the icon changes.
            AppWindow.SetIcon(IconPath);
        }

        // One frame more than the first after the layout pass, so the content is on screen before the window.
        void OnRendering(object? sender, object e)
        {
            if (Navigation.IsLoaded && ++framesDrawn == 2)
            {
                Show();
            }
        }

        CompositionTarget.Rendering += OnRendering;
        timer.Tick += (_, _) => Show();
        timer.Start();
        Closed += (_, _) => WindowCloak.Set(handle, cloaked: true);
    }

    // Raised on other threads.
    private void OnUpdateCheckChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ShowUpdateBadge);

    // While the check is on, the badge on the General item shows a newer version the General page
    // hasn't shown yet. Screen readers don't read the badge, so the item's status says it. The
    // setting changes only on the General page, whose banner has cleared the badge by then.
    private void ShowUpdateBadge()
    {
        var version = _settings.Updates.CheckAutomatically ? _updateCheck.UnseenVersion(App.CurrentVersion) : null;
        UpdateBadge.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetItemStatus(
            GeneralItem,
            version is { } newer ? string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.GeneralUpdateStatusAvailable), newer) : string.Empty);
    }

    // While the pane is folded away, its button sits over the top-left of the page, so the page
    // starts below the button's row, as in Windows Settings.
    private void PlacePageBelowPaneButton(NavigationViewDisplayMode displayMode) =>
        ContentFrame.Margin = displayMode == NavigationViewDisplayMode.Minimal ? new Thickness(0, PaneButtonRowHeight, 0, 0) : default;

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

    // A section per module, with an item per feature, below Home.
    private void AddFeatures()
    {
        foreach (var module in FeatureCatalog.Modules)
        {
            Navigation.MenuItems.Add(new NavigationViewItemHeader { Content = Localized.GetString(module.NameKey) });
            foreach (var feature in module.Features)
            {
                Navigation.MenuItems.Add(new NavigationViewItem
                {
                    Content = Localized.GetString(feature.Info.NameKey),
                    Icon = new FontIcon { Glyph = feature.Info.Glyph },
                    Tag = feature,
                });
            }
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.SelectedItemContainer == HomeItem ? typeof(HomePage)
            : args.SelectedItemContainer == GeneralItem ? typeof(GeneralPage)
            : args.SelectedItemContainer?.Tag is CatalogFeature feature ? feature.PageType
            : null;

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
