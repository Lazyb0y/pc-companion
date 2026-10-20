using System;
using System.Collections.Concurrent;
using System.Globalization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Draws the meters' notification icons as premultiplied 32-bit pixels, size by size, in one
/// color on a transparent background. See docs/features/desktop/system-meters.md.
/// </summary>
public static class MeterIcons
{
    private const string Font = "Segoe UI";
    private const int Semibold = 600;

    // Pixels fainter than this don't count as part of a digit's shape.
    private const byte InkThreshold = 24;

    // The font height that makes the widest digits fit, by icon size and number of digits.
    private static readonly ConcurrentDictionary<(int Size, int Digits), int> FontHeights = new();

    /// <summary>
    /// A percentage as a whole number, as large as fits and centered by its shape. One and two
    /// digits share a size, so the number doesn't jump between 9 and 10; 100 is drawn smaller.
    /// </summary>
    /// <param name="color">0xRRGGBB.</param>
    public static uint[] Number(int percent, int size, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var text = Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture);
        var digits = Math.Max(2, text.Length);
        var height = FontHeights.GetOrAdd((size, digits), key => FindFontHeight(key.Size, key.Digits));
        var mask = TextMask.Render(text, Font, Semibold, height);
        var ink = Ink(mask);

        var pixels = new uint[size * size];
        if (ink.IsEmpty)
        {
            return pixels;
        }

        int left = (size - ink.Width) / 2, top = (size - ink.Height) / 2;
        for (var y = 0; y < ink.Height; y++)
        {
            for (var x = 0; x < ink.Width; x++)
            {
                int targetX = left + x, targetY = top + y;
                if (targetX >= 0 && targetY >= 0 && targetX < size && targetY < size)
                {
                    var coverage = mask.Coverage[((ink.Top + y) * mask.Width) + ink.Left + x];
                    pixels[(targetY * size) + targetX] = Premultiply(color, coverage);
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// How full a network slot is for a rate, on a logarithmic scale: empty at 1 Kbps or less,
    /// half at 1 Mbps and full at 1 Gbps or more, with the same step for each tenfold rate.
    /// </summary>
    public static double SlotFraction(double bitsPerSecond) =>
        bitsPerSecond <= 1000 ? 0 : Math.Min(1, Math.Log10(bitsPerSecond / 1000) / 6);

    /// <summary>
    /// Two outlined slots side by side, like two battery icons, receive on the left and send on
    /// the right, each filled from the bottom; any traffic above 1 Kbps fills at least one row.
    /// </summary>
    /// <param name="color">0xRRGGBB.</param>
    public static uint[] NetworkSlots(double receiveBitsPerSecond, double sendBitsPerSecond, int size, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);

        var pixels = new uint[size * size];
        var solid = Premultiply(color, 255);
        var line = Math.Max(1, size / 16);
        var gap = Math.Max(2, size / 8);
        var slot = (size - gap) / 2;
        var left = (size - ((2 * slot) + gap)) / 2;
        int top = 1, bottom = size - 2;

        DrawSlot(left, SlotFraction(receiveBitsPerSecond));
        DrawSlot(left + slot + gap, SlotFraction(sendBitsPerSecond));
        return pixels;

        void DrawSlot(int x0, double fraction)
        {
            for (var y = top; y <= bottom; y++)
            {
                for (var x = x0; x < x0 + slot; x++)
                {
                    if (y < top + line || y > bottom - line || x < x0 + line || x >= x0 + slot - line)
                    {
                        pixels[(y * size) + x] = solid;
                    }
                }
            }

            // Inside the outline, with a pixel of space, so a low level shows as a fill.
            int innerTop = top + line + 1, innerBottom = bottom - line - 1;
            var tallest = innerBottom - innerTop + 1;
            var height = fraction <= 0 ? 0 : Math.Max(1, (int)Math.Round(fraction * tallest));
            for (var y = innerBottom; y > innerBottom - height; y--)
            {
                for (var x = x0 + line + 1; x < x0 + slot - line - 1; x++)
                {
                    pixels[(y * size) + x] = solid;
                }
            }
        }
    }

    /// <summary>The box around a mask's visible pixels; empty when nothing shows.</summary>
    public static InkBox Ink(TextMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        int left = mask.Width, top = mask.Height, right = -1, bottom = -1;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask.Coverage[(y * mask.Width) + x] > InkThreshold)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? default : new InkBox(left, top, right - left + 1, bottom - top + 1);
    }

    /// <param name="color">0xRRGGBB.</param>
    public static uint Premultiply(uint color, byte alpha) =>
        ((uint)alpha << 24)
        | ((((color >> 16) & 0xFF) * alpha / 255) << 16)
        | ((((color >> 8) & 0xFF) * alpha / 255) << 8)
        | ((color & 0xFF) * alpha / 255);

    // The largest font whose widest digits, 8s, fit the icon.
    private static int FindFontHeight(int size, int digits)
    {
        var widest = new string('8', digits);
        for (var height = size * 2; height > 4; height--)
        {
            var ink = Ink(TextMask.Render(widest, Font, Semibold, height));
            if (ink.Width <= size && ink.Height <= size)
            {
                return height;
            }
        }

        return 4;
    }
}

/// <summary>A box of pixels; empty when its width is 0.</summary>
public readonly record struct InkBox(int Left, int Top, int Width, int Height)
{
    public bool IsEmpty => Width == 0;
}
