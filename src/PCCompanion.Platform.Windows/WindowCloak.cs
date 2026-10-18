using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Cloaks a window: Windows keeps it out of sight while it stays open and active, and its app
/// keeps drawing it. Unlike hiding it, cloaking doesn't change the window's state.
/// </summary>
public static class WindowCloak
{
    /// <summary>
    /// Cloaks <paramref name="window"/>, one of this process's windows, or shows it again.
    /// Returns false when Windows refused, for example without desktop composition.
    /// </summary>
    public static unsafe bool Set(nint window, bool cloaked)
    {
        BOOL value = cloaked;
        return PInvoke.DwmSetWindowAttribute(new HWND(window), DWMWINDOWATTRIBUTE.DWMWA_CLOAK, &value, (uint)sizeof(BOOL)).Succeeded;
    }
}
