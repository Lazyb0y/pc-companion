using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// The windows that Window Shading rolled up, which Minimize to Tray leaves on the taskbar, as
/// "With the other features" in docs/features/windows/window-shading.md says. Window Shading's
/// thread changes it and Minimize to Tray's thread reads it.
/// </summary>
public sealed class ShadedWindows
{
    // Replaced, never changed, so a reader on another thread always sees a whole set.
    private IReadOnlySet<nint> _windows = new HashSet<nint>();

    /// <summary>Whether Window Shading rolled <paramref name="window"/> up. Any thread can call it.</summary>
    public bool Contains(nint window) => Volatile.Read(ref _windows).Contains(window);

    /// <summary>
    /// Adds a window Window Shading is about to minimize, so that it's there before Windows reports
    /// the minimize. Call it on Window Shading's thread only.
    /// </summary>
    public void Add(nint window) => Volatile.Write(ref _windows, new HashSet<nint>(_windows) { window });

    /// <summary>Takes a window out. Call it on Window Shading's thread only.</summary>
    public void Remove(nint window)
    {
        var windows = new HashSet<nint>(_windows);
        if (windows.Remove(window))
        {
            Volatile.Write(ref _windows, windows);
        }
    }
}
