using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>A meter, in the order the meters show.</summary>
public enum Meter
{
    Cpu,
    Memory,
    Network,
}

/// <summary>A command in the meters' menu.</summary>
public enum MeterMenuCommand
{
    None,
    TaskManager,
    Settings,
    Exit,
}

/// <summary>What the meters thread uses from Windows; tests use fakes.</summary>
public interface IMetersSystem
{
    ISystemReadings Readings { get; }

    IMeterAppearance Appearance { get; }

    /// <summary>Creates the meters' icons on the meters thread's window.</summary>
    IMeterIcons CreateIcons(MessageWindow window);

    /// <summary>Returns false when Windows refused.</summary>
    bool OpenTaskManager();

    /// <summary>Shows the meters' menu at a screen point and returns the command chosen.</summary>
    MeterMenuCommand ShowMenu(MessageWindow window, int x, int y);
}

/// <summary>How the meters look now, which follows Windows' settings.</summary>
public interface IMeterAppearance
{
    /// <summary>The size of the meters' notification icons, in pixels, for the main display's DPI.</summary>
    int IconSize();

    /// <summary>
    /// The color to draw in, as 0xRRGGBB: white in the dark Windows mode, black in the light
    /// mode, and the theme's text color in high contrast.
    /// </summary>
    uint Color();
}

/// <summary>Where the meters show: their notification icons, or fakes in tests. Use it on the meters thread.</summary>
public interface IMeterIcons : IDisposable
{
    /// <summary>Raised when the user selects a meter's icon.</summary>
    event EventHandler? Selected;

    /// <summary>Raised when the user asks for a meter's menu, with the screen point to show it at.</summary>
    event EventHandler<TrayMenuEventArgs>? MenuRequested;

    /// <summary>
    /// Shows a meter's picture and tooltip, adding its icon with the meter's name the first time.
    /// Returns false when Windows refused.
    /// </summary>
    bool Show(Meter meter, uint[] pixels, int size, string name, string toolTip);

    void Remove(Meter meter);
}
