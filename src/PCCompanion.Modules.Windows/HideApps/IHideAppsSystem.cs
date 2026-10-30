using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// What Hide Apps reads from Windows and changes there, so tests can supply it instead; it reads
/// what tells apps apart as App Windows and Minimize to Tray do. Use it on the feature's thread.
/// None of it waits for the windows' programs.
/// </summary>
public interface IHideAppsSystem : IAppIdentityReader
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>The visible top-level windows, front to back.</summary>
    IReadOnlyList<TopLevelWindow> ListWindows();

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    bool Exists(nint window);

    bool IsMinimized(nint window);

    /// <summary>Whether the window takes input; a window with a dialog open doesn't.</summary>
    bool IsEnabled(nint window);

    bool HasMinimizeButton(nint window);

    /// <summary>The window's class name, which tells Windows' stand-ins apart and goes in the log; never a title.</summary>
    string ClassName(nint window);

    /// <summary>The area of the display the window is mostly on, or <see langword="null"/>.</summary>
    Rectangle? DisplayOf(nint window);

    /// <summary>Whether the window's program hasn't handled its messages for 5 seconds.</summary>
    bool IsNotResponding(nint window);

    /// <summary>Minimizes the window in front; Windows brings the next window to the front. Returns false when Windows refused, as the next ones.</summary>
    bool Minimize(nint window);

    /// <summary>Minimizes a window without taking the front.</summary>
    bool MinimizeWithoutActivating(nint window);

    /// <summary>Restores a minimized window as it was, without activating it or changing its place in the order of windows.</summary>
    bool RestoreWithoutActivating(nint window);

    /// <summary>Asks the window's program to minimize it, for an app running as administrator. Returns false when the request couldn't be posted.</summary>
    bool AskToMinimize(nint window);

    /// <summary>Asks the window's program to restore it; the window takes the front.</summary>
    bool AskToRestore(nint window);

    /// <summary>Asks Windows to put the window right behind <paramref name="inFront"/>; the window's program does it later.</summary>
    bool PutBehind(nint window, nint inFront);

    /// <summary>The first visible window behind the window, or 0.</summary>
    nint NextVisibleBehind(nint window);

    /// <summary>
    /// Whether Alt + Tab shows the window: shown, not cloaked, and not a tool window or owned,
    /// unless it asks to be on the taskbar.
    /// </summary>
    bool IsAppWindow(nint window);

    /// <summary>Brings a window, or its open dialog, to the front.</summary>
    bool Activate(nint window);

    void PlayWarning();

    /// <summary>Starts or stops reporting each window that starts to come back from minimized to <see cref="HideAppsController.OnRestored"/>.</summary>
    void WatchRestores(bool watch);

    /// <summary>Starts reporting the destroyed windows of the process to <see cref="HideAppsController.OnDestroyed"/>, until <see cref="Unwatch"/>.</summary>
    void Watch(uint processId);

    void Unwatch(uint processId);

    /// <summary>Starts or stops reporting each change in the order of windows to <see cref="OrderedRestore.OnOrderChanged"/>.</summary>
    void WatchOrder(bool watch);
}

/// <summary>What a press of a shortcut, or the menu item, asked for.</summary>
public enum HideAction
{
    HideApp,
    HideOthers,
    ShowAll,
}

/// <summary>What a press did.</summary>
public enum PressOutcome
{
    /// <summary>Windows were minimized.</summary>
    Hid,

    /// <summary>Windows that Hide Apps minimized came back.</summary>
    BroughtBack,

    /// <summary>No app's window is in front, such as the desktop or the taskbar.</summary>
    NoAppInFront,

    /// <summary>No window to minimize or bring back.</summary>
    NothingToDo,
}

/// <summary>Why a window that would have gone stayed on screen.</summary>
public enum StayReason
{
    /// <summary>Always on top; left alone by Hide others.</summary>
    Topmost,

    /// <summary>On another display, with Hide others only on the display in front.</summary>
    OtherDisplay,

    /// <summary>No Minimize button, as Windows + M leaves it.</summary>
    NoMinimizeButton,

    /// <summary>A dialog is open, which minimizing can cancel; the warning sound plays.</summary>
    DialogOpen,

    /// <summary>Windows refused even to pass on the request; the warning sound plays.</summary>
    Refused,
}

/// <summary>What a press did, how many windows went or came back, and how many stayed with the warning sound.</summary>
public sealed record PressResult(PressOutcome Outcome, int Windows = 0, int Warned = 0);
