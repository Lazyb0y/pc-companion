using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Which window is in the foreground.
/// </summary>
public static class Foreground
{
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
}
