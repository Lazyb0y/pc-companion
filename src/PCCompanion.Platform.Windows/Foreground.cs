using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Which window is in the foreground.
/// </summary>
public static class Foreground
{
    /// <summary>
    /// Lets another process bring its window to the foreground, for example a running instance
    /// that this process hands its launch to. Returns false when this process may not pass that on.
    /// </summary>
    public static bool AllowProcess(uint processId) => PInvoke.AllowSetForegroundWindow(processId);
}
