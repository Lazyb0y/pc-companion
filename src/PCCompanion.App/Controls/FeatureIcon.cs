using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace PCCompanion.App.Controls;

/// <summary>
/// A feature's icon: its drawing, from the images <c>scripts/feature-icons.ps1</c> makes in
/// <c>Assets/Icons</c>, or its glyph of the icon font for a feature that has no drawing. A drawing
/// is shown from the image made for the display scale, and in high contrast as its one-color
/// version in the text color, as glyphs are.
/// </summary>
internal static class FeatureIcon
{
    // The sizes feature-icons.ps1 makes each image at: 16, 20 and 32 pixels at 100%, 125%, 150%
    // and 200% display scale.
    private static readonly int[] PixelSizes = [16, 20, 24, 25, 30, 32, 40, 48, 64];

    // Says whether high contrast is on. Its own event can't be subscribed to in a desktop app.
    private static readonly AccessibilitySettings Accessibility = new();

    // Kept for its event, which Windows raises when the system's colors change, as they do when
    // high contrast is turned on or off, and stops once the object is collected.
    private static readonly UISettings Colors = new();

    private static readonly ConditionalWeakTable<FrameworkElement, Shown> ShownOn = [];

    /// <summary>
    /// Shows the icon of a feature on <paramref name="owner"/>, which puts it in place with
    /// <paramref name="setIcon"/>: its drawing <paramref name="drawing"/>, such as <c>hot-corners</c>,
    /// or <paramref name="glyph"/> when that's null, <paramref name="size"/> pixels high at 100%
    /// display scale. A drawing's icon is made again when the display scale or high contrast
    /// changes, since a <see cref="BitmapIcon"/> whose image changes after it's shown goes blank.
    /// </summary>
    public static void Show(FrameworkElement owner, Action<IconElement> setIcon, string? drawing, string glyph, double size) =>
        ShownOn.GetValue(owner, element => new Shown(element)).Show(setIcon, drawing, glyph, size);

    /// <summary>
    /// The pixel size of the image for an icon <paramref name="size"/> pixels high at
    /// <paramref name="scale"/>: the smallest that's at least as big, or the biggest.
    /// </summary>
    public static int PixelSize(double size, double scale)
    {
        var pixels = (int)Math.Round(size * scale);
        foreach (var made in PixelSizes)
        {
            if (made >= pixels)
            {
                return made;
            }
        }

        return PixelSizes[^1];
    }

    private static T Hidden<T>(T icon)
        where T : IconElement
    {
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    // The icon shown on one element, and what makes it again.
    private sealed class Shown
    {
        private readonly FrameworkElement _owner;
        private readonly TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> _scaleChanged;
        private readonly TypedEventHandler<UISettings, object> _colorsChanged;
        private Action<IconElement>? _setIcon;
        private string? _drawing;
        private double _size;
        private XamlRoot? _root;
        private Uri? _image;

        public Shown(FrameworkElement owner)
        {
            _owner = owner;
            // The display scale changes when the window moves to another display, and high
            // contrast with the system's colors, which Windows reports on another thread.
            _scaleChanged = (_, _) => ShowDrawing(again: false);
            _colorsChanged = (_, _) => _owner.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ShowDrawing(again: false));
            owner.Loaded += (_, _) =>
            {
                Watch();
                ShowDrawing(again: false);
            };
            owner.Unloaded += (_, _) => StopWatching();
        }

        public void Show(Action<IconElement> setIcon, string? drawing, string glyph, double size)
        {
            _setIcon = setIcon;
            _drawing = drawing;
            _size = size;
            if (string.IsNullOrEmpty(drawing))
            {
                _image = null;
                setIcon(Hidden(new FontIcon { Glyph = glyph, FontSize = size }));
            }
            else
            {
                ShowDrawing(again: true);
                if (_owner.IsLoaded)
                {
                    Watch();
                }
            }
        }

        private void ShowDrawing(bool again)
        {
            if (_setIcon is null || string.IsNullOrEmpty(_drawing))
            {
                return;
            }

            var highContrast = Accessibility.HighContrast;
            var pixels = PixelSize(_size, _owner.XamlRoot?.RasterizationScale ?? 1).ToString(CultureInfo.InvariantCulture);
            var image = new Uri($"ms-appx:///Assets/Icons/{_drawing}-{pixels}{(highContrast ? "-mono" : string.Empty)}.png");
            if (again || image != _image)
            {
                _image = image;
                _setIcon(Hidden(new BitmapIcon { UriSource = image, ShowAsMonochrome = highContrast, Width = _size, Height = _size }));
            }
        }

        private void Watch()
        {
            if (_root is not null || string.IsNullOrEmpty(_drawing))
            {
                return;
            }

            _root = _owner.XamlRoot;
            _root.Changed += _scaleChanged;
            Colors.ColorValuesChanged += _colorsChanged;
        }

        private void StopWatching()
        {
            if (_root is null)
            {
                return;
            }

            _root.Changed -= _scaleChanged;
            Colors.ColorValuesChanged -= _colorsChanged;
            _root = null;
        }
    }
}
