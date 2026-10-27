using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// The displays now: the setup Windows reports, and a hash of each connected display's serial number.
/// </summary>
/// <param name="Setup">The connected displays and the setup in use.</param>
/// <param name="Serials">
/// The serial number hash of each connected display that has one, by its device path; see <see cref="HashSerial"/>.
/// </param>
public sealed record CurrentSetup(DisplaySetup Setup, IReadOnlyDictionary<string, string> Serials)
{
    // A hash of 8 bytes tells a few displays apart, and is all that's kept.
    private const int SerialHashBytes = 8;

    /// <summary>
    /// A hash of a display's serial number, 16 hexadecimal digits, so the settings keep no serial
    /// number; empty for a display without one. See "Security and privacy" in
    /// docs/features/display/display-profiles.md.
    /// </summary>
    public static string HashSerial(DisplaySerialNumber? serial)
    {
        if (serial is not { } value)
        {
            return string.Empty;
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{value.Number:X8}:{value.Text}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, SerialHashBytes);
    }

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
