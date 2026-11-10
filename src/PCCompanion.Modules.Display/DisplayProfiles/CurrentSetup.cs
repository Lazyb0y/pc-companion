using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// The displays now: the setup Windows reports, and a hash of each connected display's serial number.
/// </summary>
/// <param name="Setup">The connected displays and the setup in use.</param>
/// <param name="Serials">
/// The serial number hash of each connected display that has one, by its device path; see <see cref="DisplayRecognition.HashSerial"/>.
/// </param>
public sealed record CurrentSetup(DisplaySetup Setup, IReadOnlyDictionary<string, string> Serials)
{
    /// <summary>The serial number hash of a connected display; empty when it has none.</summary>
    public string SerialOf(ConnectedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return Serials.GetValueOrDefault(display.DevicePath, string.Empty);
    }

    /// <summary>The path of a connected display that's on; <see langword="null"/> for one that's off.</summary>
    public DisplayPath? PathOf(ConnectedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return Setup.Paths.FirstOrDefault(path => path.Target == display.Target);
    }

    /// <summary>
    /// A profile made from the setup in use: every connected display, the ones that are on first,
    /// with the main display at the start and the others from left to right.
    /// </summary>
    public DisplayProfile ToProfile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var on = Setup.Displays
            .Select(display => (Display: display, Path: PathOf(display)))
            .Where(pair => pair.Path is not null)
            .OrderBy(pair => pair.Path!.Area.Location != Point.Empty)
            .ThenBy(pair => pair.Path!.Area.X)
            .ThenBy(pair => pair.Path!.Area.Y)
            .Select((pair, index) => ToProfileDisplay(pair.Display, pair.Path!, main: index == 0 && pair.Path!.Area.Location == Point.Empty));
        var off = Setup.Displays.Where(display => PathOf(display) is null).Select(Identity);
        return new DisplayProfile { Name = name.Trim(), Displays = [.. on, .. off] };
    }

    private ProfileDisplay Identity(ConnectedDisplay display) => new()
    {
        Model = display.Model,
        Serial = SerialOf(display),
        Connection = display.DevicePath,
        BuiltIn = display.Connection == DisplayConnection.BuiltIn,
        Name = display.Name,
    };

    private ProfileDisplay ToProfileDisplay(ConnectedDisplay display, DisplayPath path, bool main)
    {
        var turned = path.Orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
        return Identity(display) with
        {
            On = true,
            Main = main,
            X = path.Area.X,
            Y = path.Area.Y,
            Width = turned ? path.Area.Height : path.Area.Width,
            Height = turned ? path.Area.Width : path.Area.Height,
            RefreshRate = new ProfileRefreshRate { Numerator = path.RefreshRate.Numerator, Denominator = path.RefreshRate.Denominator },
            Orientation = path.Orientation,
            Hdr = path.Hdr switch
            {
                DisplayHdr.On => true,
                DisplayHdr.Off => false,
                _ => null,
            },
        };
    }
}
