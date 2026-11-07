using System;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A dot drawn on an icon to point to something new, as WinUI's dot badge does: in the icon's
/// bottom-right corner, with a clear gap around it, so it reads as apart from the icon.
/// </summary>
public static class AttentionDot
{
    // Parts of the icon's width: the dot's diameter, and the gap around it.
    private const double DotPart = 3.0 / 8;
    private const double GapPart = 1.0 / 16;

    // Each pixel is sampled at a grid of points this many wide, for smooth edges.
    private const int SamplesPerSide = 4;
    private const int Samples = SamplesPerSide * SamplesPerSide;

    /// <summary>
    /// The dot's color on the taskbar, as 0xRRGGBB: WinUI's dot badge's colors, in the Windows
    /// mode, which the taskbar follows. That's the accent color in light mode, its lighter shade
    /// in dark mode, and the theme's text color, which the taskbar's own icons use, in high
    /// contrast. Each call reads the current settings.
    /// </summary>
    public static uint TaskbarColor() =>
        WindowsTheme.IsHighContrast() ? WindowsTheme.TextColor()
        : WindowsTheme.IsDark() ? WindowsTheme.LightAccentColor()
        : WindowsTheme.AccentColor();

    /// <summary>
    /// Draws the dot on a square icon <paramref name="size"/> pixels wide, given as premultiplied
    /// 32-bit pixels, as <see cref="IconImage.FromPixels"/> takes them, in
    /// <paramref name="color"/>, 0xRRGGBB. The dot is 3/8 of the icon wide and touches its right
    /// and bottom edges; the gap is 1/16 of the icon wide.
    /// </summary>
    public static void Draw(Span<uint> pixels, int size, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, size * size, nameof(pixels));

        var radius = size * DotPart / 2;
        var outerRadius = radius + (size * GapPart);
        var center = size - radius;
        var dot = 0xFF000000 | (color & 0xFFFFFF);

        // Only the square around the gap changes.
        var first = Math.Max(0, (int)Math.Floor(center - outerRadius));
        for (var y = first; y < size; y++)
        {
            for (var x = first; x < size; x++)
            {
                var (inDot, inGap) = Cover(x, y, center, radius, outerRadius);
                if (inDot + inGap > 0)
                {
                    var at = (y * size) + x;
                    pixels[at] = Mix(dot, inDot, pixels[at], Samples - inDot - inGap);
                }
            }
        }
    }

    // How many of the pixel's sample points are in the dot, and how many are in the gap around it.
    private static (int InDot, int InGap) Cover(int x, int y, double center, double radius, double outerRadius)
    {
        var inDot = 0;
        var inGap = 0;
        for (var row = 0; row < SamplesPerSide; row++)
        {
            var dy = y + ((row + 0.5) / SamplesPerSide) - center;
            for (var column = 0; column < SamplesPerSide; column++)
            {
                var dx = x + ((column + 0.5) / SamplesPerSide) - center;
                var distance = (dx * dx) + (dy * dy);
                if (distance <= radius * radius)
                {
                    inDot++;
                }
                else if (distance <= outerRadius * outerRadius)
                {
                    inGap++;
                }
            }
        }

        return (inDot, inGap);
    }

    // Each sample point shows the dot, nothing (the gap) or the icon. The pixels are premultiplied,
    // so each channel is the sum of its parts, and none comes out above the alpha.
    private static uint Mix(uint dot, int dotSamples, uint icon, int iconSamples)
    {
        var mixed = 0u;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var channel = ((((dot >> shift) & 0xFF) * (uint)dotSamples) + (((icon >> shift) & 0xFF) * (uint)iconSamples) + (Samples / 2)) / Samples;
            mixed |= channel << shift;
        }

        return mixed;
    }
}
