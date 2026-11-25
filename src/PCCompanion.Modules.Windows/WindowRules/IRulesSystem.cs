using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>Why rules leave a window alone, as "Which windows" in docs/features/windows/window-rules.md says.</summary>
public enum RuleRefusal
{
    None,

    /// <summary>The window has closed.</summary>
    Gone,

    /// <summary>Not a main window: not on the taskbar, without a title bar, or one of the shell's.</summary>
    NotMain,

    /// <summary>The window stopped responding, or is the stand-in Windows shows for one.</summary>
    NotResponding,

    /// <summary>A window of PC Companion's, or one another feature has: drop-down, in the tray or a hidden tab.</summary>
    OtherFeature,

    /// <summary>A window of an app running as administrator, while PC Companion doesn't.</summary>
    RunsAsAdministrator,
}

/// <summary>
/// Window Rules' view of Windows, for <see cref="RulesController"/>. Calls that wait for another
/// program run on the thread pool; everything else returns at once.
/// </summary>
public interface IRulesSystem
{
    /// <summary>Raised on the feature's thread when a followed window moves or changes size.</summary>
    event EventHandler<nint>? WindowMoved;

    /// <summary>Whether rules may act on <paramref name="window"/>, and why not.</summary>
    RuleRefusal Check(nint window);

    /// <summary>The window's app, title and class, or <see langword="null"/> when it has closed.</summary>
    WindowFacts? Facts(nint window);

    bool Exists(nint window);

    bool IsCloaked(nint window);

    /// <summary>Whether Windows has the window snapped (<c>IsWindowArranged</c>).</summary>
    bool IsArranged(nint window);

    bool IsFullScreen(nint window);

    /// <summary>The window's rectangle with its invisible borders.</summary>
    Rectangle? Bounds(nint window);

    /// <summary>The window's visible frame (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>).</summary>
    Rectangle? VisibleBounds(nint window);

    /// <summary>How the window shows, and its normal rectangle with its borders.</summary>
    WindowPlacement? Placement(nint window);

    /// <summary>The DPI the window is drawn at: 96 at 100% scale.</summary>
    uint Dpi(nint window);

    /// <summary>The work area of the display the window is mostly on.</summary>
    Rectangle? WorkAreaOf(nint window);

    /// <summary>The main display's work area.</summary>
    Rectangle? MainWorkArea { get; }

    /// <summary>The work area of the display with the pointer.</summary>
    Rectangle? PointerWorkArea { get; }

    /// <summary>A chosen display's work area, or <see langword="null"/> when it isn't connected.</summary>
    Rectangle? WorkAreaOf(RuleDisplay display);

    /// <summary>The windows the taskbar shows, front to back.</summary>
    IReadOnlyList<nint> TaskbarWindows();

    /// <summary>Moves and sizes the window to <paramref name="bounds"/>, with its borders, without waiting.</summary>
    bool Place(nint window, Rectangle bounds);

    /// <summary>
    /// Gives the window the normal rectangle <paramref name="normalBounds"/>, with its borders, and
    /// shows it <paramref name="show"/>, without activating it unless maximized. It waits for the
    /// app, so it runs on the thread pool.
    /// </summary>
    void SetPlacement(nint window, Rectangle normalBounds, WindowState show);

    /// <summary>Hears of the window's moves through <see cref="WindowMoved"/>, until <see cref="Unfollow"/>.</summary>
    void Follow(nint window);

    void Unfollow(nint window);
}
