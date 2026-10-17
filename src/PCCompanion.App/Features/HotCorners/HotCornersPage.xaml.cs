using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

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
}
