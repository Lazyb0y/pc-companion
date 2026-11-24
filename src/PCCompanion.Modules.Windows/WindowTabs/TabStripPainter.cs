using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>A tab's title and icon, as the strip shows them; the icon is premultiplied pixels, or null.</summary>
public sealed record TabLook(string Title, uint[]? Icon);

/// <summary>The strip's colors, as 0xRRGGBB, from the app mode, the accent color or high contrast.</summary>
/// <param name="Background">The strip behind its tabs.</param>
/// <param name="Hover">A tab under the pointer.</param>
/// <param name="Front">The tab in front.</param>
/// <param name="Text">The tabs' titles.</param>
/// <param name="FrontText">The title of the tab in front.</param>
/// <param name="Accent">The line under the tab in front, and where a dropped tab would go.</param>
/// <param name="Border">A line around the strip, in high contrast only.</param>
public sealed record TabColors(uint Background, uint Hover, uint Front, uint Text, uint FrontText, uint Accent, uint? Border)
{
    public static TabColors Light(uint accent) => new(0xF3F3F3, 0xE6E6E6, 0xFFFFFF, 0x1A1A1A, 0x1A1A1A, accent, null);

    public static TabColors Dark(uint accent) => new(0x202020, 0x2D2D2D, 0x383838, 0xFFFFFF, 0xFFFFFF, accent, null);

    /// <summary>The high contrast colors: window and text, and the highlight for the tab in front.</summary>
    public static TabColors HighContrast(uint window, uint text, uint highlight) => new(window, window, highlight, text, window, text, text);
}

/// <summary>What the painter needs besides the strip's place: sizes and text.</summary>
/// <param name="Dpi">The display's DPI: 96 at 100% scale.</param>
/// <param name="TextScale">Windows' Text size, from 1 to 2.25; icons keep their size.</param>
/// <param name="Font">The font for titles: Segoe UI Variable Text on Windows 11, Segoe UI on Windows 10.</param>
/// <param name="RoundCorners">Rounded corners, as on Windows 11.</param>
public sealed record TabStyle(int Dpi, double TextScale, string Font, bool RoundCorners, bool RightToLeft);

/// <summary>
/// Draws a group's tab strip: each tab's icon and title cut to fit, the tab in front standing out
/// with a line in the accent color, and where a dragged tab would go. See "Look" in
/// docs/features/windows/window-tabs.md.
/// </summary>
public static class TabStripPainter
{
    /// <summary>The icon's size, in effective pixels.</summary>
    public const int IconSize = 16;

    // Effective pixels.
    private const int Gap = 2;
    private const int TopMargin = 3;
    private const int Padding = 8;
    private const int IconToText = 6;
    private const int TextHeight = 12;
    private const int AccentLine = 3;
    private const int Corner = 6;

    private const char Ellipsis = '…';

    private static readonly ConcurrentDictionary<(string Text, string Font, int Height, bool RightToLeft), TextMask> Texts = new();

    /// <summary>The icon's size in pixels at the style's DPI.</summary>
    public static int IconPixels(TabStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return StripLayout.Scale(IconSize, style.Dpi);
    }

    /// <param name="hover">The tab under the pointer, or -1.</param>
    public static StripCanvas Paint(StripView view, IReadOnlyList<TabLook> looks, int hover, TabColors colors, TabStyle style)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(style);

        var place = view.Place;
        var canvas = new StripCanvas(place.Bounds.Width, place.Bounds.Height);
        int Scale(int effective) => StripLayout.Scale(effective, style.Dpi);
        var corner = style.RoundCorners ? Scale(Corner) : 0;

        if (colors.Border is { } border)
        {
            canvas.Fill(0, 0, canvas.Width, canvas.Height, corner, border, 255);
            canvas.Fill(1, 1, canvas.Width - 2, canvas.Height - 1, corner, colors.Background, 255);
        }
        else
        {
            canvas.Fill(0, 0, canvas.Width, canvas.Height, corner, colors.Background, 255);
        }

        var shown = view.Chosen >= 0 ? view.Chosen : view.Front;
        for (var index = 0; index < place.Tabs.Count && index < looks.Count; index++)
        {
            var cell = place.Tabs[index];
            var tab = cell with { X = cell.X + Scale(Gap), Width = cell.Width - (2 * Scale(Gap)), Y = Scale(TopMargin), Height = cell.Height - Scale(TopMargin) };
            var front = index == shown;
            if (front || index == hover)
            {
                canvas.Fill(tab.X, tab.Y, tab.Width, tab.Height, corner, front ? colors.Front : colors.Hover, 255);
            }

            if (front)
            {
                canvas.Fill(tab.X + Scale(Padding), tab.Bottom - Scale(AccentLine), Math.Max(1, tab.Width - (2 * Scale(Padding))), Scale(AccentLine), Scale(AccentLine) / 2, colors.Accent, 255);
            }

            PaintContent(canvas, tab, looks[index], front ? colors.FrontText : colors.Text, style);
        }

        if (view.DropMark >= 0 && place.Tabs.Count > 0)
        {
            var x = MarkX(place, view.DropMark);
            canvas.Fill(Math.Clamp(x - Scale(1), 0, canvas.Width - Scale(2)), Scale(TopMargin), Scale(2), canvas.Height - Scale(TopMargin), 0, colors.Accent, 255);
        }

        return canvas;
    }

    // The icon at the tab's start and the title after it, cut with an ellipsis to fit; a tab too
    // narrow for text shows only its icon, centered.
    private static void PaintContent(StripCanvas canvas, System.Drawing.Rectangle tab, TabLook look, uint color, TabStyle style)
    {
        int Scale(int effective) => StripLayout.Scale(effective, style.Dpi);
        var iconSize = IconPixels(style);
        var start = Scale(Padding);
        var textSpace = tab.Width - (2 * start) - iconSize - Scale(IconToText);
        var iconY = tab.Y + ((tab.Height - iconSize) / 2);
        if (textSpace < Scale(IconSize))
        {
            DrawIcon(canvas, look.Icon, iconSize, tab.X + ((tab.Width - iconSize) / 2), iconY);
            return;
        }

        var iconX = style.RightToLeft ? tab.Right - start - iconSize : tab.X + start;
        DrawIcon(canvas, look.Icon, iconSize, iconX, iconY);
        var height = Math.Max(1, (int)Math.Round(Scale(TextHeight) * style.TextScale));
        var text = Fit(look.Title, style.Font, height, textSpace, style.RightToLeft);
        if (text is null)
        {
            return;
        }

        var textX = style.RightToLeft ? iconX - Scale(IconToText) - text.Width : iconX + iconSize + Scale(IconToText);
        canvas.Draw(text, textX, tab.Y + ((tab.Height - text.Height) / 2), color);
    }

    private static void DrawIcon(StripCanvas canvas, uint[]? icon, int size, int x, int y)
    {
        if (icon is not null && icon.Length == size * size)
        {
            canvas.DrawIcon(icon, size, x, y);
        }
    }

    // The longest start of the title that fits, with an ellipsis when it's cut.
    private static TextMask? Fit(string title, string font, int height, int width, bool rightToLeft)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var whole = Render(title, font, height, rightToLeft);
        if (whole.Width <= width)
        {
            return whole;
        }

        int low = 0, high = title.Length;
        TextMask? best = null;
        while (low < high)
        {
            var length = (low + high + 1) / 2;
            var cut = Render(Cut(title, length) + Ellipsis, font, height, rightToLeft);
            if (cut.Width <= width)
            {
                best = cut;
                low = length;
            }
            else
            {
                high = length - 1;
            }
        }

        return best;
    }

    // Never cuts a surrogate pair in half.
    private static string Cut(string text, int length)
    {
        var end = length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..end].TrimEnd();
    }

    // Titles change, as a browser's with each page, so the drawn texts are let go now and then.
    private static TextMask Render(string text, string font, int height, bool rightToLeft)
    {
        if (Texts.Count > 512)
        {
            Texts.Clear();
        }

        return Texts.GetOrAdd((text, font, height, rightToLeft), key => TextMask.Render(key.Text, key.Font, 400, key.Height, key.RightToLeft));
    }

    // Where a dropped tab would go: between two tabs, in the strip's own pixels.
    private static int MarkX(StripPlace place, int insertion)
    {
        if (insertion < place.Tabs.Count)
        {
            var tab = place.Tabs[insertion];
            return place.RightToLeft ? tab.Right : tab.Left;
        }

        var last = place.Tabs[^1];
        return place.RightToLeft ? last.Left : last.Right;
    }
}
