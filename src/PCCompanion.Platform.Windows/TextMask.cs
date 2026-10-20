using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Text drawn by GDI as coverage, from 0 for none to 255 for full, so it can be painted in any
/// color over a transparent background. It's smoothed in gray: ClearType needs a known background.
/// </summary>
/// <param name="Width">The width of the text's box, in pixels.</param>
/// <param name="Height">The height of the font's line box, which has room above and below the letters.</param>
/// <param name="Coverage">One byte per pixel, row by row from the top.</param>
public sealed record TextMask(int Width, int Height, byte[] Coverage)
{
    /// <summary>
    /// Draws <paramref name="text"/> in a font, such as Segoe UI or Segoe Fluent Icons, with a
    /// weight from 100 to 900 (400 is regular and 600 semibold) and a height in pixels.
    /// </summary>
    /// <exception cref="Win32Exception">GDI couldn't draw it.</exception>
    public static unsafe TextMask Render(string text, string fontFace, int weight, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(fontFace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

        // A negative height asks for the character height, without the font's internal leading.
        var logFont = new LOGFONTW
        {
            lfHeight = -pixelHeight,
            lfWeight = weight,
            lfCharSet = FONT_CHARSET.DEFAULT_CHARSET,
            lfQuality = FONT_QUALITY.ANTIALIASED_QUALITY,
            lfFaceName = fontFace,
        };
        using var font = PInvoke.CreateFontIndirect(in logFont);
        if (font.IsInvalid)
        {
            throw new Win32Exception();
        }

        var dc = PInvoke.CreateCompatibleDC(HDC.Null);
        if (dc.IsNull)
        {
            throw new Win32Exception();
        }

        try
        {
            var oldFont = PInvoke.SelectObject(dc, (HGDIOBJ)font.DangerousGetHandle());
            if (!PInvoke.GetTextExtentPoint32W(dc, text, text.Length, out var size))
            {
                throw new Win32Exception();
            }

            // A pixel more on each side for smoothing that reaches past the advance width.
            int width = Math.Max(1, size.cx + 2), height = Math.Max(1, size.cy);
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

            using var bitmap = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, out var bits, null, 0);
            if (bitmap.IsInvalid)
            {
                throw new Win32Exception();
            }

            var oldBitmap = PInvoke.SelectObject(dc, (HGDIOBJ)bitmap.DangerousGetHandle());
            PInvoke.SetTextColor(dc, new COLORREF(0x00FFFFFF));
            PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            PInvoke.TextOut(dc, 1, 0, text, text.Length);
            PInvoke.GdiFlush();

            // White text on black: each pixel's brightness is its coverage.
            var coverage = new byte[width * height];
            var pixels = new ReadOnlySpan<uint>(bits, coverage.Length);
            for (var i = 0; i < coverage.Length; i++)
            {
                var pixel = pixels[i];
                coverage[i] = Math.Max((byte)pixel, Math.Max((byte)(pixel >> 8), (byte)(pixel >> 16)));
            }

            PInvoke.SelectObject(dc, oldBitmap);
            PInvoke.SelectObject(dc, oldFont);
            return new TextMask(width, height, coverage);
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }
}
