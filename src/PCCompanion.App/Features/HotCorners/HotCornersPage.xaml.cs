using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.HotCorners;

public sealed partial class HotCornersPage : Page
{
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
