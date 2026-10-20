using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Premultiplied 32-bit pixels (alpha, red, green and blue, from the high byte down), row by row
/// from the top, that the strip is drawn into.
/// </summary>
public sealed class StripCanvas
{
    public StripCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Pixels { get; }

    /// <summary>Fills a rectangle with rounded corners in a color (0xRRGGBB) at an opacity from 0 to 255.</summary>
    public void Fill(int left, int top, int width, int height, int radius, uint color, int opacity)
    {
        for (var y = Math.Max(0, top); y < Math.Min(Height, top + height); y++)
        {
            for (var x = Math.Max(0, left); x < Math.Min(Width, left + width); x++)
            {
                // Outside the circles that round the corners.
                var dx = x < left + radius ? left + radius - x : x >= left + width - radius ? x - (left + width - radius - 1) : 0;
                var dy = y < top + radius ? top + radius - y : y >= top + height - radius ? y - (top + height - radius - 1) : 0;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    Blend(x, y, color, opacity);
                }
            }
        }
    }

    /// <summary>Draws text or a glyph with its top-left corner at (x, y), in a color (0xRRGGBB) at an opacity.</summary>
    public void Draw(TextMask mask, int x, int y, uint color, int opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(mask);

        for (var row = 0; row < mask.Height; row++)
        {
            for (var column = 0; column < mask.Width; column++)
            {
                var coverage = mask.Coverage[(row * mask.Width) + column];
                if (coverage != 0)
                {
                    Blend(x + column, y + row, color, coverage * opacity / 255);
                }
            }
        }
    }

    /// <summary>Draws a square icon of premultiplied pixels with its top-left corner at (x, y).</summary>
    public void DrawIcon(uint[] icon, int size, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(icon);

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                int targetX = x + column, targetY = y + row;
                var source = icon[(row * size) + column];
                var alpha = source >> 24;
                if (alpha == 0 || targetX < 0 || targetY < 0 || targetX >= Width || targetY >= Height)
                {
                    continue;
                }

                ref var target = ref Pixels[(targetY * Width) + targetX];
                var inverse = 255 - alpha;
                var current = target;
                uint Channel(int shift) => ((source >> shift) & 0xFF) + (((current >> shift) & 0xFF) * inverse / 255);
                target = (Channel(24) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
            }
        }
    }

    // Paints a color over the pixel, premultiplied.
    private void Blend(int x, int y, uint color, int opacity)
    {
        if (opacity <= 0 || x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        ref var target = ref Pixels[(y * Width) + x];
        uint alpha = (uint)Math.Min(opacity, 255), inverse = 255 - alpha;
        var current = target;
        uint Channel(int shift) => (((color >> shift) & 0xFF) * alpha / 255) + (((current >> shift) & 0xFF) * inverse / 255);
        var outAlpha = alpha + (((current >> 24) & 0xFF) * inverse / 255);
        target = (outAlpha << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
