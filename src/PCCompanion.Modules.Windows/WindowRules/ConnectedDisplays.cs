using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>A connected display, as rules choose and recognize it.</summary>
/// <param name="Display">The display, as Windows lists it.</param>
/// <param name="Serial">A hash of its serial number; empty when it has none.</param>
/// <param name="WorkArea">Its work area, or <see langword="null"/> while it's off.</param>
public sealed record ConnectedRuleDisplay(ConnectedDisplay Display, string Serial, Rectangle? WorkArea)
{
    /// <summary>The display as a rule keeps it when it's chosen.</summary>
    public RuleDisplay ToRule() => new()
    {
        Which = nameof(DisplayChoice.Chosen),
        Name = Display.Name,
        Model = Display.Model,
        Serial = Serial,
        Connection = Display.DevicePath,
        BuiltIn = Display.Connection == DisplayConnection.BuiltIn,
    };
}

/// <summary>
/// The connected displays, recognized as Display Profiles recognizes them, so a rule's chosen
/// display is found on any connection. See "Display" in docs/features/windows/window-rules.md.
/// </summary>
public static class ConnectedDisplays
{
    /// <summary>
    /// Reads the connected displays, with their serial number hashes and work areas. It waits for
    /// each display's EDID, so call it on the thread pool. Returns none when Windows can't list them.
    /// </summary>
    public static IReadOnlyList<ConnectedRuleDisplay> Read()
    {
        DisplaySetup setup;
        try
        {
            setup = DisplayConfiguration.Read();
        }
        catch (Win32Exception)
        {
            return [];
        }

        var details = Displays.GetDetails();
        return
        [
            .. setup.Displays.Select(display =>
            {
                var area = setup.Paths.FirstOrDefault(path => path.Target == display.Target)?.Area;
                var workArea = area is { } shown ? details.FirstOrDefault(detail => detail.Bounds == shown)?.WorkArea : null;
                return new ConnectedRuleDisplay(display, SerialOf(display.DevicePath), workArea);
            }),
        ];
    }

    /// <summary>
    /// The work area of the display a rule chose, among <paramref name="displays"/>, or
    /// <see langword="null"/> when it isn't connected or is off.
    /// </summary>
    public static Rectangle? WorkAreaOf(RuleDisplay display, IReadOnlyList<ConnectedRuleDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(displays);

        if (displays.Count == 0)
        {
            return null;
        }

        var connected = displays.Select(found => found.Display).ToList();
        var recognized = DisplayRecognition.Recognize(
            [display.AsKnown],
            connected,
            other => displays.First(found => found.Display == other).Serial);
        return recognized.Displays[0] is { } match ? displays.First(found => found.Display == match).WorkArea : null;
    }

    private static string SerialOf(string devicePath)
    {
        try
        {
            return DisplayRecognition.HashSerial(DisplayEdid.ReadSerialNumberAsync(devicePath).GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return string.Empty;
        }
    }
}
