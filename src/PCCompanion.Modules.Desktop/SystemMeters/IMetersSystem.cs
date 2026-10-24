using System;
using System.Collections.Generic;
using System.Drawing;
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

    /// <summary>The Windows build number, such as 26100, which decides whether the strip can run.</summary>
    int WindowsBuild { get; }

    /// <summary>Creates the meters' icons on the meters thread's window.</summary>
    IMeterIcons CreateIcons(MessageWindow window);

    /// <summary>Creates the strip's window and starts watching the taskbar, on the meters thread's window.</summary>
    IStripHost CreateStripHost(MessageWindow window);

    /// <summary>Starts watching what pauses the meters, on the meters thread's window.</summary>
    IMeterPauses WatchPauses(MessageWindow window);

    /// <summary>Returns false when Windows refused.</summary>
    bool OpenTaskManager();

    /// <summary>Shows the meters' menu at a screen point and returns the command chosen.</summary>
    MeterMenuCommand ShowMenu(MessageWindow window, int x, int y);
}

/// <summary>
/// What pauses the meters: the session locked or the displays off, from Windows or fakes in tests.
/// Raised on the meters thread; the displays' state is reported once right after watching starts.
/// </summary>
public interface IMeterPauses : IDisposable
{
    event EventHandler? Locked;

    event EventHandler? Unlocked;

    event EventHandler? DisplaysOff;

    event EventHandler? DisplaysOn;
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

    bool IsHighContrast();
}

/// <summary>Where the meters show: their notification icons, or fakes in tests. Use it on the meters thread.</summary>
public interface IMeterIcons : IDisposable
{
    /// <summary>Raised when the user selects a meter's icon.</summary>
    event EventHandler? Selected;

    /// <summary>Raised when the user asks for a meter's menu, with the screen point to show it at.</summary>
    event EventHandler<TrayMenuEventArgs>? MenuRequested;

    /// <summary>Raised when the taskbar restarted and Windows took a meter's icon again.</summary>
    event EventHandler<MeterEventArgs>? AddedAgain;

    /// <summary>
    /// Shows a meter's picture and tooltip, adding its icon with the meter's name the first time.
    /// Returns false when Windows refused.
    /// </summary>
    bool Show(Meter meter, uint[] pixels, int size, string name, string toolTip);

    void Remove(Meter meter);
}

/// <summary>Which meter an event is about.</summary>
public sealed class MeterEventArgs(Meter meter) : EventArgs
{
    public Meter Meter { get; } = meter;
}

/// <summary>
/// The taskbar and the strip's window, from Windows, or fakes in tests. Use it on the meters thread.
/// </summary>
public interface IStripHost : IDisposable
{
    /// <summary>
    /// Raised when the taskbar's buttons, its settings or the displays may have changed: half a
    /// second after windows that get taskbar buttons appear or go away, when Windows' settings or
    /// the displays change, when the taskbar restarts, and every 30 seconds.
    /// </summary>
    event EventHandler? LayoutMayHaveChanged;

    /// <summary>
    /// Raised when the taskbar may have come in front of the strip: when Explorer's windows change
    /// order, take the focus or the mouse, or open a menu, or another window comes to the front.
    /// </summary>
    event EventHandler? ZOrderMayHaveChanged;

    /// <summary>Raised when the pointer comes onto the strip or leaves it.</summary>
    event EventHandler? PointerOverChanged;

    /// <summary>Raised when the user clicks the strip.</summary>
    event EventHandler? Clicked;

    /// <summary>Raised when the user right-clicks the strip, with the screen point to show a menu at.</summary>
    event EventHandler<TrayMenuEventArgs>? MenuRequested;

    bool IsShown { get; }

    bool IsPointerOver { get; }

    /// <summary>Reads the taskbar, or returns <see langword="null"/> when it isn't running.</summary>
    TaskbarState? ReadTaskbar();

    /// <summary>Lists what's on the taskbar, or returns <see langword="null"/> when it didn't answer.</summary>
    IReadOnlyList<TaskbarElement>? ReadElements(nint taskbar);

    /// <summary>Whether the taskbar is in front, as it is unless a full-screen app is.</summary>
    bool IsTaskbarTopmost(nint taskbar);

    /// <summary>
    /// Shows the strip's pixels at its bounds, with a description of every meter's values for its
    /// tooltip and for screen readers.
    /// </summary>
    void Show(uint[] pixels, Rectangle bounds, string description);

    void Hide();

    /// <summary>Brings the strip back in front of the taskbar when the taskbar came in front of it.</summary>
    bool KeepInFrontOf(nint taskbar);
}
