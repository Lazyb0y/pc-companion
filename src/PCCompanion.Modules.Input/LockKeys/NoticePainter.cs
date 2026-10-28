using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>A line of the notice: a key's state, such as "Caps Lock on", with a lock icon, closed when it's on.</summary>
public readonly record struct NoticeLine(string Text, bool IsOn);

/// <summary>The notice's colors, as 0xRRGGBB.</summary>
/// <param name="HighContrast">High contrast is on: the border is wider and the corners square.</param>
public readonly record struct NoticeColors(uint Background, uint Border, uint Text, bool HighContrast)
{
    /// <summary>Near black with white text, as Windows 11's indicators in the dark Windows mode.</summary>
    public static NoticeColors Dark { get; } = new(0x2C2C2C, 0x474747, 0xFFFFFF, false);

    /// <summary>Near white with black text, as Windows 11's indicators in the light Windows mode.</summary>
    public static NoticeColors Light { get; } = new(0xF9F9F9, 0xD0D0D0, 0x1A1A1A, false);

    /// <summary>The high contrast theme's window background and text colors, with a border in the text color.</summary>
    public static NoticeColors HighContrastTheme(uint window, uint text) => new(window, text, text, true);
}

/// <summary>The fonts the notice draws with, which differ between Windows 11 and Windows 10.</summary>
public sealed record NoticeFonts(string Text, string Icons)
{
    public static NoticeFonts Windows11 { get; } = new("Segoe UI Variable Text", "Segoe Fluent Icons");

    // Windows 10 has neither Segoe UI Variable nor Segoe Fluent Icons; Segoe MDL2 Assets has the same icons.
    public static NoticeFonts Windows10 { get; } = new("Segoe UI", "Segoe MDL2 Assets");

    /// <summary>The fonts of a Windows build: Windows 11's from build 22000.</summary>
    public static NoticeFonts For(int windowsBuild) => windowsBuild >= 22000 ? Windows11 : Windows10;
}

/// <summary>How to draw the notice or the badge.</summary>
/// <param name="Scale">The display's scale: 1 at 100%.</param>
/// <param name="TextScale">Windows' text size (Accessibility > Text size): 1 to 2.25.</param>
/// <param name="RoundedCorners">Rounded on Windows 11, square on Windows 10 and in high contrast.</param>
/// <param name="RightToLeft">Mirrored, with the icon on the right and the text read right to left.</param>
/// <param name="Badge">The smaller badge that stays while Caps Lock is on, rather than the notice.</param>
public sealed record NoticeStyle(double Scale, double TextScale, NoticeColors Colors, NoticeFonts Fonts, bool RoundedCorners, bool RightToLeft, bool Badge = false);

/// <summary>A drawn notice: premultiplied 32-bit pixels for <see cref="LayeredWindow.Show"/>, row by row from the top.</summary>
public sealed record NoticeImage(int Width, int Height, uint[] Pixels);

/// <summary>Where the notice's parts go, in pixels from its top-left corner.</summary>
/// <param name="RowHeight">The height of each line.</param>
/// <param name="IconX">Where each line's icon starts.</param>
/// <param name="TextX">Where each line's text starts; in right-to-left languages, lines end at the same place instead.</param>
public sealed record NoticeLayout(int Width, int Height, int RowHeight, int IconX, int TextX);

/// <summary>
/// Draws the notice and the badge: a box with a lock icon and a key's state on each line, in the
/// Windows mode's or high contrast colors. See "How the notice looks" in docs/features/input/lock-keys.md.
/// </summary>
public static class NoticePainter
{
    private const int Regular = 400;
    private const string LockGlyph = "";
    private const string UnlockGlyph = "";

    /// <summary>Works out the size of the notice and where its parts go.</summary>
    public static NoticeLayout Measure(IReadOnlyList<NoticeLine> lines, NoticeStyle style) => Draw(lines, style, paint: false).Layout;

    /// <summary>Draws the notice for <paramref name="lines"/>, one line for each key that changed.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">GDI couldn't draw the text.</exception>
    public static NoticeImage Paint(IReadOnlyList<NoticeLine> lines, NoticeStyle style) => Draw(lines, style, paint: true).Image!;

    private static (NoticeLayout Layout, NoticeImage? Image) Draw(IReadOnlyList<NoticeLine> lines, NoticeStyle style, bool paint)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfZero(lines.Count, nameof(lines));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(style.Scale);

        var sizes = new Sizes(style);
        var texts = lines.Select(line => TextMask.Render(line.Text, style.Fonts.Text, Regular, sizes.Text, style.RightToLeft)).ToArray();
        var icons = lines.Select(line => TextMask.Render(line.IsOn ? LockGlyph : UnlockGlyph, style.Fonts.Icons, Regular, sizes.Icon)).ToArray();
        var iconWidth = icons.Max(icon => icon.Width);
        var textWidth = texts.Max(text => text.Width);
        // A larger text size makes the lines taller.
        var rowHeight = Math.Max(sizes.Row, texts.Max(text => text.Height) + (2 * sizes.VerticalPadding));

        var width = sizes.Padding + iconWidth + sizes.Gap + textWidth + sizes.Padding;
        var height = rowHeight * lines.Count;
        var iconX = style.RightToLeft ? width - sizes.Padding - iconWidth : sizes.Padding;
        var textX = style.RightToLeft ? sizes.Padding : sizes.Padding + iconWidth + sizes.Gap;
        var layout = new NoticeLayout(width, height, rowHeight, iconX, textX);
        if (!paint)
        {
            return (layout, null);
        }

        var coverage = new byte[width * height];
        for (var row = 0; row < lines.Count; row++)
        {
            var top = row * rowHeight;

            // The icon is centered by its visible pixels, the text by its line box, so lines of
            // any language sit alike.
            var icon = icons[row];
            var (inkTop, inkBottom) = InkRows(icon);
            Stamp(coverage, width, icon, iconX + ((iconWidth - icon.Width) / 2), top + ((rowHeight - (inkTop + inkBottom)) / 2));

            var text = texts[row];
            var x = style.RightToLeft ? textX + textWidth - text.Width : textX;
            Stamp(coverage, width, text, x, top + ((rowHeight - text.Height) / 2));
        }

        return (layout, new NoticeImage(width, height, Compose(coverage, width, height, style, sizes)));
    }

    // Box, border and text, premultiplied, opaque inside the box.
    private static uint[] Compose(byte[] text, int width, int height, NoticeStyle style, Sizes sizes)
    {
        var colors = style.Colors;
        var rounded = style.RoundedCorners && !colors.HighContrast;
        double radius = rounded ? Math.Min(sizes.Corner, height / 2.0) : 0;
        var border = sizes.Border;
        var pixels = new uint[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var outer = Coverage(x + 0.5, y + 0.5, width, height, radius);
                if (outer <= 0)
                {
                    continue;
                }

                var inner = Coverage(x + 0.5 - border, y + 0.5 - border, width - (2 * border), height - (2 * border), Math.Max(0, radius - border));
                var color = Mix(colors.Border, colors.Background, inner);
                color = Mix(color, colors.Text, text[(y * width) + x] / 255.0);
                pixels[(y * width) + x] = Premultiply(color, outer);
            }
        }

        return pixels;
    }

    private static void Stamp(byte[] coverage, int width, TextMask mask, int left, int top)
    {
        var height = coverage.Length / width;
        for (var y = 0; y < mask.Height; y++)
        {
            var row = top + y;
            if (row < 0 || row >= height)
            {
                continue;
            }

            for (var x = 0; x < mask.Width; x++)
            {
                var column = left + x;
                if (column < 0 || column >= width)
                {
                    continue;
                }

                var index = (row * width) + column;
                coverage[index] = Math.Max(coverage[index], mask.Coverage[(y * mask.Width) + x]);
            }
        }
    }

    // The first row with ink and the row after the last, or the whole box without ink.
    private static (int Top, int Bottom) InkRows(TextMask mask)
    {
        int top = -1, bottom = 0;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask.Coverage[(y * mask.Width) + x] > 0)
                {
                    top = top < 0 ? y : top;
                    bottom = y + 1;
                    break;
                }
            }
        }

        return top < 0 ? (0, mask.Height) : (top, bottom);
    }

    // How much of the pixel centered at (x, y) a rounded rectangle from (0, 0) to (w, h) covers.
    private static double Coverage(double x, double y, double w, double h, double radius)
    {
        if (x < 0 || y < 0 || x > w || y > h)
        {
            return 0;
        }

        if (radius <= 0)
        {
            return 1;
        }

        var cx = x < radius ? radius : x > w - radius ? w - radius : x;
        var cy = y < radius ? radius : y > h - radius ? h - radius : y;
        var distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
        return Math.Clamp(radius - distance + 0.5, 0, 1);
    }

    private static uint Mix(uint from, uint to, double amount)
    {
        uint Channel(int shift) => (uint)Math.Round((((from >> shift) & 0xFF) * (1 - amount)) + (((to >> shift) & 0xFF) * amount)) << shift;
        return Channel(16) | Channel(8) | Channel(0);
    }

    private static uint Premultiply(uint color, double alpha)
    {
        uint Channel(int shift) => (uint)Math.Round(((color >> shift) & 0xFF) * alpha) << shift;
        return ((uint)Math.Round(alpha * 255) << 24) | Channel(16) | Channel(8) | Channel(0);
    }

    // Sizes in pixels at the style's scale: the notice is 40 effective pixels high with 14-pixel
    // text, the badge 28 with 12-pixel text.
    private readonly struct Sizes
    {
        public Sizes(NoticeStyle style)
        {
            var scale = style.Scale;
            var text = style.Badge ? 12 : 14;
            Text = Pixels(text * style.TextScale, scale);
            Icon = Pixels((style.Badge ? 13 : 16) * style.TextScale, scale);
            Row = Pixels(style.Badge ? 28 : 40, scale);
            Padding = Pixels(style.Badge ? 10 : 14, scale);
            VerticalPadding = Pixels(style.Badge ? 4 : 6, scale);
            Gap = Pixels(style.Badge ? 6 : 8, scale);
            Corner = Pixels(8, scale);
            Border = style.Colors.HighContrast ? Pixels(2, scale) : Math.Max(1, (int)Math.Floor(scale));
        }

        public int Text { get; }

        public int Icon { get; }

        public int Row { get; }

        public int Padding { get; }

        public int VerticalPadding { get; }

        public int Gap { get; }

        public int Corner { get; }

        public int Border { get; }

        private static int Pixels(double effective, double scale) => Math.Max(1, (int)Math.Round(effective * scale));
    }
}
