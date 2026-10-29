using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// This process's place in the order in which Windows asks programs to end when the user signs out,
/// restarts or shuts down.
/// </summary>
public static class ShutdownOrder
{
    // The highest level apps may use: Windows asks processes from the highest level down.
    private const uint FirstForApps = 0x3FF;

    /// <summary>
    /// Asks Windows to ask this process before other apps, for example to show other apps' hidden
    /// windows again before those apps are asked to close. Returns false when Windows refused.
    /// </summary>
    public static bool AskFirst() => PInvoke.SetProcessShutdownParameters(FirstForApps, 0);
}
