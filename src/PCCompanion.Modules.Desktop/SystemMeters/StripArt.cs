using System;
using System.Collections.Concurrent;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// The strip's color icons: a blue processor and a green memory module, and the colors of the
/// network arrows. Their mid-tone colors read on light and dark taskbars alike. Icons are drawn at
/// four times their size and scaled down, so their edges are smooth.
/// </summary>
public static class StripArt
{
    /// <summary>The download arrow's green, as 0xRRGGBB.</summary>
    public const uint Download = 0x13A10E;

    /// <summary>The upload arrow's orange, as 0xRRGGBB.</summary>
    public const uint Upload = 0xE3600B;

    private const int Factor = 4;

    private static readonly ConcurrentDictionary<(bool Memory, int Size), uint[]> Icons = new();

    /// <summary>The processor icon at a size, in premultiplied pixels.</summary>
    public static uint[] Processor(int size) => Icons.GetOrAdd((false, size), key => Draw(key.Size, DrawProcessor));

    /// <summary>The memory module icon at a size, in premultiplied pixels.</summary>
    public static uint[] Memory(int size) => Icons.GetOrAdd((true, size), key => Draw(key.Size, DrawMemory));

    private static void DrawProcessor(Sketch sketch)
    {
        const uint pin = 0x6CA8DC;
        foreach (var at in new[] { 5.25, 9.25, 13.25 })
        {
            sketch.Rectangle(at, 1, at + 1.5, 4, pin);
            sketch.Rectangle(at, 16, at + 1.5, 19, pin);
            sketch.Rectangle(1, at, 4, at + 1.5, pin);
            sketch.Rectangle(16, at, 19, at + 1.5, pin);
        }

        sketch.RoundedRectangle(3.5, 3.5, 16.5, 16.5, 2.2, 0x4CB6F5, 0x0F5FB0);
        sketch.RoundedRectangle(7, 7, 13, 13, 1.2, 0xE6F5FF, 0xB8E2FF);
    }

    private static void DrawMemory(Sketch sketch)
    {
        const uint contact = 0xF2C14E;
        for (var x = 2.6; x < 17.5; x += 1.9)
        {
            sketch.Rectangle(x, 13, x + 1.1, 15.6, contact);
        }

        sketch.RoundedRectangle(1, 4.5, 19, 13.5, 1.4, 0x4FD063, 0x0E7A0E);
        const uint chip = 0x0A460A;
        foreach (var x in new[] { 3.0, 8.25, 13.5 })
        {
            sketch.RoundedRectangle(x, 6.3, x + 3.5, 11.2, 0.5, chip, chip);
        }
    }

    // Averages each block of samples into a pixel, premultiplied.
    private static uint[] Draw(int size, Action<Sketch> paint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var sketch = new Sketch(size * Factor);
        paint(sketch);
        var pixels = new uint[size * size];
        const int samples = Factor * Factor;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double alpha = 0, red = 0, green = 0, blue = 0;
                for (var dy = 0; dy < Factor; dy++)
                {
                    for (var dx = 0; dx < Factor; dx++)
                    {
                        var (a, r, g, b) = sketch.Samples[((((y * Factor) + dy) * sketch.Size) + (x * Factor)) + dx];
                        alpha += a;
                        red += r * a;
                        green += g * a;
                        blue += b * a;
                    }
                }

                pixels[(y * size) + x] = ((uint)Math.Round(alpha / samples * 255) << 24)
                    | ((uint)Math.Round(red / samples) << 16)
                    | ((uint)Math.Round(green / samples) << 8)
                    | (uint)Math.Round(blue / samples);
            }
        }

        return pixels;
    }

    // Shapes in the units of a 20 by 20 grid, with a color that runs from top to bottom.
    private sealed class Sketch(int size)
    {
        public int Size { get; } = size;

        public (double Alpha, double Red, double Green, double Blue)[] Samples { get; } = new (double, double, double, double)[size * size];

        private double Unit => Size / 20.0;

        public void Rectangle(double left, double top, double right, double bottom, uint color) =>
            RoundedRectangle(left, top, right, bottom, 0, color, color);

        public void RoundedRectangle(double left, double top, double right, double bottom, double radius, uint topColor, uint bottomColor)
        {
            int x0 = (int)(left * Unit), y0 = (int)(top * Unit), x1 = (int)(right * Unit), y1 = (int)(bottom * Unit);
            var r = radius * Unit;
            for (var y = y0; y < y1; y++)
            {
                var t = (y - y0) / (double)Math.Max(1, y1 - y0 - 1);
                for (var x = x0; x < x1; x++)
                {
                    double centerX = x + 0.5, centerY = y + 0.5;
                    var dx = centerX < x0 + r ? x0 + r - centerX : centerX > x1 - r ? centerX - (x1 - r) : 0;
                    var dy = centerY < y0 + r ? y0 + r - centerY : centerY > y1 - r ? centerY - (y1 - r) : 0;
                    if ((dx * dx) + (dy * dy) <= r * r)
                    {
                        Samples[(y * Size) + x] = (1, Mix(16), Mix(8), Mix(0));
                    }

                    double Mix(int shift) => ((topColor >> shift) & 0xFF) + ((((double)((bottomColor >> shift) & 0xFF)) - ((topColor >> shift) & 0xFF)) * t);
                }
            }
        }
    }
}
