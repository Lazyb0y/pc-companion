using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
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
    public static nint? OfForegroundWindow() => FocusInFront() is { } focus ? OfThread(focus.Thread) : null;

    /// <summary>
    /// The window that typing goes to: the window with the keyboard focus in the thread in front,
    /// which for Store apps belongs to another process than their frame, otherwise the window in
    /// front. Returns <see langword="null"/> when no window is in front. Sends no message to the app.
    /// </summary>
    public static unsafe InputFocus? FocusInFront()
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        var window = PInvoke.GetGUIThreadInfo(0, ref info) && !info.hwndFocus.IsNull ? info.hwndFocus : PInvoke.GetForegroundWindow();
        uint processId = 0;
        var thread = window.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(window, &processId);
        return thread != 0 ? new InputFocus(window, thread, processId) : null;
    }

    /// <summary>
    /// The layout of <paramref name="thread"/>, or <see langword="null"/> when it has none, as a
    /// console program's thread, whose console window's layout is on the console host's thread.
    /// </summary>
    public static nint? OfThread(uint thread)
    {
        nint layout = thread != 0 ? PInvoke.GetKeyboardLayout(thread) : 0;
        return layout != 0 ? layout : null;
    }

    /// <summary>The layout of the calling thread, which for a window in front is the layout the user types with.</summary>
    public static nint OfCurrentThread() => PInvoke.GetKeyboardLayout(0);

    /// <summary>
    /// Asks <paramref name="window"/>'s app to switch to <paramref name="layout"/>, one of the
    /// user's layouts (<see cref="Installed"/>), with the request Windows itself posts when the user
    /// switches (<c>WM_INPUTLANGCHANGEREQUEST</c>). It returns at once; the app switches when it
    /// handles the request, before the keys typed after it.
    /// </summary>
    public static LayoutRequest RequestSwitch(nint window, nint layout)
    {
        if (PInvoke.PostMessage(new HWND(window), PInvoke.WM_INPUTLANGCHANGEREQUEST, 0, layout))
        {
            return LayoutRequest.Posted;
        }

        return Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_ACCESS_DENIED ? LayoutRequest.Refused : LayoutRequest.Failed;
    }

    /// <summary>
    /// Whether Windows' <b>Let me use a different input method for each app window</b> is on, so a
    /// switch applies to one app window rather than to every app.
    /// </summary>
    public static unsafe bool IsPerWindow()
    {
        BOOL on = false;
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETTHREADLOCALINPUTSETTINGS, 0, &on, 0) && on;
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

    /// <summary>
    /// The character that the key with <paramref name="scanCode"/> types in <paramref name="layout"/>
    /// without Shift, such as <c>`</c> for the key above Tab in US English and <c>^</c> in German,
    /// or <see langword="null"/> when it types none there.
    /// </summary>
    public static char? CharacterOf(uint scanCode, nint layout)
    {
        if (VirtualKeyOf(scanCode, layout) is not { } key)
        {
            return null;
        }

        // The top bit marks a dead key, such as the German ^.
        var character = PInvoke.MapVirtualKeyEx(key, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_CHAR, (HKL)layout) & 0x7FFFFFFF;
        return character != 0 ? (char)character : null;
    }
}

/// <summary>The window that typing goes to, as <see cref="KeyboardLayout.FocusInFront"/> read it.</summary>
/// <param name="Window">The window with the keyboard focus, or the window in front when none has it.</param>
/// <param name="Thread">The window's thread, whose layout is the one the user types with.</param>
/// <param name="ProcessId">The window's process; for a console window, the console program's.</param>
public readonly record struct InputFocus(nint Window, uint Thread, uint ProcessId);

/// <summary>What became of a request to switch an app's layout.</summary>
public enum LayoutRequest
{
    /// <summary>The request is on its way to the app, which switches when it handles it.</summary>
    Posted,

    /// <summary>
    /// Windows refused it, as it does for the windows of apps running as administrator when this
    /// program runs without admin rights.
    /// </summary>
    Refused,

    /// <summary>The request couldn't be posted, for example because the window closed.</summary>
    Failed,
}
