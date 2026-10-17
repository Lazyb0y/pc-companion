using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Core.Text;
using Windows.Foundation;

namespace PCCompanion.App.Controls;

/// <summary>
/// A text box for a file path. While it doesn't have focus and the path doesn't fit, it shows
/// the end of the path after an ellipsis, as in <c>…\System32\charmap.exe</c>, and its tooltip
/// shows the whole path. Screen readers always get the whole path.
/// </summary>
public sealed partial class PathBox : UserControl
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(PathBox), new PropertyMetadata(string.Empty, (sender, _) => ((PathBox)sender).OnPathChanged()));

    public static readonly DependencyProperty AccessibleNameProperty = DependencyProperty.Register(
        nameof(AccessibleName), typeof(string), typeof(PathBox), new PropertyMetadata(string.Empty, (sender, e) => AutomationProperties.SetName(((PathBox)sender).Box, (string)e.NewValue)));

    private readonly TextBlock _probe = new();
    private UIElement? _boxText;

    public PathBox()
    {
        InitializeComponent();

        // Edits are saved when the box loses focus, as with a bound text box.
        Box.GotFocus += (_, _) => Update();
        Box.LostFocus += (_, _) =>
        {
            Path = Box.Text;
            Update();
        };
        Box.SizeChanged += (_, _) => Update();
        Box.Loaded += (_, _) =>
        {
            _boxText = FindDescendant(Box, "ContentElement");
            Update();
        };
    }

    /// <summary>The path, saved when the user leaves the box.</summary>
    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>The name screen readers give the box.</summary>
    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    private void OnPathChanged()
    {
        if (Box.Text != Path)
        {
            Box.Text = Path ?? string.Empty;
        }

        ToolTipService.SetToolTip(Box, string.IsNullOrEmpty(Path) ? null : Path);
        Update();
    }

    private void Update()
    {
        var text = Box.Text;
        var width = Box.ActualWidth - Box.BorderThickness.Left - Box.BorderThickness.Right - Box.Padding.Left - Box.Padding.Right;
        var trimmed = Box.FocusState == FocusState.Unfocused && width > 0 && text.Length > 0
            ? EndTrimming.KeepEnd(text, width, Measure, @"\/")
            : text;

        var showTrimmed = trimmed != text;
        Trimmed.Text = showTrimmed ? trimmed : string.Empty;
        Trimmed.Margin = new Thickness(
            Box.BorderThickness.Left + Box.Padding.Left,
            Box.BorderThickness.Top + Box.Padding.Top,
            Box.BorderThickness.Right + Box.Padding.Right,
            0);
        Trimmed.Visibility = showTrimmed ? Visibility.Visible : Visibility.Collapsed;
        if (_boxText is not null)
        {
            _boxText.Opacity = showTrimmed ? 0 : 1;
        }
    }

    private double Measure(string text)
    {
        _probe.FontFamily = Box.FontFamily;
        _probe.FontSize = Box.FontSize;
        _probe.FontWeight = Box.FontWeight;
        _probe.Text = text;
        _probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _probe.DesiredSize.Width;
    }

    private static UIElement? FindDescendant(DependencyObject parent, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { Name: var childName } element && childName == name)
            {
                return element;
            }

            if (FindDescendant(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
