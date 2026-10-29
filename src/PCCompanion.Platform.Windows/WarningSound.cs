using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Windows' warning sound, as Windows plays it when it refuses an action. It plays the sound the
/// user chose for it in Windows' sound settings, or none.
/// </summary>
public static class WarningSound
{
    /// <summary>Plays the sound without waiting for it. Returns false when Windows couldn't.</summary>
    public static bool Play() => PInvoke.MessageBeep(MESSAGEBOX_STYLE.MB_ICONEXCLAMATION);
}
