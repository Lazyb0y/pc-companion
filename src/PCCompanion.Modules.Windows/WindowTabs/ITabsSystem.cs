using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// What <see cref="TabsController"/> reads from Windows and changes there, so tests can supply it
/// instead. Use it on the feature's thread. None of it waits for the windows' programs.
/// </summary>
public interface ITabsSystem
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>The pointer position, or <see langword="null"/> when it can't be read.</summary>
    Point? PointerPosition { get; }

    /// <summary>Whether Ctrl is held now.</summary>
    bool IsControlHeld { get; }

    /// <summary>Whether the window can be a tab now, and why not when it can't.</summary>
    TabRefusal Check(nint window);

    bool Exists(nint window);

    bool IsVisible(nint window);

    bool IsMinimized(nint window);

    bool IsMaximized(nint window);

    /// <summary>Whether Windows keeps the window out of sight, as on another virtual desktop.</summary>
    bool IsCloaked(nint window);

    /// <summary>Whether the window takes input; a window with a dialog open doesn't.</summary>
    bool IsEnabled(nint window);

    /// <summary>Whether the window covers its display without a frame, as a browser after F11 does.</summary>
    bool IsFullScreen(nint window);

    uint ProcessIdOf(nint window);

    /// <summary>The window's rectangle, invisible borders included.</summary>
    Rectangle? Bounds(nint window);

    /// <summary>The part of the window that shows, without its invisible borders.</summary>
    Rectangle? VisibleBounds(nint window);

    WindowPlacement? Placement(nint window);

    /// <summary>The visible windows the window owns, such as its palettes.</summary>
    IReadOnlyList<nint> VisibleOwnedWindows(nint window);

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>
    /// The front-most shown window that the taskbar shows and whose visible frame holds
    /// <paramref name="point"/>, other than <paramref name="except"/> and PC Companion's own, or 0.
    /// </summary>
    nint WindowAt(Point point, nint except);

    /// <summary>
    /// The shown windows behind <paramref name="window"/> in the order of windows, the front-most
    /// first, that the taskbar shows, other than minimized windows, windows on another virtual
    /// desktop and PC Companion's own; none when <paramref name="window"/> isn't shown.
    /// </summary>
    IReadOnlyList<nint> WindowsBehind(nint window);

    /// <summary>The display a window is mostly on.</summary>
    DisplayDetails? DisplayOf(nint window);

    /// <summary>Moves and sizes the window, with its invisible borders. Returns false when Windows refused, as for the next ones.</summary>
    bool Place(nint window, Rectangle bounds);

    /// <summary>Shows the window as <paramref name="how"/> says, activating it.</summary>
    bool Show(nint window, TabShow how);

    /// <summary>Shows a window in its current state without activating it, as for an owned window.</summary>
    bool ShowWithoutActivating(nint window);

    bool Hide(nint window);

    /// <summary>Shows a window minimized on the taskbar, without activating it.</summary>
    bool ShowMinimized(nint window);

    /// <summary>Brings a shown window, or its open dialog, to the front.</summary>
    bool Activate(nint window);

    /// <summary>
    /// Puts a window right behind <paramref name="inFront"/> in the order of windows, without
    /// activating it, once its program has done what it was asked before, such as showing it.
    /// </summary>
    bool PutBehind(nint window, nint inFront);

    /// <summary>Asks the window to close, as its Close button does.</summary>
    bool AskToClose(nint window);

    /// <summary>
    /// Gives a hidden window a normal rectangle, keeping it hidden, off the feature's thread, since
    /// Windows waits for the window's program.
    /// </summary>
    void SetHiddenPlacementLater(nint window, Rectangle normalBounds);

    /// <summary>
    /// Waits for Windows to draw <paramref name="frames"/> frames, off the feature's thread, then
    /// runs <paramref name="done"/> on it.
    /// </summary>
    void AfterFrames(int frames, Action done);

    /// <summary>Marks a window as one Window Tabs hid; <paramref name="owned"/> for an owned window hidden with its tab. Returns false when Windows refused.</summary>
    bool Mark(nint window, bool owned);

    void Unmark(nint window);

    /// <summary>The top-level windows, shown or hidden, that carry the mark, and whether each was an owned window.</summary>
    IReadOnlyList<(nint Window, bool Owned)> FindMarked();

    void PlayWarning();

    /// <summary>
    /// Reports the closes, shows, hides and title changes of the process's windows to the
    /// controller, until <see cref="Unwatch"/>. Calls are counted.
    /// </summary>
    void Watch(uint processId);

    void Unwatch(uint processId);

    /// <summary>Reports every move of the process's windows to the controller, until <see cref="StopFollowingMoves"/>. Calls are counted.</summary>
    void FollowMoves(uint processId);

    void StopFollowingMoves(uint processId);
}

/// <summary>How a tab's window is shown when it comes to the front.</summary>
public enum TabShow
{
    /// <summary>In the state it had, so a snapped window comes back snapped.</summary>
    AsItWas,

    /// <summary>Restored from maximized.</summary>
    Restored,

    Maximized,
}

/// <summary>Why a window can't be a tab, as "Windows that can't be tabs" in the spec lists.</summary>
public enum TabRefusal
{
    /// <summary>It can be a tab.</summary>
    None,

    /// <summary>It has closed.</summary>
    Gone,

    /// <summary>The taskbar doesn't show it, as a dialog, a palette or the desktop.</summary>
    NotOnTaskbar,

    /// <summary>Its app runs as administrator while PC Companion doesn't, so Windows refuses to mark it.</summary>
    RunsAsAdministrator,

    /// <summary>A Store app in Windows' shared app frame, which keeps its taskbar button when hidden.</summary>
    SharedAppFrame,

    /// <summary>It has no sizing border, so it can't take the group's size.</summary>
    NotResizable,

    /// <summary>It has a dialog open.</summary>
    DialogOpen,

    /// <summary>Its app stopped responding.</summary>
    NotResponding,

    Minimized,

    FullScreen,

    /// <summary>It's on another virtual desktop.</summary>
    OtherDesktop,

    /// <summary>A Drop-down Apps window, or one in Minimize to Tray's tray.</summary>
    OtherFeature,
}

/// <summary>What a group's strip shows: its place, its tabs' windows, the tab in front and the one chosen while it comes.</summary>
/// <param name="Chosen">The tab the user chose, which shows once its app shows it, or -1.</param>
/// <param name="DropMark">Where a dropped tab would go among the tabs, or -1 when nothing is dragged over the strip.</param>
public sealed record StripView(StripPlace Place, IReadOnlyList<nint> Tabs, int Front, int Chosen, int DropMark);

/// <summary>
/// A group's tab strip as <see cref="TabsController"/> uses it, so tests can supply it instead.
/// Use it on the feature's thread.
/// </summary>
public interface ITabStrip : IDisposable
{
    /// <summary>Raised when the user clicks a tab, or a screen reader chooses it, with its index.</summary>
    event EventHandler<int>? TabChosen;

    /// <summary>Raised when the user drops a tab they dragged, with its index and where on the screen.</summary>
    event EventHandler<TabDrop>? TabDropped;

    /// <summary>Raised when the user chooses an item from a tab's menu.</summary>
    event EventHandler<TabMenuChoice>? MenuChosen;

    /// <summary>Whether the pointer is on the strip.</summary>
    bool IsPointerOver { get; }

    /// <summary>Draws and shows the strip as <paramref name="view"/> says, right above <paramref name="window"/> in the order of windows.</summary>
    void Show(StripView view, nint window);

    void Hide();

    /// <summary>Puts the strip on top of other windows while something dragged may be dropped on it, or back in its place.</summary>
    void SetDropTarget(bool isTarget);

    /// <summary>Reads a tab's title again, as after its window's title changed.</summary>
    void TitleChanged(nint window);

    /// <summary>Has screen readers say <paramref name="text"/>.</summary>
    void Announce(string text);
}

/// <summary>A tab the user dropped at <paramref name="Point"/> on the screen.</summary>
public readonly record struct TabDrop(int Index, Point Point);

/// <summary>An item chosen from a tab's menu.</summary>
public readonly record struct TabMenuChoice(int Index, TabMenuItem Item);

/// <summary>The items of a tab's menu.</summary>
public enum TabMenuItem
{
    MoveOutOfGroup,
    CloseWindow,
    Settings,
}

/// <summary>The highlight on a window's top while a window dragged with Ctrl held would group with it.</summary>
public interface IDropHighlight : IDisposable
{
    void Show(Rectangle area);

    void Hide();
}
