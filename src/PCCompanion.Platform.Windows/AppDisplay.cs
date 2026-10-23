using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The name and icon that Windows shows for an app: from the Windows Apps folder for an app ID,
/// or from the program file. The shell can take a while to read them, so read them off a window's
/// thread.
/// </summary>
public static class AppDisplay
{
    /// <summary>
    /// The app's name in the Windows Apps folder, such as "Photos", or <see langword="null"/> when
    /// the folder has no app with that ID.
    /// </summary>
    public static string? NameOfAppId(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);

        if (AppsFolderItem(appId) is not { } item)
        {
            return null;
        }

        try
        {
            return DisplayName(item);
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// The program's description, such as "Character Map", or its file name without its extension
    /// when it has none or can't be read.
    /// </summary>
    public static string NameOfProgram(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description))
            {
                return description;
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or ArgumentException)
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// The icon of the app in the Windows Apps folder, about <paramref name="size"/> pixels square,
    /// or <see langword="null"/> when the folder has no app with that ID.
    /// </summary>
    public static AppIcon? IconOfAppId(string appId, int size)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);

        return IconOf(AppsFolderItem(appId), size);
    }

    /// <summary>
    /// The program's icon, about <paramref name="size"/> pixels square, or <see langword="null"/>
    /// when it can't be read, for example because the program isn't there.
    /// </summary>
    public static AppIcon? IconOfProgram(string path, int size)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return IconOf(FileItem(path), size);
    }

    private static unsafe IShellItem? AppsFolderItem(string appId)
    {
        var folder = PInvoke.FOLDERID_AppsFolder;
        var iid = typeof(IShellItem).GUID;
        fixed (char* name = appId)
        {
            return PInvoke.SHCreateItemInKnownFolder(&folder, 0, name, &iid, out var item).Succeeded ? item as IShellItem : null;
        }
    }

    private static unsafe IShellItem? FileItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        fixed (char* name = path)
        {
            return PInvoke.SHCreateItemFromParsingName(name, null, &iid, out var item).Succeeded ? item as IShellItem : null;
        }
    }

    private static unsafe string? DisplayName(IShellItem item)
    {
        PWSTR name;
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, &name);
        }
        catch (COMException)
        {
            return null;
        }

        try
        {
            var text = name.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name.Value);
        }
    }

    private static unsafe AppIcon? IconOf(IShellItem? item, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        if (item is null)
        {
            return null;
        }

        try
        {
            HBITMAP bitmap;
            try
            {
                ((IShellItemImageFactory)item).GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY, &bitmap);
            }
            catch (Exception exception) when (exception is COMException or InvalidCastException)
            {
                return null;
            }

            try
            {
                return Read(bitmap);
            }
            finally
            {
                PInvoke.DeleteObject(bitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    // Reads the bitmap's pixels as premultiplied blue, green, red and alpha bytes, row by row from
    // the top. The shell's icons usually have premultiplied alpha; ones that don't, or that have
    // no alpha at all, are converted.
    private static unsafe AppIcon? Read(HBITMAP bitmap)
    {
        BITMAP header;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &header) == 0 || header.bmWidth <= 0 || header.bmHeight <= 0)
        {
            return null;
        }

        var width = header.bmWidth;
        var height = Math.Abs(header.bmHeight);
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            },
        };

        var pixels = new byte[width * height * 4];
        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* bits = pixels)
            {
                if (PInvoke.GetDIBits(screen, bitmap, 0, (uint)height, bits, &info, DIB_USAGE.DIB_RGB_COLORS) != height)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }

        Premultiply(pixels);
        return new AppIcon(width, height, pixels);
    }

    private static void Premultiply(byte[] pixels)
    {
        var hasAlpha = false;
        var isPremultiplied = true;
        for (var at = 0; at < pixels.Length; at += 4)
        {
            var alpha = pixels[at + 3];
            hasAlpha |= alpha != 0;
            isPremultiplied &= pixels[at] <= alpha && pixels[at + 1] <= alpha && pixels[at + 2] <= alpha;
        }

        for (var at = 0; at < pixels.Length; at += 4)
        {
            if (!hasAlpha)
            {
                pixels[at + 3] = 255;
            }
            else if (!isPremultiplied)
            {
                var alpha = pixels[at + 3];
                pixels[at] = (byte)(pixels[at] * alpha / 255);
                pixels[at + 1] = (byte)(pixels[at + 1] * alpha / 255);
                pixels[at + 2] = (byte)(pixels[at + 2] * alpha / 255);
            }
        }
    }
}

/// <summary>
/// An app's icon as pixels: premultiplied blue, green, red and alpha bytes, row by row from the
/// top, as WinUI's bitmaps take them.
/// </summary>
public sealed record AppIcon(int Width, int Height, ReadOnlyMemory<byte> Pixels);
