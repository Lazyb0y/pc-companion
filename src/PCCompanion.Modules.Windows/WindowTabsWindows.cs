using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// The windows that are tabs, shown or hidden, which Minimize to Tray and Drop-down Apps leave
/// alone, as "With the other features" in docs/features/windows/window-tabs.md says. Window Tabs'
/// thread changes it and the others read it on their threads.
/// </summary>
public sealed class WindowTabsWindows
{
    // Replaced, never changed, so a reader on another thread always sees a whole set.
    private IReadOnlySet<nint> _windows = new HashSet<nint>();

    /// <summary>Whether <paramref name="window"/> is a tab. Any thread can call it.</summary>
    public bool Contains(nint window) => Volatile.Read(ref _windows).Contains(window);

    /// <summary>Makes the windows the tabs. Call it on Window Tabs' thread only.</summary>
    public void Set(IEnumerable<nint> windows) => Volatile.Write(ref _windows, new HashSet<nint>(windows));
}
