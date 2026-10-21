using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The 32-bit bitmaps that icons, text masks and layered windows are drawn on: pixels that this
/// program reads and writes directly, row by row from the top.
/// </summary>
internal static class TopDownBitmap
{
    /// <summary>
    /// Creates a bitmap of <paramref name="width"/> by <paramref name="height"/> pixels for
    /// <paramref name="dc"/>, or for the screen with <see cref="HDC.Null"/>; <paramref name="bits"/>
    /// points to its pixels while the returned handle isn't disposed.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't create the bitmap.</exception>
    public static unsafe SafeHandle Create(HDC dc, int width, int height, out void* bits)
    {
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

        var bitmap = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, out bits, null, 0);
        if (bitmap.IsInvalid)
        {
            bitmap.Dispose();
            throw new Win32Exception();
        }

        return bitmap;
    }
}
