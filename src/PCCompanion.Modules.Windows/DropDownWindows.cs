using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// The drop-down windows, shown or hidden, which Hide Apps and Minimize to Tray leave alone, as
/// "With Hide Apps and Minimize to Tray" in docs/features/windows/drop-down-apps.md says.
/// Drop-down Apps' thread changes it and the others read it on their threads.
/// </summary>
public sealed class DropDownWindows
{
    // Replaced, never changed, so a reader on another thread always sees a whole set.
    private IReadOnlySet<nint> _windows = new HashSet<nint>();

    /// <summary>Whether <paramref name="window"/> is a drop-down window. Any thread can call it.</summary>
    public bool Contains(nint window) => Volatile.Read(ref _windows).Contains(window);

    /// <summary>Makes the windows Drop-down Apps' windows. Call it on Drop-down Apps' thread only.</summary>
    public void Set(IEnumerable<nint> windows) => Volatile.Write(ref _windows, new HashSet<nint>(windows));
}
