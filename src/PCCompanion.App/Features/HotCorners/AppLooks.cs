using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PCCompanion.App.Features.HotCorners;

/// <summary>
/// The names and icons of the apps on the Hot Corners page. The shell can take a while to read
/// them, so <see cref="ReadName"/> and <see cref="ReadIcon"/> run off the UI thread, and
/// <see cref="ToImageAsync"/> on it.
/// </summary>
internal static class AppLooks
{
    // Read large enough for 200% scale; the page shows it at the card's icon size.
    private const int IconSize = 48;

    /// <summary>A plain app icon, for an app whose own icon can't be read.</summary>
    public const string DefaultGlyph = ""; // AppIconDefault

    /// <summary>
    /// The app's name in the Windows Apps folder for an app ID, otherwise the description of
    /// <paramref name="program"/> or the app's own program, otherwise the app ID.
    /// </summary>
    public static string ReadName(ExcludedApp app, string? program)
    {
        if (app.AppId is { } appId)
        {
            return AppDisplay.NameOfAppId(appId) ?? (program is not null ? AppDisplay.NameOfProgram(program) : appId);
        }

        return app.Program is { } path ? AppDisplay.NameOfProgram(path) : string.Empty;
    }

    /// <summary>
    /// The app's icon in the Windows Apps folder for an app ID, otherwise the icon of
    /// <paramref name="program"/> or the app's own program.
    /// </summary>
    public static AppIcon? ReadIcon(ExcludedApp app, string? program) =>
        (app.AppId is { } appId ? AppDisplay.IconOfAppId(appId, IconSize) : null)
        ?? ((program ?? app.Program) is { } path ? AppDisplay.IconOfProgram(path, IconSize) : null);

    /// <summary>The icon as an image to show, or <see langword="null"/> when there's none.</summary>
    public static async Task<ImageSource?> ToImageAsync(AppIcon? icon)
    {
        if (icon is null)
        {
            return null;
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)icon.Width, (uint)icon.Height, 96, 96, icon.Pixels.ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);

        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    /// <summary>
    /// The icon to show on a card: the image, or the plain app icon when there's none. An
    /// <c>IconSourceElement</c> drew nothing there, so the card gets an icon element of its own.
    /// </summary>
    public static IconElement ToIconElement(ImageSource? image) =>
        image is not null ? new ImageIcon { Source = image } : new FontIcon { Glyph = DefaultGlyph };
}
