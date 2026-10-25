using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// What <see cref="WindowSwitcher"/> reads from Windows and changes there, so tests can supply it
/// instead; it reads what tells apps apart as Hot Corners does.
/// </summary>
public interface IAppWindowsEnvironment : IAppIdentityReader
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>The visible top-level windows, front to back.</summary>
    IReadOnlyList<TopLevelWindow> ListWindows();

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>Brings the window to the front, restoring it when it's minimized. Returns false when Windows refused.</summary>
    bool Activate(nint window);

    /// <summary>Puts the window behind all other windows. Returns false when Windows refused.</summary>
    bool SendToBack(nint window);
}
