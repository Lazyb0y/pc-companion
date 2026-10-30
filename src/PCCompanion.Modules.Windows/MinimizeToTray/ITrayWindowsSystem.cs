using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// What <see cref="TrayController"/> reads from Windows and changes there, so tests can supply it
/// instead; it reads what tells apps apart as App Windows and Hot Corners do. Use it on the
/// feature's thread. None of it waits for the windows' programs.
/// </summary>
public interface ITrayWindowsSystem : IAppIdentityReader
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    bool IsShiftHeld { get; }

    bool IsWindowsKeyHeld { get; }

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>Whether the window is one the taskbar may show: not a tool window, nor owned, unless it asks to be.</summary>
    bool MayHaveTaskbarButton(nint window);

    bool Exists(nint window);

    bool IsVisible(nint window);

    bool IsMinimized(nint window);

    /// <summary>Whether the window takes input; a window with a dialog open doesn't.</summary>
    bool IsEnabled(nint window);

    /// <summary>Whether Hide Apps minimized the window and brings it back itself, so it stays on the taskbar.</summary>
    bool IsMinimizedByHideApps(nint window);

    string ClassName(nint window);

    string Title(nint window);

    uint ProcessIdOf(nint window);

    /// <summary>Marks the window as one in the tray. Returns false when Windows refused.</summary>
    bool Mark(nint window);

    void Unmark(nint window);

    /// <summary>The top-level windows, shown or hidden, that carry the mark.</summary>
    IReadOnlyList<nint> FindMarked();

    /// <summary>Returns false when Windows refused, as for the next ones.</summary>
    bool Minimize(nint window);

    bool Hide(nint window);

    /// <summary>Shows a hidden window minimized on the taskbar, without activating it.</summary>
    bool ShowMinimized(nint window);

    /// <summary>Shows a hidden window as it was before it was minimized, and activates it.</summary>
    bool Restore(nint window);

    /// <summary>Brings a shown window, or its open dialog, to the front.</summary>
    bool Activate(nint window);

    void PlayWarning();

    /// <summary>
    /// Starts reporting the shows, destroys and title changes of the process's windows to the
    /// controller, until <see cref="Unwatch"/>.
    /// </summary>
    void Watch(uint processId);

    void Unwatch(uint processId);

    /// <summary>The size of notification icons, in pixels, for the main display's scale.</summary>
    int IconSize();

    /// <summary>The icon of the window's class, drawn at <paramref name="size"/>, or <see langword="null"/> when it has none.</summary>
    uint[]? ClassIcon(nint window, int size);

    /// <summary>
    /// Asks the window for its icon, drawn at <paramref name="size"/>; <paramref name="answered"/>
    /// gets it, or <see langword="null"/> when the window has none, on the feature's thread once
    /// the window's program answered. Returns false when Windows refused.
    /// </summary>
    bool RequestIcon(nint window, int size, Action<uint[]?> answered);

    /// <summary>The window glyph that stands in for an app without an icon, in the Windows mode's color.</summary>
    uint[] Glyph(int size);

    /// <summary>
    /// Reads the app's name and its program's icon off the feature's thread; <paramref name="read"/>
    /// gets them on the feature's thread.
    /// </summary>
    void ReadLooks(AppOfWindow app, int size, Action<TrayLooks> read);
}

/// <summary>An app's name, as Windows shows it, and its program's icon, or <see langword="null"/>.</summary>
public sealed record TrayLooks(string Name, uint[]? ProgramIcon);

/// <summary>The notification icons of the apps with windows in the tray, or fakes in tests.</summary>
public interface ITrayIcons
{
    /// <summary>
    /// Shows an icon's picture and tooltip, adding it with <paramref name="name"/>, which stays its
    /// name, the first time. Returns false when Windows refused.
    /// </summary>
    bool Show(uint id, uint[] pixels, int size, string name, string toolTip);

    void Remove(uint id);
}

/// <summary>How a window was asked to go to the tray.</summary>
public enum SendWay
{
    Shortcut,
    ShiftMinimize,
    ListedApp,
}

/// <summary>Whether a window went to the tray, and why not.</summary>
public enum SendOutcome
{
    /// <summary>It went, or it goes once its app's name is read.</summary>
    Sent,

    NoWindow,

    /// <summary>Not a window the taskbar shows, such as the desktop.</summary>
    NotOnTaskbar,

    /// <summary>A Store app in Windows' shared app frame, which Windows shows again.</summary>
    SharedAppFrame,

    /// <summary>The window has a dialog open.</summary>
    DialogOpen,

    /// <summary>Windows refused to mark or hide it, as for an app running as administrator.</summary>
    Refused,

    AlreadyInTray,

    /// <summary>Several windows minimized at once, as for Show desktop.</summary>
    MinimizedWithOthers,

    /// <summary>The window started to minimize while the Windows key was held.</summary>
    WindowsKeyHeld,

    /// <summary>Hide Apps minimized the window, and brings it back itself.</summary>
    MinimizedByHideApps,
}

/// <summary>Why a window left the tray.</summary>
public enum LeaveReason
{
    /// <summary>The user brought it back from its icon or a menu.</summary>
    BroughtBack,

    /// <summary>Its app showed it again.</summary>
    AppShowedIt,

    /// <summary>It closed, or its app ended.</summary>
    Closed,

    /// <summary>PC Companion stopped or the session is ending, so it's back on the taskbar.</summary>
    Released,
}

/// <summary>A command in an app's notification icon menu.</summary>
public enum TrayMenuCommand
{
    BringBack,
    BringAllBack,
    Settings,
}

/// <summary>An item in an app's notification icon menu: <see cref="Window"/> for <see cref="TrayMenuCommand.BringBack"/>.</summary>
public sealed record TrayMenuItem(TrayMenuCommand Command, string Text, nint Window = 0);

/// <summary>A window in the tray, for the PC Companion icon's menu.</summary>
public sealed record TrayWindowInfo(nint Window, string AppName, string Title);
