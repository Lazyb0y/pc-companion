using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.Tests;

/// <summary>
/// The displays kept in memory, as Windows would change them: a setup in use and the one Windows
/// saved, the modes each display offers, and HDR on the displays that support it.
/// </summary>
internal sealed class FakeDisplaySystem : IDisplaySystem
{
    private const int BadConfiguration = 1610;
    private const int NotSupported = 50;

    /// <summary>The connected displays.</summary>
    public List<ConnectedDisplay> Displays { get; } = [];

    /// <summary>The setup in use, with each display's HDR.</summary>
    public List<DisplayPath> Current { get; set; } = [];

    /// <summary>The setup Windows saved for these displays.</summary>
    public List<DisplayPath> Saved { get; set; } = [];

    /// <summary>The resolutions and refresh rates every display offers.</summary>
    public List<DisplayMode> Modes { get; } = [new(1920, 1080, 60), new(1680, 1050, 60), new(3840, 2160, 60)];

    /// <summary>Whether turning HDR on or off fails.</summary>
    public bool HdrFails { get; set; }

    /// <summary>What was asked of Windows, in order.</summary>
    public List<string> Calls { get; } = [];

    public CurrentSetup Read() => new(new DisplaySetup([.. Displays], [.. Current]), new Dictionary<string, string>());

    public void Check(IReadOnlyList<DisplayPath> setup)
    {
        Calls.Add("check");
        Validate(setup);
    }

    // As Windows, it leaves HDR as it is.
    public void Apply(IReadOnlyList<DisplayPath> setup)
    {
        Calls.Add("apply");
        Validate(setup);
        Current = [.. setup.Select(path => path with { Hdr = Current.FirstOrDefault(old => old.Target == path.Target)?.Hdr ?? path.Hdr })];
    }

    public void SaveCurrent()
    {
        Calls.Add("save");
        Saved = [.. Current];
    }

    public void ApplySaved()
    {
        Calls.Add("apply saved");
        Current = [.. Saved.Where(path => Displays.Any(display => display.Target == path.Target))];
    }

    public void SetHdr(DisplayTarget target, bool enabled)
    {
        Calls.Add($"hdr {(enabled ? "on" : "off")} {target.Id}");
        var index = Current.FindIndex(path => path.Target == target);
        if (HdrFails || index < 0 || Current[index].Hdr == DisplayHdr.NotSupported)
        {
            throw new Win32Exception(NotSupported);
        }

        Current[index] = Current[index] with { Hdr = enabled ? DisplayHdr.On : DisplayHdr.Off };
    }

    public IReadOnlyList<DisplayMode> ListModes(DisplaySource source) => Modes;

    // As Windows does, refuses a display that isn't connected, and a mode that a display doesn't list.
    private void Validate(IReadOnlyList<DisplayPath> setup)
    {
        if (setup.Any(path => !Displays.Any(display => display.Target == path.Target)))
        {
            throw new ArgumentException("A display isn't connected.", nameof(setup));
        }

        if (!setup.All(path => Modes.Any(mode =>
            (mode.Width, mode.Height) == (path.Area.Width, path.Area.Height) && Math.Abs(mode.RefreshRate - path.RefreshRate.Hertz) < 1)))
        {
            throw new Win32Exception(BadConfiguration);
        }
    }
}
