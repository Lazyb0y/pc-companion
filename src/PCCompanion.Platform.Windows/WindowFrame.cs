using System;
using System.Drawing;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A frame of one color around a part of the screen, such as another program's window, drawn by
/// four windows of this process that stay on top, let clicks through and never take the focus.
/// Create, use and dispose it on one thread, which must run a <see cref="MessageLoop"/>.
/// </summary>
public sealed class WindowFrame : IDisposable
{
    private readonly LayeredWindow[] _sides = new LayeredWindow[4];

    /// <param name="className">A prefix for the four windows' class names, unique to the app.</param>
    public WindowFrame(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        try
        {
            for (var i = 0; i < _sides.Length; i++)
            {
                _sides[i] = new LayeredWindow($"{className}.{i}", string.Empty, clickThrough: true);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsShown { get; private set; }

    /// <summary>
    /// Shows the frame just outside <paramref name="area"/>, <paramref name="thickness"/> physical
    /// pixels wide, in <paramref name="color"/> (0xRRGGBB), in front of other windows that stay on top.
    /// </summary>
    public void Show(Rectangle area, int thickness, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);

        Rectangle[] bounds =
        [
            new(area.Left - thickness, area.Top - thickness, area.Width + (2 * thickness), thickness),
            new(area.Left - thickness, area.Bottom, area.Width + (2 * thickness), thickness),
            new(area.Left - thickness, area.Top, thickness, area.Height),
            new(area.Right, area.Top, thickness, area.Height),
        ];

        var pixel = 0xFF000000 | (color & 0xFFFFFF);
        for (var i = 0; i < _sides.Length; i++)
        {
            if (bounds[i].Width <= 0 || bounds[i].Height <= 0)
            {
                continue;
            }

            var pixels = new uint[bounds[i].Width * bounds[i].Height];
            Array.Fill(pixels, pixel);
            _sides[i].Show(pixels, bounds[i]);
            _sides[i].BringToFront();
        }

        IsShown = true;
    }

    public void Hide()
    {
        foreach (var side in _sides)
        {
            side?.Hide();
        }

        IsShown = false;
    }

    public void Dispose()
    {
        foreach (var side in _sides)
        {
            side?.Dispose();
        }

        IsShown = false;
    }
}
