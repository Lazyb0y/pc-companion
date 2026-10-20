using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>How this process's shortcut menus are drawn.</summary>
public enum MenuMode
{
    /// <summary>As Windows draws them by default: light, or in the high contrast colors.</summary>
    Default,
    Light,
    Dark,
}

/// <summary>
/// Draws this process's shortcut menus dark or light. Windows has no documented way to do this,
/// so it calls two undocumented uxtheme.dll functions, and only on the Windows builds they were
/// tested on. Elsewhere, menus stay as Windows draws them.
/// </summary>
public static class MenuTheme
{
    /// <summary>The newest Windows build the menus were tested on. Raise it after testing a new release.</summary>
    public const int MaxTestedBuild = 26300;

    // Windows 10 version 2004, the oldest supported build.
    private const int MinBuild = 19041;

    // uxtheme.dll exports them by ordinal only, and has since Windows 10 version 1903.
    private const int SetPreferredAppModeOrdinal = 135;
    private const int FlushMenuThemesOrdinal = 136;

    // SetPreferredAppMode's modes.
    private const int DefaultAppMode = 0;
    private const int ForceDarkAppMode = 2;
    private const int ForceLightAppMode = 3;

    private static readonly Lazy<Functions?> UxTheme = new(LoadFunctions);

    /// <summary>Whether menus can be drawn dark or light on this Windows build.</summary>
    public static bool IsSupported => IsSupportedBuild(Environment.OSVersion.Version.Build);

    public static bool IsSupportedBuild(int build) => build is >= MinBuild and <= MaxTestedBuild;

    /// <summary>
    /// The mode that matches the Windows mode, which the taskbar and Start use. In high contrast,
    /// it's <see cref="MenuMode.Default"/>, which keeps the high contrast colors.
    /// </summary>
    public static MenuMode ForWindowsMode() => ForWindowsMode(WindowsTheme.IsHighContrast(), WindowsTheme.IsDark());

    public static MenuMode ForWindowsMode(bool highContrast, bool windowsModeDark) =>
        highContrast ? MenuMode.Default : windowsModeDark ? MenuMode.Dark : MenuMode.Light;

    /// <summary>
    /// Draws the menus this process shows from now on in <paramref name="mode"/>. Returns false on
    /// builds where that isn't supported, or when uxtheme.dll doesn't have the functions.
    /// </summary>
    public static unsafe bool Set(MenuMode mode)
    {
        if (!IsSupported)
        {
            return false;
        }

        var functions = UxTheme.Value;
        if (functions is null)
        {
            return false;
        }

        var appMode = mode switch
        {
            MenuMode.Dark => ForceDarkAppMode,
            MenuMode.Light => ForceLightAppMode,
            _ => DefaultAppMode,
        };

        // Menus keep the theme they were first drawn with; the flush makes the next one use the new mode.
        ((delegate* unmanaged<int, int>)functions.SetPreferredAppMode)(appMode);
        ((delegate* unmanaged<void>)functions.FlushMenuThemes)();
        return true;
    }

    private static unsafe Functions? LoadFunctions()
    {
        // Only from System32; it stays loaded while the process runs.
        if (!NativeLibrary.TryLoad("uxtheme.dll", typeof(MenuTheme).Assembly, DllImportSearchPath.System32, out var handle))
        {
            return null;
        }

        var module = new HMODULE(handle);
        nint Load(int ordinal) => PInvoke.GetProcAddress(module, new PCSTR((byte*)ordinal)).Value;

        var functions = new Functions(Load(SetPreferredAppModeOrdinal), Load(FlushMenuThemesOrdinal));
        return functions.SetPreferredAppMode == 0 || functions.FlushMenuThemes == 0 ? null : functions;
    }

    private sealed record Functions(nint SetPreferredAppMode, nint FlushMenuThemes);
}
