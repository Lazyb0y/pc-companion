using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Opens parts of Windows and changes the session. Each method returns false when Windows
/// refused the request. Windows drops shortcuts without an error while an app running as
/// administrator is in front.
/// </summary>
public static class ShellActions
{
    private const nint MonitorOff = 2;

    // Unassigned, so pressing it does nothing.
    private const VIRTUAL_KEY MaskKey = (VIRTUAL_KEY)0xE8;

    private static readonly VIRTUAL_KEY[] Modifiers =
    [
        VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_RCONTROL, VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_RSHIFT,
        VIRTUAL_KEY.VK_LMENU, VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_RWIN,
    ];

    public static bool ShowTaskView() => SendShortcut(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_TAB);

    public static bool ShowDesktop() => SendShortcut(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_D);

    public static bool OpenStart() => SendShortcut(VIRTUAL_KEY.VK_LWIN);

    public static bool OpenSearch() => SendShortcut(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_S);

    /// <summary>
    /// Opens the notification center on Windows 11 and the Action center on Windows 10.
    /// </summary>
    public static bool OpenNotifications() =>
        SendShortcut(VIRTUAL_KEY.VK_LWIN, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? VIRTUAL_KEY.VK_N : VIRTUAL_KEY.VK_A);

    public static bool Lock() => PInvoke.LockWorkStation();

    /// <summary>
    /// Opens a web page in the default browser. Only https addresses are opened: the shell starts
    /// whatever program is registered for other kinds of addresses. A browser started from a
    /// process running as administrator would run as administrator too.
    /// </summary>
    public static bool OpenWebPage(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only https addresses are opened.", nameof(address));
        }

        return OpenAddress(address);
    }

    /// <summary>
    /// Opens a link in the default browser, such as one the user copied. Only http and https
    /// addresses are opened: the shell starts whatever program is registered for other kinds of
    /// addresses. A browser started from a process running as administrator would run as
    /// administrator too.
    /// </summary>
    public static bool OpenLink(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only http and https addresses are opened.", nameof(address));
        }

        return OpenAddress(address);
    }

    /// <summary>
    /// Opens Task Manager as Windows does from the taskbar: through the shell and without
    /// arguments, so Windows gives it admin rights for administrators without asking. It opens on
    /// the page the user chose as its default, and an open Task Manager comes to the front.
    /// </summary>
    public static bool OpenTaskManager()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Opens the taskbar page of Windows Settings.</summary>
    public static bool OpenTaskbarSettings() => OpenSettingsPage("ms-settings:taskbar");

    /// <summary>Opens the Proxy page of Windows Settings.</summary>
    public static bool OpenProxySettings() => OpenSettingsPage("ms-settings:network-proxy");

    /// <summary>Opens the Display page of Windows Settings.</summary>
    public static bool OpenDisplaySettings() => OpenSettingsPage("ms-settings:display");

    /// <summary>Opens the Keyboard page of Windows Settings' accessibility settings, where Toggle Keys is.</summary>
    public static bool OpenKeyboardAccessibilitySettings() => OpenSettingsPage("ms-settings:easeofaccess-keyboard");

    /// <summary>Opens the Language &amp; region page of Windows Settings, where the user adds languages and keyboards.</summary>
    public static bool OpenLanguageSettings() => OpenSettingsPage("ms-settings:regionlanguage");

    /// <summary>
    /// Opens the Typing page of Windows Settings, whose Advanced keyboard settings has Let me use a
    /// different input method for each app window: on Windows 11 build 26100, the address of
    /// Advanced keyboard settings opened Language &amp; region instead.
    /// </summary>
    public static bool OpenTypingSettings() => OpenSettingsPage("ms-settings:typing");

    /// <summary>Opens the Recycle Bin in File Explorer, where its files can be restored.</summary>
    public static bool OpenRecycleBin()
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { "shell:RecycleBinFolder" } });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns the displays off until the pointer moves or a key is pressed.
    /// </summary>
    /// <param name="window">A window of the calling thread, which passes the request to Windows.</param>
    public static bool TurnOffDisplays(MessageWindow window) => PostSystemCommand(window, PInvoke.SC_MONITORPOWER, MonitorOff);

    /// <summary>
    /// Starts the screen saver chosen in Windows settings, if there is one.
    /// </summary>
    /// <param name="window">A window of the calling thread, which passes the request to Windows.</param>
    public static bool StartScreenSaver(MessageWindow window) => PostSystemCommand(window, PInvoke.SC_SCREENSAVE, 0);

    private static bool PostSystemCommand(MessageWindow window, uint command, nint parameter)
    {
        ArgumentNullException.ThrowIfNull(window);

        return PInvoke.PostMessage((HWND)window.Handle, PInvoke.WM_SYSCOMMAND, command, parameter);
    }

    private static bool OpenAddress(Uri address)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    // A modifier the user is holding, such as the key a hot corner requires, would change the
    // shortcut: Ctrl + Windows + D adds a virtual desktop. Those keys are released first, after a
    // key that does nothing, because releasing the Windows key or Alt on its own opens Start or
    // the menu bar.
    private static bool SendShortcut(params ReadOnlySpan<VIRTUAL_KEY> keys)
    {
        var inputs = new List<INPUT>();

        foreach (var modifier in Modifiers)
        {
            if (InputState.IsDown(modifier))
            {
                if (inputs.Count == 0)
                {
                    inputs.Add(Key(MaskKey, up: false));
                    inputs.Add(Key(MaskKey, up: true));
                }

                inputs.Add(Key(modifier, up: true));
            }
        }

        foreach (var key in keys)
        {
            inputs.Add(Key(key, up: false));
        }

        for (var index = keys.Length - 1; index >= 0; index--)
        {
            inputs.Add(Key(keys[index], up: true));
        }

        return PInvoke.SendInput(CollectionsMarshal.AsSpan(inputs), Marshal.SizeOf<INPUT>()) == inputs.Count;
    }

    private static INPUT Key(VIRTUAL_KEY key, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (key is VIRTUAL_KEY.VK_RCONTROL or VIRTUAL_KEY.VK_RMENU or VIRTUAL_KEY.VK_LWIN or VIRTUAL_KEY.VK_RWIN)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union { ki = new KEYBDINPUT { wVk = key, dwFlags = flags } },
        };
    }

    private static bool OpenSettingsPage(string address)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
