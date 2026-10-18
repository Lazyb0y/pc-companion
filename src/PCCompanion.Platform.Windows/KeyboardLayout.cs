using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The user's keyboard layouts, and which virtual key a key on the keyboard means in one. Windows
/// keeps a layout for each thread; with the default settings, switching layouts switches the
/// layout of every app. Windows reports no switch to other programs.
/// </summary>
public static class KeyboardLayout
{
    /// <summary>
    /// The layout of the window in front, read from the window with the keyboard focus, which for
    /// Store apps belongs to another process than their frame. Returns <see langword="null"/> when
    /// no window is in front or its layout can't be read, as for console windows.
    /// </summary>
    public static unsafe nint? OfForegroundWindow()
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        var window = PInvoke.GetGUIThreadInfo(0, ref info) && !info.hwndFocus.IsNull ? info.hwndFocus : PInvoke.GetForegroundWindow();
        var thread = window.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(window, null);
        nint layout = thread != 0 ? PInvoke.GetKeyboardLayout(thread) : 0;
        return layout != 0 ? layout : null;
    }

    /// <summary>The layouts the user has, in the order of the input switcher.</summary>
    public static IReadOnlyList<nint> Installed()
    {
        var layouts = new HKL[PInvoke.GetKeyboardLayoutList(Span<HKL>.Empty)];
        var count = PInvoke.GetKeyboardLayoutList(layouts);

        var result = new List<nint>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(layouts[i]);
        }

        return result;
    }

    /// <summary>
    /// The virtual key that the key with <paramref name="scanCode"/> means in
    /// <paramref name="layout"/>, or <see langword="null"/> when it means none there.
    /// </summary>
    public static uint? VirtualKeyOf(uint scanCode, nint layout)
    {
        var key = PInvoke.MapVirtualKeyEx(scanCode, MAP_VIRTUAL_KEY_TYPE.MAPVK_VSC_TO_VK, (HKL)layout);
        return key != 0 ? key : null;
    }
}
