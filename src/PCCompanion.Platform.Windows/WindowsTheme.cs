using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The Windows mode, dark or light, which the taskbar and Start use, and high contrast. Each call
/// reads the current setting, so a change shows at the caller's next look.
/// </summary>
public static class WindowsTheme
{
    /// <summary>Whether the Windows mode is dark.</summary>
    public static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is 0;
    }

    public static unsafe bool IsHighContrast()
    {
        var highContrast = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, highContrast.cbSize, &highContrast, 0)
            && (highContrast.dwFlags & HIGHCONTRASTW_FLAGS.HCF_HIGHCONTRASTON) != 0;
    }

    /// <summary>
    /// The theme's text color, as 0xRRGGBB, which the taskbar's own icons use in high contrast.
    /// </summary>
    public static uint TextColor()
    {
        // A COLORREF is 0x00BBGGRR.
        var color = PInvoke.GetSysColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT);
        return ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
    }
}
