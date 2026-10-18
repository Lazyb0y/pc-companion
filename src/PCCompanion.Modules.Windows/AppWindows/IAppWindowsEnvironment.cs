using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// What <see cref="WindowSwitcher"/> reads from Windows and changes there, so tests can supply it instead.
/// </summary>
public interface IAppWindowsEnvironment
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>The visible top-level windows, front to back.</summary>
    IReadOnlyList<TopLevelWindow> ListWindows();

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>The app ID the window sets for itself, or <see langword="null"/>.</summary>
    string? WindowAppId(nint window);

    /// <summary>The app ID of the process's Store app package, or <see langword="null"/>.</summary>
    string? PackageAppId(uint processId);

    /// <summary>The full path of the process's program file, or <see langword="null"/> when it can't be read.</summary>
    string? ProgramPath(uint processId);

    /// <summary>Brings the window to the front, restoring it when it's minimized. Returns false when Windows refused.</summary>
    bool Activate(nint window);

    /// <summary>Puts the window behind all other windows. Returns false when Windows refused.</summary>
    bool SendToBack(nint window);
}
