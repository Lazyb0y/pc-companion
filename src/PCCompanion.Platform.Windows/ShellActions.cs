using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Opens parts of Windows and changes the session. Each method returns false when Windows
/// refused the request. Windows drops shortcuts without an error while an app running as
/// administrator is in front.
/// </summary>
public static class ShellActions
{
    private const nint MonitorOff = 2;

    private const string AppsFolder = @"shell:AppsFolder\";

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
    /// Switches to the virtual desktop on the right, or on the left, with Ctrl + Windows + Right or
    /// Left: Windows has no documented API for it. Past the last or first desktop, nothing happens.
    /// </summary>
    /// <param name="keepHeldKeys">
    /// Whether keys the user is holding, which are released for the shortcut, are pressed again
    /// afterwards, as for a wheel turned with a key held, so the next notch still finds the key.
    /// </param>
    public static bool SwitchDesktop(bool next, bool keepHeldKeys = false) =>
        SendShortcut(keepHeldKeys, VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_LWIN, next ? VIRTUAL_KEY.VK_RIGHT : VIRTUAL_KEY.VK_LEFT);

    /// <summary>
    /// Makes the volume louder or quieter by one step, 2%, with the Volume Up or Volume Down key,
    /// which shows Windows' volume display; the audio API changes the volume without it.
    /// </summary>
    /// <param name="keepHeldKeys">As for <see cref="SwitchDesktop"/>.</param>
    public static bool ChangeVolume(bool louder, bool keepHeldKeys = false) =>
        SendShortcut(keepHeldKeys, louder ? VIRTUAL_KEY.VK_VOLUME_UP : VIRTUAL_KEY.VK_VOLUME_DOWN);

    /// <summary>
    /// Goes to the next or previous track with the Next Track or Previous Track key, which Windows
    /// passes to the app playing media.
    /// </summary>
    /// <param name="keepHeldKeys">As for <see cref="SwitchDesktop"/>.</param>
    public static bool SwitchTrack(bool next, bool keepHeldKeys = false) =>
        SendShortcut(keepHeldKeys, next ? VIRTUAL_KEY.VK_MEDIA_NEXT_TRACK : VIRTUAL_KEY.VK_MEDIA_PREV_TRACK);

    /// <summary>
    /// Presses and releases a key that does nothing, so that releasing a held Windows key or Alt
    /// opens neither Start nor a menu bar. Windows also lets the process that sent it bring a
    /// window to the front afterwards, which isn't documented.
    /// </summary>
    public static bool PressMaskKey()
    {
        INPUT[] inputs = [Key(MaskKey, up: false), Key(MaskKey, up: true)];
        return PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

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
    /// Whether Start's Apps folder has an app with the app ID <paramref name="appId"/>
    /// (<c>shell:AppsFolder\</c> and the app ID). Windows opens File Explorer, without an error,
    /// for an app ID that isn't there.
    /// </summary>
    public static unsafe bool IsInAppsFolder(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);

        var iid = typeof(IShellItem).GUID;
        fixed (char* name = AppsFolder + appId)
        {
            return PInvoke.SHCreateItemFromParsingName(name, null, &iid, out var item).Succeeded && item is not null;
        }
    }

    /// <summary>
    /// Opens the app with the app ID <paramref name="appId"/> through Start's Apps folder, as Start
    /// does, with this process's rights, without waiting for the app. Check with
    /// <see cref="IsInAppsFolder"/> first. Returns 0, or Windows' error code when File Explorer,
    /// which opens it, didn't start.
    /// </summary>
    public static int OpenApp(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);

        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { AppsFolder + appId } });
            return 0;
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode;
        }
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

    /// <summary>Opens AutoPlay in Settings, where removable drives and memory cards can be set to take no action.</summary>
    public static bool OpenAutoPlaySettings() => OpenSettingsPage("ms-settings:autoplay");

    /// <summary>Opens the Background page of Windows Settings, where Windows spotlight is chosen.</summary>
    public static bool OpenBackgroundSettings() => OpenSettingsPage("ms-settings:personalization-background");

    /// <summary>Opens the Location page of Windows Settings, where desktop apps are let use the location.</summary>
    public static bool OpenLocationSettings() => OpenSettingsPage("ms-settings:privacy-location");

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

    private static bool SendShortcut(params ReadOnlySpan<VIRTUAL_KEY> keys) => SendShortcut(keepHeldKeys: false, keys);

    // A modifier the user is holding, such as the key a hot corner requires, would change the
    // shortcut: Ctrl + Windows + D adds a virtual desktop. Those keys are released first, after a
    // key that does nothing, because releasing the Windows key or Alt on its own opens Start or
    // the menu bar. Kept, they're pressed again after the shortcut, followed by the key that does
    // nothing, so the user releasing them opens neither.
    private static bool SendShortcut(bool keepHeldKeys, params ReadOnlySpan<VIRTUAL_KEY> keys)
    {
        var inputs = new List<INPUT>();
        var released = new List<VIRTUAL_KEY>();

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
                released.Add(modifier);
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

        if (keepHeldKeys && released.Count > 0)
        {
            foreach (var modifier in released)
            {
                inputs.Add(Key(modifier, up: false));
            }

            inputs.Add(Key(MaskKey, up: false));
            inputs.Add(Key(MaskKey, up: true));
        }

        return PInvoke.SendInput(CollectionsMarshal.AsSpan(inputs), Marshal.SizeOf<INPUT>()) == inputs.Count;
    }

    private static INPUT Key(VIRTUAL_KEY key, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (key is VIRTUAL_KEY.VK_RCONTROL or VIRTUAL_KEY.VK_RMENU or VIRTUAL_KEY.VK_LWIN or VIRTUAL_KEY.VK_RWIN
            or VIRTUAL_KEY.VK_LEFT or VIRTUAL_KEY.VK_RIGHT
            or VIRTUAL_KEY.VK_VOLUME_UP or VIRTUAL_KEY.VK_VOLUME_DOWN or VIRTUAL_KEY.VK_MEDIA_NEXT_TRACK or VIRTUAL_KEY.VK_MEDIA_PREV_TRACK)
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
