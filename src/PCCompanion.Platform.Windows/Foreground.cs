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
    /// Whether the app in front is full screen, such as a game, a video or a presentation.
    /// </summary>
    public static bool IsFullScreen() =>
        PInvoke.SHQueryUserNotificationState(out var state).Succeeded
        && state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
            or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;

    /// <summary>
    /// Lets another process bring its window to the foreground, for example a running instance
    /// that this process hands its launch to. Returns false when this process may not pass that on.
    /// </summary>
    public static bool AllowProcess(uint processId) => PInvoke.AllowSetForegroundWindow(processId);

    /// <summary>
    /// Brings <paramref name="window"/>, one of this process's windows, in front of other apps'
    /// windows. Returns false when this process may not, for example while another app is in use
    /// and nothing let this process come to the front.
    /// </summary>
    public static bool Bring(nint window) => PInvoke.SetForegroundWindow(new HWND(window));
}
