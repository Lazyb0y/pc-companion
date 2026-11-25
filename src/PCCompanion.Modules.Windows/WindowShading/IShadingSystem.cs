using System;
using System.Collections.Generic;
using System.Drawing;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>Why a window isn't rolled up. See "Which windows" in docs/features/windows/window-shading.md.</summary>
public enum ShadeRefusal
{
    None,

    /// <summary>Not a main window with a title bar that the taskbar shows, or not shown.</summary>
    NotMainWindow,

    Minimized,

    FullScreen,

    /// <summary>The window has a dialog open, recognized by it being disabled.</summary>
    DialogOpen,

    NotResponding,

    /// <summary>One of Window Tabs' tabs.</summary>
    WindowTab,

    /// <summary>One of Drop-down Apps' windows.</summary>
    DropDown,

    /// <summary>A window in Minimize to Tray's tray.</summary>
    InTray,

    /// <summary>One of PC Companion's own windows.</summary>
    Own,

    /// <summary>A window of an app running as administrator, while PC Companion runs without admin rights.</summary>
    RunsAsAdministrator,

    /// <summary>Windows refused minimizing the window.</summary>
    Refused,

    /// <summary>The window closed, or its frame couldn't be read.</summary>
    Gone,

    /// <summary>Its app hid the window as it was minimized, as apps that live in their notification icon do.</summary>
    HiddenByItsApp,
}

/// <summary>A choice in a strip's menu.</summary>
public enum ShadeMenuChoice
{
    None,
    RollDown,
    Close,
}

/// <summary>
/// The strip that takes a rolled-up window's title bar's place, showing a picture of it. Its
/// events come on the feature's thread; positions are in physical pixels.
/// </summary>
public interface IShadeStrip : IDisposable
{
    /// <summary>A click without a drag, at a point in the strip.</summary>
    event EventHandler<Point>? Clicked;

    /// <summary>The wheel turned on the strip: above zero up, below zero down.</summary>
    event EventHandler<int>? Wheeled;

    /// <summary>A right-click, at a point on the screen to show a menu at.</summary>
    event EventHandler<Point>? MenuRequested;

    /// <summary>A drag ended, with the strip's new place on the screen.</summary>
    event EventHandler<Rectangle>? Moved;

    /// <summary>The pointer came onto the strip.</summary>
    event EventHandler? PointerEntered;

    /// <summary>The pointer left the strip.</summary>
    event EventHandler? PointerLeft;

    /// <summary>Where the strip is on the screen.</summary>
    Rectangle Bounds { get; }

    void SetTopmost(bool topmost);

    void Show();

    void Hide();
}

/// <summary>
/// What Window Shading needs from Windows, so the controller can be tested without real windows.
/// Every member is called on the feature's thread, and none waits for another program.
/// </summary>
public interface IShadingSystem
{
    /// <summary>Whether <paramref name="window"/> can be rolled up, or why not.</summary>
    ShadeRefusal Check(nint window);

    /// <summary>The part of the window that shows, without its invisible borders, or null when it's gone.</summary>
    Rectangle? Frame(nint window);

    /// <summary>The window's rectangle, with its invisible borders, or null when it's gone.</summary>
    Rectangle? Bounds(nint window);

    /// <summary>The window's DPI.</summary>
    uint Dpi(nint window);

    /// <summary>Where Windows says the title bar's buttons are, in the window's rectangle with its borders, or null.</summary>
    Rectangle? CaptionButtons(nint window);

    /// <summary>Whether the window is laid out right to left, with its buttons at the left.</summary>
    bool IsRightToLeft(nint window);

    /// <summary>Windows' height of a title bar at <paramref name="dpi"/>, with the top border.</summary>
    int SystemCaptionHeight(uint dpi);

    /// <summary>
    /// Asks the window where its title bar ends, without waiting: <paramref name="measured"/> gets,
    /// once, on this thread, the height from the frame's top, at most <paramref name="band"/>, or
    /// null when the window didn't say in time.
    /// </summary>
    void MeasureTitleBar(nint window, Rectangle frame, int band, Action<int?> measured);

    /// <summary>The window in front, or 0.</summary>
    nint Foreground { get; }

    /// <summary>Where the pointer is.</summary>
    Point PointerPosition { get; }

    /// <summary>
    /// Minimizes the window as its Minimize button does, which gives the front to the window behind
    /// it when <paramref name="inFront"/>, and otherwise without taking the front. Returns false
    /// when Windows refused.
    /// </summary>
    bool Minimize(nint window, bool inFront);

    /// <summary>Restores the window, and brings it to the front when <paramref name="activate"/>.</summary>
    void RollDown(nint window, bool activate);

    /// <summary>Restores the window, brings it to the front and asks it to close, as its Close button does.</summary>
    void RollDownAndClose(nint window);

    /// <summary>Restores the window without taking the front, for hover.</summary>
    void ShowWithoutActivating(nint window);

    /// <summary>Minimizes the window without taking the front, for hover.</summary>
    void MinimizeWithoutActivating(nint window);

    /// <summary>Moves a minimized window's normal place by <paramref name="offset"/>, keeping it minimized.</summary>
    void MoveWhileMinimized(nint window, Point offset);

    /// <summary>Whether the window has a menu or another window of its app open, or is being moved or sized.</summary>
    bool IsBusy(nint window);

    bool IsMinimized(nint window);

    /// <summary>The window's title, kept in memory only, to name its strip for screen readers.</summary>
    string Title(nint window);

    /// <summary>Marks a window as rolled up. Returns false when Windows refused.</summary>
    bool Mark(nint window);

    void Unmark(nint window);

    /// <summary>The windows that carry the mark.</summary>
    IReadOnlyList<nint> FindMarked();

    /// <summary>
    /// Shows a strip at <paramref name="bounds"/>, with the picture of <paramref name="source"/>,
    /// a part of <paramref name="window"/>'s rectangle with its borders, named <paramref name="name"/>.
    /// </summary>
    IShadeStrip CreateStrip(nint window, Rectangle bounds, Rectangle source, bool topmost, string name);

    /// <summary>Shows a strip's menu at <paramref name="at"/>, laid out as the user's language is, and returns the choice.</summary>
    ShadeMenuChoice ShowMenu(IShadeStrip strip, Point at);

    /// <summary>Starts or stops hearing that the window is restored, closes or hides.</summary>
    void Watch(nint window);

    void Unwatch(nint window);

    /// <summary>Plays Windows' warning sound.</summary>
    void PlayWarning();
}
