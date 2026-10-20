using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>Which meters the strip shows, and how.</summary>
/// <param name="RightToLeft">The columns run right to left, CPU first, as in right-to-left languages.</param>
public sealed record StripStyle(bool Cpu, bool Memory, bool Network, StripLayout Layout, StripIcons Icons, bool RightToLeft)
{
    public static StripStyle From(SystemMetersSettings settings, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new StripStyle(settings.Cpu, settings.Memory, settings.Network, settings.Layout, settings.Icons, rightToLeft);
    }
}

/// <summary>The values the strip shows; a meter without a value yet shows "--".</summary>
public sealed record StripTexts(string? Cpu, string? Memory, string? Receive, string? Send);

/// <summary>The strip's colors.</summary>
/// <param name="Text">The text color, as 0xRRGGBB: white on a dark taskbar, black on a light one, or the high contrast text color.</param>
/// <param name="HighContrast">High contrast is on, so the icons use the text color too.</param>
public readonly record struct StripColors(uint Text, bool HighContrast);

/// <summary>The fonts the strip draws with, which differ between Windows 11 and Windows 10.</summary>
/// <param name="Value">Values, in semibold.</param>
/// <param name="Text">Names and rates.</param>
/// <param name="Large">Large values, in semibold.</param>
/// <param name="Icons">The outline icons and the arrows.</param>
public sealed record StripFonts(string Value, string Text, string Large, string Icons)
{
    // GDI knows Segoe UI Variable Display Semibold by its cut name: it cuts face names to 31 characters.
    public static StripFonts Windows11 { get; } = new("Segoe UI Variable Text Semibold", "Segoe UI Variable Text", "Segoe UI Variable Display Semib", "Segoe Fluent Icons");

    // Windows 10 has neither Segoe UI Variable nor Segoe Fluent Icons.
    public static StripFonts Windows10 { get; } = new("Segoe UI Semibold", "Segoe UI", "Segoe UI Semibold", "Segoe MDL2 Assets");

    /// <summary>The fonts of a Windows build: Windows 11's from build 22000.</summary>
    public static StripFonts For(int windowsBuild) => windowsBuild >= 22000 ? Windows11 : Windows10;
}

/// <summary>A column of the strip.</summary>
public enum StripColumnKind
{
    Cpu,
    Memory,

    /// <summary>CPU above memory, in the Compact layout.</summary>
    CpuAndMemory,
    Network,
}

/// <summary>A column of the strip, from <paramref name="X"/> for <paramref name="Width"/> pixels.</summary>
public readonly record struct StripColumn(StripColumnKind Kind, int X, int Width);

/// <summary>The strip's size and its columns' places, at a scale, which <see cref="StripPainter.Measure"/> works out.</summary>
public sealed record StripMetrics(StripStyle Style, double Scale, StripFonts Fonts, int Width, IReadOnlyList<StripColumn> Columns);

/// <summary>
/// Lays out and draws the strip: the columns of CPU, memory and network, each with its icon. Each
/// value has a fixed place, sized for its widest value, so the strip keeps its width as the
/// numbers change. See "On the taskbar" in docs/features/desktop/system-meters.md.
/// </summary>
public static class StripPainter
{
    private const int Regular = 400;
    private const int Semibold = 600;
    private const string NoValue = "--";
    private const string ProcessorGlyph = "";
    private const string MemoryGlyph = "";
    private const string DownloadGlyph = "";
    private const string UploadGlyph = "";
    private const string DownArrow = "↓";
    private const string UpArrow = "↑";
    private const int NameOpacity = 200;
    private const int SendOpacity = 220;

    // Almost transparent, so clicks between the letters reach the strip.
    private const int BackgroundOpacity = 1;
    private const int HighlightOpacity = 20;
    private const int MaximumCachedTexts = 512;

    // The widest rate in each unit, such as 99.9 Kbps and 999 Mbps.
    private static readonly double[] WidestRates = [99_900, 999_000, 99_900_000, 999_000_000, 99_900_000_000];

    private static readonly ConcurrentDictionary<(string Text, string Font, int Weight, int Height), TextMask> Texts = new();
    private static readonly ConcurrentDictionary<TextMask, double> InkMiddles = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Works out the strip's width and its columns' places for a style at a scale (1 at 100%), with
    /// the widest values in <paramref name="culture"/> and the names in the current UI language, in
    /// Windows 11's fonts unless others are given.
    /// </summary>
    public static StripMetrics Measure(StripStyle style, double scale, CultureInfo culture, StripFonts? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        fonts ??= StripFonts.Windows11;
        var sizes = new Sizes(scale);
        var widestPercent = MeterText.Percent(100, culture);
        var kinds = new List<StripColumnKind>();
        if (style.Layout == StripLayout.Compact)
        {
            if (style.Cpu || style.Memory)
            {
                kinds.Add(StripColumnKind.CpuAndMemory);
            }
        }
        else
        {
            if (style.Cpu)
            {
                kinds.Add(StripColumnKind.Cpu);
            }

            if (style.Memory)
            {
                kinds.Add(StripColumnKind.Memory);
            }
        }

        if (style.Network)
        {
            kinds.Add(StripColumnKind.Network);
        }

        var widths = kinds.Select(kind => kind switch
        {
            StripColumnKind.Cpu => MeterWidth(style, fonts, sizes, widestPercent, Strings.SystemMetersCpuHeader),
            StripColumnKind.Memory => MeterWidth(style, fonts, sizes, widestPercent, Strings.SystemMetersMemoryHeader),
            StripColumnKind.CpuAndMemory => (style.Icons == StripIcons.None ? 0 : sizes.SmallIcon + sizes.SmallIconGap) + Text(widestPercent, fonts.Value, Semibold, sizes.Line).Width,
            _ => Arrow(style, fonts, sizes, receive: true).Width + sizes.ArrowGap + WidestRate(culture, fonts, sizes),
        }).ToList();

        var columns = new List<StripColumn>();
        var order = style.RightToLeft ? Enumerable.Range(0, kinds.Count).Reverse() : Enumerable.Range(0, kinds.Count);
        var x = sizes.Padding;
        foreach (var index in order)
        {
            columns.Add(new StripColumn(kinds[index], x, widths[index]));
            x += widths[index] + sizes.Between;
        }

        var width = kinds.Count == 0 ? 0 : x - sizes.Between + sizes.Padding;
        return new StripMetrics(style, scale, fonts, width, columns);
    }

    /// <summary>
    /// Draws the strip as premultiplied pixels, the strip's width by <paramref name="height"/>, with a
    /// rounded highlight while the pointer is on it.
    /// </summary>
    public static uint[] Paint(StripMetrics metrics, int height, StripTexts texts, StripColors colors, bool highlighted)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metrics.Width, nameof(metrics));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var sizes = new Sizes(metrics.Scale);
        var canvas = new StripCanvas(metrics.Width, height);
        canvas.Fill(0, 0, canvas.Width, height, 0, colors.Text, BackgroundOpacity);
        if (highlighted)
        {
            canvas.Fill(sizes.HighlightInset, sizes.HighlightInset, canvas.Width - (2 * sizes.HighlightInset), height - (2 * sizes.HighlightInset), sizes.HighlightRadius, colors.Text, HighlightOpacity);
        }

        var (style, fonts) = (metrics.Style, metrics.Fonts);
        foreach (var column in metrics.Columns)
        {
            switch (column.Kind)
            {
                case StripColumnKind.Cpu:
                    PaintMeter(canvas, style, fonts, sizes, colors, column.X, texts.Cpu, Strings.SystemMetersCpuHeader, memory: false);
                    break;
                case StripColumnKind.Memory:
                    PaintMeter(canvas, style, fonts, sizes, colors, column.X, texts.Memory, Strings.SystemMetersMemoryHeader, memory: true);
                    break;
                case StripColumnKind.CpuAndMemory:
                    PaintCompact(canvas, style, fonts, sizes, colors, column.X, texts);
                    break;
                case StripColumnKind.Network:
                    PaintNetwork(canvas, style, fonts, sizes, colors, column.X, texts);
                    break;
            }
        }

        return canvas.Pixels;
    }

    // The width of a CPU or memory column: its icon, then its value, above its name or large.
    private static int MeterWidth(StripStyle style, StripFonts fonts, Sizes sizes, string widestPercent, string name)
    {
        var icon = style.Icons == StripIcons.None ? 0 : sizes.Icon + sizes.IconGap;
        var text = style.Layout == StripLayout.LargeValues
            ? Text(widestPercent, fonts.Large, Semibold, sizes.Large).Width
            : Math.Max(Text(widestPercent, fonts.Value, Semibold, sizes.Line).Width, Text(name, fonts.Text, Regular, sizes.Line).Width);
        return icon + text;
    }

    // So a rate keeps its place as it changes.
    private static int WidestRate(CultureInfo culture, StripFonts fonts, Sizes sizes) =>
        WidestRates.Max(rate => Text(MeterText.Rate(rate, culture), fonts.Text, Regular, sizes.Line).Width);

    private static void PaintMeter(StripCanvas canvas, StripStyle style, StripFonts fonts, Sizes sizes, StripColors colors, int x, string? value, string name, bool memory)
    {
        var middle = canvas.Height / 2.0;
        if (style.Icons != StripIcons.None)
        {
            PaintIcon(canvas, style, fonts, colors, memory, sizes.Icon, x, middle);
            x += sizes.Icon + sizes.IconGap;
        }

        if (style.Layout == StripLayout.LargeValues)
        {
            var large = Text(value ?? NoValue, fonts.Large, Semibold, sizes.Large);
            canvas.Draw(large, x, TopFor(large, middle), colors.Text);
            return;
        }

        var valueMask = Text(value ?? NoValue, fonts.Value, Semibold, sizes.Line);
        var nameMask = Text(name, fonts.Text, Regular, sizes.Line);
        var top = (canvas.Height - (valueMask.Height + nameMask.Height)) / 2;
        canvas.Draw(valueMask, x, top, colors.Text);
        canvas.Draw(nameMask, x, top + valueMask.Height, colors.Text, NameOpacity);
    }

    // CPU above memory, each with a small icon centered on its value.
    private static void PaintCompact(StripCanvas canvas, StripStyle style, StripFonts fonts, Sizes sizes, StripColors colors, int x, StripTexts texts)
    {
        var rows = new List<(string? Value, bool Memory)>();
        if (style.Cpu)
        {
            rows.Add((texts.Cpu, false));
        }

        if (style.Memory)
        {
            rows.Add((texts.Memory, true));
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var value = Text(rows[row].Value ?? NoValue, fonts.Value, Semibold, sizes.Line);
            var top = ((canvas.Height - (rows.Count * value.Height)) / 2) + (row * value.Height);
            var textX = x;
            if (style.Icons != StripIcons.None)
            {
                PaintIcon(canvas, style, fonts, colors, rows[row].Memory, sizes.SmallIcon, x, top + InkMiddle(value));
                textX += sizes.SmallIcon + sizes.SmallIconGap;
            }

            canvas.Draw(value, textX, top, colors.Text);
        }
    }

    // Receive above send, each after its arrow, centered on the rate.
    private static void PaintNetwork(StripCanvas canvas, StripStyle style, StripFonts fonts, Sizes sizes, StripColors colors, int x, StripTexts texts)
    {
        var colored = style.Icons == StripIcons.Color && !colors.HighContrast;
        var lineHeight = Text(texts.Receive ?? NoValue, fonts.Text, Regular, sizes.Line).Height;
        var firstTop = (canvas.Height - (2 * lineHeight)) / 2;
        foreach (var receive in new[] { true, false })
        {
            var rate = Text((receive ? texts.Receive : texts.Send) ?? NoValue, fonts.Text, Regular, sizes.Line);
            var arrow = Arrow(style, fonts, sizes, receive);
            var top = firstTop + (receive ? 0 : lineHeight);
            var arrowColor = colored ? (receive ? StripArt.Download : StripArt.Upload) : colors.Text;
            canvas.Draw(arrow, x, TopFor(arrow, top + InkMiddle(rate)), arrowColor);
            canvas.Draw(rate, x + arrow.Width + sizes.ArrowGap, top, colors.Text, receive ? 255 : SendOpacity);
        }
    }

    // Color icons are drawn; One color icons, and every icon in high contrast, are outline glyphs in the text color.
    private static void PaintIcon(StripCanvas canvas, StripStyle style, StripFonts fonts, StripColors colors, bool memory, int size, int x, double middle)
    {
        if (style.Icons == StripIcons.Color && !colors.HighContrast)
        {
            canvas.DrawIcon(memory ? StripArt.Memory(size) : StripArt.Processor(size), size, x, (int)Math.Round(middle - (size / 2.0)));
            return;
        }

        var glyph = Text(memory ? MemoryGlyph : ProcessorGlyph, fonts.Icons, Regular, size);
        canvas.Draw(glyph, x, TopFor(glyph, middle), colors.Text);
    }

    // Without icons, the arrows are plain characters in the text color.
    private static TextMask Arrow(StripStyle style, StripFonts fonts, Sizes sizes, bool receive) =>
        style.Icons == StripIcons.None
            ? Text(receive ? DownArrow : UpArrow, fonts.Text, Regular, sizes.Line)
            : Text(receive ? DownloadGlyph : UploadGlyph, fonts.Icons, Semibold, sizes.Line + 1);

    // Cached, so an update draws only text it hasn't drawn before.
    private static TextMask Text(string text, string font, int weight, int height)
    {
        if (Texts.Count > MaximumCachedTexts)
        {
            Texts.Clear();
            InkMiddles.Clear();
        }

        return Texts.GetOrAdd((text, font, weight, height), key => TextMask.Render(key.Text, key.Font, key.Weight, key.Height));
    }

    // The middle of the visible pixels, from the mask's top: text is centered by what's drawn, not
    // by the font's line box, which has room above and below.
    private static double InkMiddle(TextMask mask) => InkMiddles.GetOrAdd(mask, key =>
    {
        var ink = MeterIcons.Ink(key);
        return ink.IsEmpty ? key.Height / 2.0 : ink.Top + (ink.Height / 2.0);
    });

    // The top that centers a mask's visible pixels on a line.
    private static int TopFor(TextMask mask, double middle) => (int)Math.Round(middle - InkMiddle(mask));

    // The strip's sizes in pixels at a scale: 12-pixel text at 100%, two lines on the taskbar.
    private readonly struct Sizes(double scale)
    {
        public int Line { get; } = Scaled(12, scale);

        public int Large { get; } = Scaled(26, scale);

        public int Icon { get; } = Scaled(20, scale);

        public int IconGap { get; } = Scaled(6, scale);

        public int SmallIcon { get; } = Scaled(12, scale) + 4;

        public int SmallIconGap { get; } = Scaled(4, scale);

        public int ArrowGap { get; } = Scaled(3, scale);

        public int Padding { get; } = Scaled(10, scale);

        public int Between { get; } = Scaled(14, scale);

        public int HighlightInset { get; } = Scaled(4, scale);

        public int HighlightRadius { get; } = Scaled(4, scale);

        private static int Scaled(double pixels, double scale) => (int)Math.Round(pixels * scale);
    }
}
