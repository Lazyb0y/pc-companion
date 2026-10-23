using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Which window is in the foreground, and bringing one there.
/// </summary>
public static class Foreground
{
    /// <summary>The window in front, or 0 when there is none, for example while windows are switching.</summary>
    public static nint Window => PInvoke.GetForegroundWindow();

    /// <summary>
    /// What Windows reports about full-screen apps. Its busy state isn't used: it doesn't say which
    /// display the full-screen app is on.
    /// </summary>
    public static FullScreenMode FullScreenMode =>
        !PInvoke.SHQueryUserNotificationState(out var state).Succeeded ? FullScreenMode.None
        : state switch
        {
            QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => FullScreenMode.Exclusive,
            QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => FullScreenMode.Presentation,
            _ => FullScreenMode.None,
        };

    /// <summary>
    /// Lets another process bring its window to the foreground, for example a running instance
    /// that this process hands its launch to. Returns false when this process may not pass that on.
    /// </summary>
    public static bool AllowProcess(uint processId) => PInvoke.AllowSetForegroundWindow(processId);

    /// <summary>
    /// Lets the next process that tries bring its window to the foreground, such as a browser or a
    /// program this process is about to start through another process. Returns false when this
    /// process may not pass that on.
    /// </summary>
    public static bool AllowAnyProcess() => PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);

    /// <summary>
    /// Brings <paramref name="window"/>, one of this process's windows, in front of other apps'
    /// windows. Returns false when this process may not, for example while another app is in use
    /// and nothing let this process come to the front.
    /// </summary>
    public static bool Bring(nint window) => PInvoke.SetForegroundWindow(new HWND(window));
}

/// <summary>
/// What Windows reports about full-screen apps.
/// </summary>
public enum FullScreenMode
{
    None,

    /// <summary>
    /// An app, usually a game, has its display to itself (exclusive full screen). It's the app in front.
    /// </summary>
    Exclusive,

    /// <summary>
    /// Presentation mode is on, as set in Windows Mobility Center.
    /// </summary>
    Presentation,
}
