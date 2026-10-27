using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using PCCompanion.Modules.Display.DisplayProfiles;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>A display in a profile's picture: its place, size and number, and whether it's the main display.</summary>
/// <param name="Margin">Its distance from the picture's top-left corner.</param>
/// <param name="Label">The places in the profile of the displays it stands for, from 1: several for displays that show the same picture.</param>
public sealed record PictureDisplay(Thickness Margin, double Width, double Height, string Label, bool IsMain)
{
    public bool IsNotMain => !IsMain;

    // A label that fits a small box: the displays' numbers, smaller in a smaller box.
    public double FontSize => Math.Clamp(Math.Min(Width, Height) / 2.2, 8, 14);
}

/// <summary>
/// A picture of a profile's arrangement, drawn as the displays stand on the desktop, also in
/// right-to-left languages: each display that's on, scaled to fit the picture, numbered by its
/// place in the profile, the main display in the accent color.
/// </summary>
public static class DisplayProfilesPicture
{
    /// <summary>The largest the picture is, in effective pixels.</summary>
    public const double MaximumWidth = 160;

    /// <inheritdoc cref="MaximumWidth"/>
    public const double MaximumHeight = 64;

    // A gap between displays that touch, so each shows its own edge.
    private const double Gap = 2;

    /// <summary>The displays of a profile that passed the checks, and the picture's size.</summary>
    public static (IReadOnlyList<PictureDisplay> Displays, double Width, double Height) Draw(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var pictures = profile.Displays
            .Select((display, index) => (display, index))
            .Where(pair => pair.display.On)
            .GroupBy(pair => pair.display.Area())
            .Select(group => (Area: group.Key, Numbers: group.Select(pair => pair.index + 1).ToList(), Main: group.Any(pair => pair.display.Main)))
            .ToList();
        if (pictures.Count == 0)
        {
            return ([], 0, 0);
        }

        var bounds = pictures.Select(picture => picture.Area).Aggregate(Rectangle.Union);
        var scale = Math.Min(MaximumWidth / bounds.Width, MaximumHeight / bounds.Height);
        var displays = pictures.Select(picture => new PictureDisplay(
            new Thickness(((picture.Area.X - bounds.X) * scale) + (Gap / 2), ((picture.Area.Y - bounds.Y) * scale) + (Gap / 2), 0, 0),
            Math.Max(1, (picture.Area.Width * scale) - Gap),
            Math.Max(1, (picture.Area.Height * scale) - Gap),
            string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator, picture.Numbers.Select(number => number.ToString(CultureInfo.CurrentCulture))),
            picture.Main)).ToList();
        return (displays, bounds.Width * scale, bounds.Height * scale);
    }
}
