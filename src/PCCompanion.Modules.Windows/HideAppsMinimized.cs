using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// The windows that Hide Apps minimized and can bring back, which Minimize to Tray leaves on the
/// taskbar, as "With Minimize to Tray" in docs/features/windows/hide-apps.md says. Hide Apps'
/// thread changes it and Minimize to Tray's thread reads it.
/// </summary>
public sealed class HideAppsMinimized
{
    // Replaced, never changed, so a reader on another thread always sees a whole set.
    private IReadOnlySet<nint> _windows = new HashSet<nint>();

    /// <summary>Whether Hide Apps minimized <paramref name="window"/> and can bring it back. Any thread can call it.</summary>
    public bool Contains(nint window) => Volatile.Read(ref _windows).Contains(window);

    /// <summary>
    /// Adds a window Hide Apps is about to minimize, so that it's there before Windows reports the
    /// minimize. Call it on Hide Apps' thread only.
    /// </summary>
    public void Add(nint window) => Volatile.Write(ref _windows, new HashSet<nint>(_windows) { window });

    /// <summary>Makes the windows those in Hide Apps' list. Call it on Hide Apps' thread only.</summary>
    public void Set(IEnumerable<nint> windows) => Volatile.Write(ref _windows, new HashSet<nint>(windows));
}
