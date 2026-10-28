using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The Windows mode, dark or light, which the taskbar and Start use, high contrast, animation
/// effects and the text size. Each call reads the current setting, so a change shows at the
/// caller's next look.
/// </summary>
public static class WindowsTheme
{
    private const int DefaultTextScalePercent = 100;
    private const int MaximumTextScalePercent = 225;
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
    public static uint TextColor() => SystemColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT);

    /// <summary>
    /// The theme's window background color, as 0xRRGGBB, which <see cref="TextColor"/> is drawn
    /// on in high contrast.
    /// </summary>
    public static uint WindowColor() => SystemColor(SYS_COLOR_INDEX.COLOR_WINDOW);

    /// <summary>
    /// Whether Windows' animation effects are on (Accessibility > Visual effects); on when the
    /// setting can't be read.
    /// </summary>
    public static unsafe bool AreAnimationsOn()
    {
        BOOL on = true;
        return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &on, 0) || on;
    }

    /// <summary>
    /// Windows' text size (Accessibility > Text size), which makes text larger without changing
    /// the display's scale: from 1 to 2.25, and 1 when it's not set or out of range.
    /// </summary>
    public static double TextScale()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
        return key?.GetValue("TextScaleFactor") is int percent and >= DefaultTextScalePercent and <= MaximumTextScalePercent
            ? percent / (double)DefaultTextScalePercent
            : 1;
    }

    private static uint SystemColor(SYS_COLOR_INDEX index)
    {
        // A COLORREF is 0x00BBGGRR.
        var color = PInvoke.GetSysColor(index);
        return ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
    }
}
