using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// What End App reads from Windows and changes there, so tests can supply it instead. Use it on
/// the feature's thread, except <see cref="ListProcesses"/>, <see cref="StartTime"/> and
/// <see cref="AppNameAndIcon"/>, which take longer and run on the thread pool. None of it waits for
/// the windows' programs.
/// </summary>
public interface IEndAppSystem
{
    /// <summary>The host's own process, whose windows are never chosen and which never ends.</summary>
    uint OwnProcessId { get; }

    /// <summary>The Windows folder, such as <c>C:\Windows</c>, without a trailing separator.</summary>
    string WindowsFolder { get; }

    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>Where the pointer is, in physical pixels, or <see langword="null"/> when that can't be read.</summary>
    Point? PointerPosition { get; }

    /// <summary>The visible top-level windows, front to back.</summary>
    IReadOnlyList<EndAppWindow> ListWindows();

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>The window's class name, which tells Windows' stand-ins and shared frames apart; never a title.</summary>
    string ClassName(nint window);

    /// <summary>The window's rectangle with its invisible borders (<c>GetWindowRect</c>).</summary>
    Rectangle WindowRect(nint window);

    /// <summary>The ID of the window's process, or 0 when the window has closed.</summary>
    uint ProcessIdOf(nint window);

    /// <summary>Whether the window's program hasn't handled its messages for 5 seconds (<c>IsHungAppWindow</c>).</summary>
    bool IsNotResponding(nint window);

    /// <summary>
    /// The top-level windows, shown or not and other than Windows' stand-ins, that stopped
    /// responding and have the rectangle <paramref name="bounds"/>: those a stand-in stands for.
    /// </summary>
    IReadOnlyList<nint> NotRespondingWindowsAt(Rectangle bounds);

    /// <summary>The core window of a Store app in Windows' shared frame, or 0 when it has none.</summary>
    nint CoreWindowOf(nint frame);

    /// <summary>The DPI the window is drawn at: 96 at 100% scale.</summary>
    int DpiOf(nint window);

    /// <summary>The work area of the display the window is mostly on, or <see langword="null"/>.</summary>
    Rectangle? WorkAreaOf(nint window);

    /// <summary>The full path of the process's program file, or <see langword="null"/> when it can't be read.</summary>
    string? ProgramPath(uint processId);

    /// <summary>Whether the process runs as administrator, or <see langword="null"/> when that can't be read.</summary>
    bool? IsElevated(uint processId);

    /// <summary>
    /// Opens the process with the rights to end it. Returns <see langword="null"/>, with Windows'
    /// error code, when Windows refuses.
    /// </summary>
    IEndAppProcess? Open(uint processId, out int errorCode);

    /// <summary>Every running process with its parent's ID; on the thread pool.</summary>
    IReadOnlyList<ProcessEntry> ListProcesses();

    /// <summary>When the process started, in UTC, or <see langword="null"/>; on the thread pool.</summary>
    DateTime? StartTime(uint processId);

    /// <summary>
    /// The app's name and icon as Windows shows them, for the window's app: its name in Start's Apps
    /// folder for an app ID, otherwise its program's description; on the thread pool.
    /// </summary>
    (string Name, AppIcon? Icon) AppNameAndIcon(nint window, uint processId);
}

/// <summary>A process End App holds open from the user's choice until it ends; see <see cref="HeldProcess"/>.</summary>
public interface IEndAppProcess : IDisposable
{
    uint Id { get; }

    DateTime? StartTime { get; }

    string? ProgramPath { get; }

    bool? IsCritical { get; }

    bool HasExited { get; }

    bool EndProcess(out int errorCode);

    bool WaitForExit(TimeSpan timeout);
}

/// <summary>A visible top-level window, as End App reads it to find the one under the pointer.</summary>
/// <param name="Bounds">Its rectangle with its invisible borders, as Windows' stand-ins copy it.</param>
/// <param name="VisibleBounds">The part of it that shows (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>).</param>
/// <param name="IsClickThrough">Whether it lets clicks through (<c>WS_EX_LAYERED</c> with <c>WS_EX_TRANSPARENT</c>).</param>
/// <param name="IsStandIn">Whether it's Windows' stand-in for a window that stopped responding (class <c>Ghost</c>).</param>
public sealed record EndAppWindow(
    nint Handle,
    uint ProcessId,
    Rectangle Bounds,
    Rectangle VisibleBounds,
    bool IsCloaked,
    bool IsClickThrough,
    bool MayHaveTaskbarButton,
    bool IsStandIn);
