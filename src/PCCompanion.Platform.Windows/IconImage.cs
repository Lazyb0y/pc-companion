using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// An icon made from pixels, such as one drawn for a tray icon. Disposing it destroys the icon.
/// </summary>
public sealed class IconImage : IDisposable
{
    private readonly DestroyIconSafeHandle _icon;

    private IconImage(DestroyIconSafeHandle icon, int size)
    {
        _icon = icon;
        Size = size;
    }

    public int Size { get; }

    internal nint Handle => _icon.DangerousGetHandle();

    /// <summary>
    /// Makes a square icon from premultiplied 32-bit pixels (alpha, red, green and blue, from the
    /// high byte down), row by row from the top.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't make the icon.</exception>
    public static unsafe IconImage FromPixels(ReadOnlySpan<uint> pixels, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, size * size, nameof(pixels));

        using var color = TopDownBitmap.Create(HDC.Null, size, size, out var bits);

        pixels.CopyTo(new Span<uint>(bits, pixels.Length));

        // The color bitmap's alpha decides what shows; the mask, all zero, is still required.
        var mask = new byte[(size + 15) / 16 * 2 * size];
        HBITMAP maskBitmap;
        fixed (byte* maskBits = mask)
        {
            maskBitmap = PInvoke.CreateBitmap(size, size, 1, 1, maskBits);
        }

        if (maskBitmap.IsNull)
        {
            throw new Win32Exception();
        }

        try
        {
            var iconInfo = new ICONINFO { fIcon = true, hbmMask = maskBitmap, hbmColor = (HBITMAP)color.DangerousGetHandle() };
            var icon = PInvoke.CreateIconIndirect(in iconInfo);
            if (icon.IsInvalid)
            {
                icon.Dispose();
                throw new Win32Exception();
            }

            return new IconImage(icon, size);
        }
        finally
        {
            PInvoke.DeleteObject((HGDIOBJ)maskBitmap.Value);
        }
    }

    public void Dispose() => _icon.Dispose();
}
