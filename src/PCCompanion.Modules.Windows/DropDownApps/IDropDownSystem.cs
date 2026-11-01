using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// What <see cref="DropDownController"/> reads from Windows and changes there, so tests can supply
/// it instead; it reads what tells apps apart as the other features do. Use it on the feature's
/// thread. None of it waits for the windows' programs.
/// </summary>
public interface IDropDownSystem : IAppIdentityReader, ISlideSystem
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>The pointer position, or <see langword="null"/> when it can't be read.</summary>
    Point? PointerPosition { get; }

    /// <summary>Whether Windows' animation effects are on.</summary>
    bool AnimationsOn { get; }

    /// <summary>Whether a mouse button is held down, as in a drag and drop.</summary>
    bool IsMouseButtonHeld { get; }

    /// <summary>What a top-level window is like now: its styles, owner and process.</summary>
    TopLevelWindow Describe(nint window);

    /// <summary>The visible top-level windows, front to back, minimized and cloaked ones included.</summary>
    IReadOnlyList<TopLevelWindow> Windows();

    /// <summary>The hidden windows in Minimize to Tray's tray, which carry its mark.</summary>
    IReadOnlyList<TopLevelWindow> TrayWindows();

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    bool Exists(nint window);

    bool IsVisible(nint window);

    bool IsMinimized(nint window);

    bool IsMaximized(nint window);

    /// <summary>Whether Windows keeps the window out of sight, as on another virtual desktop.</summary>
    bool IsCloaked(nint window);

    /// <summary>Whether the window takes input; a window with a dialog open doesn't.</summary>
    bool IsEnabled(nint window);

    bool IsTopmost(nint window);

    string ClassName(nint window);

    uint ProcessIdOf(nint window);

    /// <summary>The window's rectangle, invisible borders included.</summary>
    Rectangle? Bounds(nint window);

    /// <summary>The part of the window that shows, without its invisible borders.</summary>
    Rectangle? VisibleBounds(nint window);

    WindowPlacement? Placement(nint window);

    /// <summary>The visible windows the window owns, such as its palettes.</summary>
    IReadOnlyList<nint> VisibleOwnedWindows(nint window);

    /// <summary>The display at a point, or the nearest one.</summary>
    DisplayDetails? DisplayAt(Point point);

    DisplayDetails? MainDisplay();

    /// <summary>The display a window is mostly on.</summary>
    DisplayDetails? DisplayOf(nint window);

    /// <summary>Every display's whole area.</summary>
    IReadOnlyList<Rectangle> DisplayAreas();

    /// <summary>Moves and sizes the window, with its invisible borders. Returns false when Windows refused, as for the next ones.</summary>
    bool Place(nint window, Rectangle bounds);

    /// <summary>Shows a hidden window in its current state, without activating it.</summary>
    bool ShowWithoutActivating(nint window);

    bool Hide(nint window);

    /// <summary>Shows a window minimized on the taskbar, without activating it.</summary>
    bool ShowMinimized(nint window);

    /// <summary>Brings a shown window, or its open dialog, to the front.</summary>
    bool Activate(nint window);

    /// <summary>Brings a window to the front as it is, as when giving the front back.</summary>
    bool GiveFront(nint window);

    bool SetTopmost(nint window, bool topmost);

    /// <summary>
    /// Sets the window's normal rectangle and shows it there, or minimized, without activating it,
    /// off the feature's thread, since Windows waits for the window's program;
    /// <paramref name="done"/> gets whether it worked, on the feature's thread.
    /// </summary>
    void SetPlacementLater(nint window, Rectangle normalBounds, bool minimized, bool restoresMaximized, Action<bool> done);

    /// <summary>Marks the window as one Drop-down Apps hid. Returns false when Windows refused.</summary>
    bool Mark(nint window);

    void Unmark(nint window);

    /// <summary>Marks the window as one Drop-down Apps put on top.</summary>
    bool MarkOnTop(nint window);

    void UnmarkOnTop(nint window);

    /// <summary>The top-level windows, shown or hidden, that carry the mark.</summary>
    IReadOnlyList<nint> FindMarked();

    /// <summary>The top-level windows that carry the mark for being put on top.</summary>
    IReadOnlyList<nint> FindMarkedOnTop();

    void PlayWarning();

    /// <summary>
    /// Starts the app off the feature's thread, as <see cref="AppStarter"/> does; <paramref name="done"/>
    /// gets <see cref="DropDownOutcome.Started"/>, or why it didn't start, on the feature's thread.
    /// </summary>
    void StartLater(DropDownApp app, Action<DropDownOutcome> done);

    /// <summary>
    /// Starts reporting the shows, destroys and minimizes of the process's windows to the
    /// controller, until <see cref="Unwatch"/>.
    /// </summary>
    void Watch(uint processId);

    void Unwatch(uint processId);
}

/// <summary>What a press did, or why it did nothing; the recent results keep it.</summary>
public enum DropDownOutcome
{
    Shown,
    Hidden,
    BroughtToFront,
    BroughtFromOtherDesktop,
    Started,

    /// <summary>The app runs as administrator while PC Companion doesn't, so Windows refused.</summary>
    RunsAsAdministrator,

    /// <summary>A Store app in Windows' shared app frame, which keeps its taskbar button when hidden.</summary>
    SharedAppFrame,

    /// <summary>The window has a dialog open.</summary>
    DialogOpen,

    /// <summary>The program is missing or isn't an <c>.exe</c>.</summary>
    ProgramMissing,

    /// <summary>The app ID isn't in Start's Apps folder.</summary>
    AppIdNotFound,

    /// <summary>Windows couldn't start the program or open the app.</summary>
    StartFailed,

    /// <summary>The program needs admin rights, which Drop-down Apps never starts it with.</summary>
    NeedsAdminRights,

    /// <summary>No window of the app came within 30 seconds of starting it.</summary>
    NoWindowInTime,

    /// <summary>The window stayed on another virtual desktop.</summary>
    StayedOnOtherDesktop,

    /// <summary>Another program has the app's shortcut.</summary>
    ShortcutTaken,
}

/// <summary>What happened for the listed app at <paramref name="AppIndex"/>, from 0.</summary>
public readonly record struct DropDownReport(int AppIndex, DropDownOutcome Outcome);
