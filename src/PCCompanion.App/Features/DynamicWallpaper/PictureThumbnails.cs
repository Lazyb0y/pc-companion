using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PCCompanion.App.Features.DynamicWallpaper;

/// <summary>
/// Windows' own thumbnails of pictures and folders, the ones File Explorer shows, which Windows
/// makes and keeps; the page never reads a picture itself. Call it on the UI thread.
/// </summary>
public static class PictureThumbnails
{
    // The thumbnail's width in pixels; the page shows it at 96 by 54 effective pixels.
    private const uint Size = 192;

    /// <summary>The thumbnail of a picture or folder; <see langword="null"/> when Windows has none, as for one that's gone.</summary>
    public static async Task<ImageSource?> LoadAsync(string path, bool folder)
    {
        try
        {
            StorageItemThumbnail? thumbnail = folder
                ? await (await StorageFolder.GetFolderFromPathAsync(path)).GetThumbnailAsync(ThumbnailMode.PicturesView, Size)
                : await (await StorageFile.GetFileFromPathAsync(path)).GetThumbnailAsync(ThumbnailMode.PicturesView, Size, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null)
            {
                return null;
            }

            using (thumbnail)
            {
                var image = new BitmapImage();
                await image.SetSourceAsync(thumbnail);
                return image;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            return null;
        }
    }
}
