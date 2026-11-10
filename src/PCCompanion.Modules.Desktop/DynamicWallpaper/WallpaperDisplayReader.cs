using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// Reads the connected displays and a hash of each one's serial number, as Display Profiles reads
/// them, for the host and the page. A display's EDID is read once for each device path. It waits
/// for Windows, so call it off the UI thread.
/// </summary>
public sealed class WallpaperDisplayReader
{
    // The serial number hash of each device path read so far.
    private readonly Lock _serialsGate = new();
    private readonly Dictionary<string, string> _serials = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The connected displays; none when Windows refuses to read them.</summary>
    public WallpaperDisplays Read()
    {
        DisplaySetup setup;
        try
        {
            setup = DisplayConfiguration.Read();
        }
        catch (Win32Exception)
        {
            return WallpaperDisplays.None;
        }

        var serials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in setup.Displays)
        {
            serials[display.DevicePath] = SerialOf(display.DevicePath);
        }

        return new WallpaperDisplays(setup.Displays, serials);
    }

    /// <summary>A connected display as the settings keep it.</summary>
    public static WallpaperDisplay ToSettings(ConnectedDisplay display, WallpaperDisplays displays)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(displays);

        return new WallpaperDisplay
        {
            Model = display.Model,
            Serial = displays.SerialOf(display),
            Connection = display.DevicePath,
            BuiltIn = display.Connection == DisplayConnection.BuiltIn,
            Name = display.Name,
        };
    }

    // A display whose EDID Windows can't give now has no serial number this time, and its EDID is
    // read again the next time.
    private string SerialOf(string devicePath)
    {
        lock (_serialsGate)
        {
            if (_serials.TryGetValue(devicePath, out var known))
            {
                return known;
            }
        }

        string serial;
        try
        {
            serial = DisplayRecognition.HashSerial(DisplayEdid.ReadSerialNumberAsync(devicePath).GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return string.Empty;
        }

        lock (_serialsGate)
        {
            _serials[devicePath] = serial;
        }

        return serial;
    }
}
