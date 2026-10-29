# Minimize to Tray

Send any app's window to an icon in the notification area instead of the taskbar, and bring it back from there.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.minimizeToTray`. Issue: [#32](https://github.com/Lazyb0y/pc-companion/issues/32).
- Inspired by tools for Linux desktops that dock any window in the tray, and by Hide in macOS.
- Off until the user turns it on.

## Behavior

- **Windows + Shift + Z** sends the window in front to the tray: it leaves the taskbar and Alt + Tab, and its app gets an icon in the notification area.
- **Shift + Minimize** does the same: holding Shift while clicking a window's Minimize button sends it to the tray. Without Shift, Minimize works as always.
- **Listed apps** go to the tray whenever one of their windows is minimized; see [Apps that always go to the tray](#apps-that-always-go-to-the-tray).
- **Selecting an app's icon** brings back the window of that app that went to the tray last: where it was, at its size, maximized if it was, and in front. **Right-clicking it** lists the app's windows in the tray, to bring back one or all.
- The PC Companion icon's menu lists every window in the tray too, so they're reachable while their icons sit in the hidden icons menu.
- No window is ever lost: when PC Companion exits, for any reason, or the user signs out, every window in the tray comes back on the taskbar, minimized; see [Never losing a window](#never-losing-a-window).
- Some windows can't go to the tray, such as those of apps running as administrator while PC Companion doesn't; Windows' warning sound says so, and the page explains why; see [Windows that can't go to the tray](#windows-that-cant-go-to-the-tray).

## Compared with other systems

- **Windows** has no way to send another app's window to the tray. Microsoft's notification area guidelines recommend against minimizing to it, and ask apps that do to let users choose it and to use Minimize, never Close. Minimize to Tray is off until turned on, works only when the user asks, with a shortcut, Shift + Minimize or a listed app, and never changes what Close does.
- **macOS** hides a whole app, with no way to hide one window, and minimized windows are hard to find again: Command + Tab doesn't bring them back. Minimize to Tray works per window, and every window in the tray is listed in its app's icon and in the PC Companion icon's menu.
- **KDE Plasma and GNOME** have no way to send a window to the tray; KDE Plasma's window rules can only keep a window off the taskbar. Tray tools for Linux desktops mostly work only on X11, leave the windows they hid hidden when they crash, can bring a maximized window back small, and can show every docked window under the tool's own name in the tray's settings. Minimize to Tray brings windows back after a crash, restores them as they were, and names each icon after its app.
- **Tray tools for Windows** that catch a right-click on Minimize or a click on Close load code into every app: users report anti-cheat software freezing games, antivirus warnings, slow file dialogs and separate 32-bit and 64-bit builds, and tools that guess where the buttons are break on apps that draw their own title bars. Minimize to Tray runs no code in other apps and uses no keyboard or mouse hook. Shift + Minimize reacts to the window starting to minimize, not to where the click was, so it doesn't depend on how an app draws its title bar.
- **Tools that hide windows with a shortcut** lose them when they crash, or save the windows' handles to a file that's wrong after a restart. Minimize to Tray marks the windows themselves, so a restarted PC Companion finds exactly the windows it hid, and a restart of Windows leaves nothing behind.
- **One icon per window,** as most tools add, fills the hidden icons menu, and Windows 11 keeps an entry in the taskbar settings for every icon it has seen, also after the icon is gone. Minimize to Tray adds one icon per app, with an ID that stays the same, so an icon the user moves onto the taskbar keeps its place every time that app goes to the tray.
- **A mail app** that offered minimizing to the tray dropped it because Show desktop (Windows + D) sent it to the tray by accident. Minimize to Tray ignores Show desktop and the other shortcuts that minimize every window.
- **Tray tools for Windows fail silently** on apps running as administrator and on Store apps, whose windows keep their taskbar buttons. Minimize to Tray plays Windows' warning sound, and its page says which windows can't go and why.
- **Window titles name documents.** Windows keeps the first tooltip of each notification icon in the registry, so Minimize to Tray adds each icon with the app's name, and shows titles only in tooltips and menus, which Windows doesn't keep.

## Which windows can go to the tray

- The windows that the taskbar shows: visible top-level windows that aren't tool windows and aren't owned by another window, unless they ask to be on the taskbar (`TopLevelWindows.MayHaveTaskbarButton`).
- The shortcut acts on the window in front, or on the window that owns it (`TopLevelWindows.RootOwner`), such as a palette's main window. Owned windows, such as palettes, go to the tray with their main window and come back with it.
- File Explorer windows can go to the tray; the desktop, the taskbar and the shell's other windows, such as Start and Task View, can't.
- PC Companion's settings window can go to the tray like any other window.

## Windows that can't go to the tray

- **Apps running as administrator,** while PC Companion runs without admin rights: Windows refuses to hide their windows, and to mark them, for programs without admin rights. With Always run as administrator, or with UAC off, they go to the tray like any other.
- **Store apps in Windows' shared app frame** (`ApplicationFrameWindow`, in `ApplicationFrameHost.exe`), such as Settings: Windows keeps their taskbar button and Alt + Tab entry, and shows the window again within seconds. Store apps that run as desktop apps, such as most new apps, have ordinary windows and can go to the tray.
- **A window with a dialog open,** such as a Save dialog, recognized by the main window being disabled (`IsWindowEnabled`): some apps cancel a dialog when its window is minimized, as the spike showed for Windows Forms apps, and an app can't be told apart in advance.
- **Windows that aren't on the taskbar,** such as the desktop, or no window in front.
- When the shortcut can't send the window in front, or Shift + Minimize can't send the window, Windows' warning sound plays (`MessageBeep` with `MB_ICONEXCLAMATION`). With Shift + Minimize, the window still minimizes to the taskbar, as without Shift. The one exception is an app running as administrator while PC Companion doesn't: see [Apps running as administrator](#apps-running-as-administrator). A listed app's window that can't go stays minimized on the taskbar, with no sound, since the user only minimized it.
- The feature page lists what can't go to the tray and why. Its note about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.
- Each case is logged at Debug level with its reason, never with the app.

## Sending a window

### The shortcut

- **Windows + Shift + Z,** registered with `RegisterHotKey` and `MOD_NOREPEAT`, so Windows tells PC Companion only when the shortcut is pressed. No keyboard hook. Windows 11's list of keyboard shortcuts doesn't include it, and several tray tools for Windows use it too.
- **Shortcut** on the page offers Windows + Shift + Z or None.
- When another program already has the shortcut, Windows refuses it and a warning is logged. PC Companion tries again after any change to the settings.

### Shift + Minimize

- **How:** an out-of-context WinEvent hook for `EVENT_SYSTEM_MINIMIZESTART`, which Windows raises when any window starts to minimize, without code in other apps. When Shift is held at that moment (`GetAsyncKeyState`), and no Windows key, the window goes to the tray.
- It reacts to the minimize itself, not to where the click was, so it doesn't depend on where an app draws its Minimize button, as apps with their own title bars do. Shift + clicking a taskbar button opens another window of the app instead, so it never minimizes.
- The spike measured the event 15 to 48 ms after the mouse button came up: Shift let go at the same moment was missed, and 20 ms later was seen. Shift is normally let go after the click, so this works when the user clicks with Shift held.
- **Shift + Minimize** on the page, on by default when the feature is first turned on.
- While it's off and no app is listed, the hook isn't set.

### Apps that always go to the tray

- **Apps that go to the tray when minimized,** a list on the page: when a window of a listed app is minimized, it goes to the tray, as apps' own "minimize to tray" options do.
- **What counts as one app,** as the taskbar groups them, with the same code as App Windows and Hot Corners (`AppOfWindow`): the app ID the window sets for itself, as browsers do for each profile and installed web app; otherwise its Store app package's app ID; otherwise its program file. An app ID matches whichever way it was read, and a program matches by its full path, ignoring case.
- The list is added to as Hot Corners' list of apps is: **Running app** lists the apps with windows on the taskbar, and **Program file** picks a program with the Open dialog. **Remove** takes one out.
- An app that moves its program file when it updates, as some apps installed in a folder named after their version do, must be added again.

### Show desktop and minimize all

- Show desktop (Windows + D), Windows + M, Windows + Home and shaking a window, where it's turned on, minimize many windows at once. Sending them all to the tray would take every window off the taskbar, and Show desktop pressed again couldn't bring them back.
- So a minimize doesn't send a listed app's window while the Windows key is held, or when another window starts to minimize within 100 ms of it: the spike measured Windows + D, Windows + M and Windows + Home starting every window's minimize within 31 ms, with the Windows key still held when the events came.
- The window then minimizes to the taskbar as usual. Windows + Down, which also holds the Windows key, doesn't send a listed app's window either; the shortcut does.
- To tell the two apart, the decision waits 100 ms after the window starts to minimize, which the minimize animation covers, and the window goes only when it's still minimized then.

## Tray icons

### One icon per app

- Each app with windows in the tray gets one icon, as the taskbar gives each app one button. It goes when the app's last window leaves the tray.
- **ID:** derived from the app's identity, the app ID or the program's path, so Windows, which remembers by program path and ID where the user put each icon, keeps an icon the user moved onto the taskbar there every time that app goes to the tray, also after a restart and an update. IDs have 16 bits, as Windows reports an icon's ID to its window in 16 bits, and start at 256, clear of the PC Companion icon's (1) and System Meters' (2 to 4); when two apps would get the same ID, which is rare, the second gets the next free one, for that session.
- An app without an app ID or program path gets an ID for the session only.
- **The app's name,** for the icon's name and menus: the name Windows shows for its app ID in Start (the Apps folder), otherwise its program file's description, otherwise the program's file name. It's read once per app, on the thread pool. Until it's read, the app's first window waits on the taskbar, minimized, since its icon is added with the name. A window that closes meanwhile stays out of the tray, as does one that was minimizing with Shift + Minimize or as a listed app's and that the user shows again. The shortcut's window goes even when it isn't minimized yet, since its app may not have handled the minimize, as one that stopped responding hasn't.

### Picture

- The window's own icon, as the taskbar shows it, drawn at the notification area's size for the main display's scale (16 pixels at 100%, 24 at 150%): the large icon when the small one would be scaled up.
- **Read without waiting for the app:** `WM_GETICON` sent with `SendMessageCallback`, which returns at once and passes the answer back when the app handles it; until then, the window class's icon (`GetClassLongPtr`); otherwise the program file's icon, read on the thread pool; otherwise a window glyph from Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10), drawn in the Windows mode's color, or in the high contrast text color.
- The icon is drawn into PC Companion's own pixels (`IconImage`), since the app can destroy its icon at any time.
- It's read again when the window's title changes, as apps that show a count of new messages change their title and icon together. Windows reports no change of the icon alone to programs that don't use the shell hook; see [Not included](#not-included).
- With several windows of the app in the tray, the icon is that of the window that went last.

### Tooltip

- With one window in the tray: its title. With several: the number of windows, such as "3 windows", then each title on its own line, the window that went last first. A window without a title shows the app's name.
- At most 127 characters, the limit Windows sets; a longer one is cut without splitting a character.
- **Added with the app's name only, then given the titles:** Windows 11 keeps the tooltip an icon is added with in the registry (`HKCU\Control Panel\NotifyIconSettings`, `InitialTooltip`), with a picture of the icon, also after the icon is gone, and Windows 11 names the icon for screen readers with that tooltip followed by the current one. The spike showed that later tooltips aren't kept, so no title ends up there. `TrayIcon` already adds an icon with a name that stays the same.
- Updated when a window's title changes (`EVENT_OBJECT_NAMECHANGE`) and when a window goes or comes back.

### Hidden icons

- Windows 11 and Windows 10 put new notification icons in the hidden icons menu (^), and give apps no documented way to show their own; PC Companion doesn't try.
- The feature page says how to keep an app's icon in sight, as System Meters' page does: drag it from the hidden icons menu onto the taskbar, or turn it on in the taskbar settings, with **Open taskbar settings** (`ms-settings:taskbar`).
- The PC Companion icon's menu lists every window in the tray, for when the icons are in the hidden icons menu.
- Windows 11 left the hidden icons menu open after an icon in it brought a window back to the front, in the Sandbox checks; it closes when the user clicks elsewhere or presses Esc. Windows offers apps no documented way to close it.

## Bringing a window back

- **Selecting an app's icon,** with the mouse or with Enter or Space from the keyboard, brings back the app's window that went to the tray last.
- **Right-clicking it,** or Shift + F10 or the menu key, opens its menu: one item per window, by title, the one that went last first; **Bring all back**, when there are several; then **Minimize to Tray settings**, which opens the feature's page. The menu follows the Windows mode, dark or light, and high contrast, as the PC Companion icon's menu does (`PopupMenu`, `MenuTheme`), and is laid out right to left in right-to-left languages.
- **The PC Companion icon's menu** gets a **Windows in the tray** submenu, above Settings and Exit, while at least one window is in the tray: one item per window, as "App name: title", then **Bring all back**. The host asks the feature for it each time the menu opens (`IHostMenuItems`), and the feature answers from what it already knows.
- **How:** `ShowWindowAsync` with `SW_RESTORE`, which shows the window, restores it from minimized, maximized again if it was, and doesn't wait for the app. Once the window shows, PC Companion brings its open dialog, if any, or the window to the front (`SetForegroundWindow` on `GetLastActivePopup`): done at once, the window came to the front behind its own dialog in the spike. Its owned windows, such as palettes, come back with it.
- Windows lets a program bring a window to the front after the user selects its notification icon, as the PC Companion icon does with the settings window, and while its menu is open. The Sandbox doesn't apply the foreground lock, so this is tested by hand.
- **Virtual desktops:** a window comes back on the virtual desktop that's showing, as Windows places a window that's shown again; Windows doesn't switch desktops. The spike confirmed it with a window hidden on one desktop and brought back on another.
- The window's mark is removed when it comes back; see [Never losing a window](#never-losing-a-window).

## While a window is in the tray

- **How it's hidden:** `ShowWindowAsync` with `SW_MINIMIZE`, then with `SW_HIDE`. The app sees an ordinary minimize, then its window hidden, as when an app minimizes itself to the tray; Windows hides its owned windows with it, and the window is in no switcher. For a window that's already minimizing, as with Shift + Minimize, only `SW_HIDE`.
- **Its title changes:** the tooltip and the menus follow, and the icon is read again (`EVENT_OBJECT_NAMECHANGE`).
- **The app shows it again** on its own, as for a reminder, a call or a second launch of the app: the app wins. The window stays shown, it leaves the tray, its icon is updated or goes, and its mark is removed (`EVENT_OBJECT_SHOW`). PC Companion never hides it again on its own.
- **It closes, or its app ends:** it leaves the tray and its icon is updated or goes (`EVENT_OBJECT_DESTROY`, which Windows also raises when the process is ended). A window that's gone is also noticed when its icon or menu item is chosen.
- **The app stops responding:** nothing waits for it. Hiding and showing are passed on to the app, which carries them out when it responds again; the title is read without a message (`GetWindowText` on another program's window reads Windows' copy), and the icon with `SendMessageCallback`.
- **Explorer restarts:** Windows sends `TaskbarCreated` and the icons are added again. File Explorer windows in Explorer's process close with it and leave the tray.
- **How it's watched:** one out-of-context WinEvent hook for each process with windows in the tray, for `EVENT_OBJECT_DESTROY` to `EVENT_OBJECT_SHOW` and for `EVENT_OBJECT_NAMECHANGE`, removed when that process has no window left in the tray. Events for other windows of the process are dropped at once.

## Never losing a window

- **The mark:** before a window is hidden, PC Companion sets a window property on it (`SetProp`, named `PCCompanion.MinimizeToTray`), and removes it when the window comes back. The spike confirmed that another program can set a property on a window of the same rights and that a later process reads it. The mark lives as long as the window, so nothing outlasts the window or a restart of Windows, and there's no file to keep right.
- The icon is added and the mark set before the window is hidden. When the mark can't be set, as for an app running as administrator, the window isn't hidden.
- **When PC Companion exits** for any reason it's told about: **Exit**, turning the feature off, setup closing it for an update or a removal (`WM_CLOSE`), Restart Manager and the end of the session (`WM_ENDSESSION`). Every window in the tray comes back on the taskbar, minimized, without taking the front (`ShowWindowAsync` with `SW_SHOWMINNOACTIVE`); its mark is removed and its icon goes. The host waits up to 2 seconds for the feature's thread to finish, as for System Meters.
- **Signing out, restarting and shutting down:** the windows come back on the taskbar when Windows asks whether the session may end (`WM_QUERYENDSESSION`), before their apps are asked to close, so an app's question about unsaved changes shows with its window, and Windows' list of apps that keep the session from ending can name it. When the feature first starts, the host asks Windows to ask it before other apps (`SetProcessShutdownParameters` with level `0x3FF`, the highest that apps may use). When the user cancels the sign-out, the windows stay on the taskbar.
- **When PC Companion crashes:** a handler for unhandled exceptions, which the feature adds to the host's process, brings the windows in the tray back on the taskbar before the host ends, from a list the feature's thread keeps for it. A failure on the feature's own thread does the same. When the host is ended without a chance to run code, for example from Task Manager, the windows stay hidden until it starts again.
- **At the next start,** on or off, the feature brings back on the taskbar, minimized, every hidden window that carries the mark, and removes the mark. It doesn't put them back in the tray: a crash wasn't the user's choice, and a window that made the host crash won't make it crash again. Opening PC Companion from Start starts the host, so the user has a way back at any time.
- Windows in the tray at sign-out don't go to the tray again at the next sign-in; apps that Windows starts again then open as they choose.

## Apps running as administrator

- Windows keeps the windows of apps running as administrator away from programs that don't run as administrator. The spike, with a copy of the spike labeled to run without admin rights, confirmed that Windows refuses to hide, move or mark such a window (error 5), still reports its minimize, and lets its title be read.
- So without admin rights, the shortcut plays the warning sound for such a window, and a listed app's window stays minimized on the taskbar.
- Windows also hides the keys from a program without admin rights while an app running as administrator is in front, so PC Companion doesn't see Shift held: Shift + Minimize on such a window minimizes it to the taskbar as usual, without the sound. The Sandbox checks showed both, with the host labeled Medium integrity: the shortcut reached it and was refused, and Shift + Minimize didn't.
- With Always run as administrator, or UAC off, every window goes to the tray. Running as administrator, the host's window accepts the taskbar's messages, as the PC Companion icon's window does (`TrayIcon`).
- In the spike, a minimize posted by the program without admin rights (`WM_SYSCOMMAND` with `SC_MINIMIZE`) did minimize the window of the one with them, with UAC off. Minimize to Tray doesn't rely on it; it's checked by hand with UAC on.

## Threads

- Minimize to Tray runs on a thread of its own, derived from `MessageLoopThread<TSettings>`, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: `Shell_NotifyIcon` waits for Explorer with no time limit, so a slow Explorer delays only these icons, never the other features ([#184](https://github.com/Lazyb0y/pc-companion/issues/184) is the same for the PC Companion icon).
- **The host's UI thread** runs only `Apply`, which reads the section and passes it to the feature's thread, and the **Windows in the tray** submenu, built from a list the feature's thread keeps for it. A chosen item is passed to the feature's thread. The first `Apply` also looks for windows left marked: it reads every top-level window's properties, which waits for no program.
- **The feature's thread** has its own hidden window and runs everything else: the shortcut, the WinEvent hooks, the 100 ms wait after a minimize, hiding, showing and marking windows, reading titles, window icons and app identity, drawing the icons, `Shell_NotifyIcon`, `TaskbarCreated`, the icons' messages and menus, `WM_QUERYENDSESSION`, and the warning sound.
- **The thread pool** reads the program file's icon and description and the app's name in Start, each once per app, and passes them back to the feature's thread with `MessageWindow.BeginInvoke`.
- Nothing on either thread waits for another program's window: `ShowWindowAsync`, `SendMessageCallback`, `GetWindowText`, `GetProp` and `SetProp` don't, and `ShowWindow`, which does, isn't used; see [Spike results](#spike-results).
- **Stopping** brings every window back on the taskbar, closes an open menu (`EndMenu`) as System Meters does, removes the icons, hooks, shortcut and window, and ends the thread.
- **Failures:** a failure on the feature's thread is logged, brings the windows in the tray back on the taskbar, and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), with test windows it created and File Explorer, Edge, a command prompt, Character Map, Task Manager and Settings, confirmed:

- **Minimizing, then hiding** takes the window off the taskbar and out of Alt + Tab. Its palette, a message box and the Open dialog hid with it and came back with it. It came back at the same place and size, maximized when it had been. It worked the same for File Explorer, Edge, the command prompt, Character Map and Task Manager.
- **Hiding only** left the owned palette on screen. **Cloaking** another program's window was refused (`E_ACCESSDENIED`). **The tool window style** took the window off the taskbar but changed its title bar.
- **Removing the taskbar button** (`ITaskbarList::DeleteTab`) also took the window out of Alt + Tab while it stayed on screen, and the button stayed away after the window was activated, minimized, restored, renamed, and hidden and shown by its app, until Explorer restarted.
- **Settings** kept its taskbar button and Alt + Tab entry after being hidden, and was shown again, in front, within 5 seconds.
- **A Windows Forms dialog** closed, as canceled, when its window was minimized, even without hiding it. A Win32 message box and the Open dialog stayed.
- **Virtual desktops:** windows hidden on one desktop came back on the desktop showing. A hidden window has no desktop. Moving another program's window to a desktop (`IVirtualDesktopManager::MoveWindowToDesktop`) was refused.
- **Events:** a title change while hidden raised `EVENT_OBJECT_NAMECHANGE`, the app showing its window `EVENT_OBJECT_SHOW`, and closing it and ending its process `EVENT_OBJECT_DESTROY`. Changing only the icon raised no window event; the shell hook reported it, and the window flashing (`HSHELL_REDRAW`, `HSHELL_FLASH`).
- **An app that stopped responding:** minimizing and hiding its window took 0.3 ms, reading its title under 0.1 ms and asking for its icon with `SendMessageCallback` 2 ms, and the answer came once the app responded. `ShowWindow` waited 2.7 seconds.
- **Minimize events:** `EVENT_SYSTEM_MINIMIZESTART` came 15 to 48 ms after the mouse button came up; Shift was seen when let go 20 ms or more after the button. Windows + D, Windows + M and Windows + Home started each window's minimize within 31 ms of the others, and the Windows key was still held at the event, also for Windows + Down.
- **The mark:** `SetProp` on another program's window worked, and another process read it. A program without admin rights was refused it on a window of one with them.
- **Notification icons:** Windows 11 kept one entry per program and icon ID, with the tooltip the icon was added with and a picture of it, also after the icon was removed. Later tooltips weren't kept, and adding the same ID again with another tooltip kept the first one. `TaskbarCreated` came about 1.6 seconds after Explorer was ended, and the icons were added again.
- **Coming back as PC Companion exits:** `SW_SHOWMINNOACTIVE` put the window on the taskbar, minimized, and left the window in front where it was.
- **Apps running as administrator:** a copy of the spike whose file was labeled Medium integrity ran without admin rights; Windows refused it hiding, moving and marking a window of an app with them (error 5), and still reported that window's minimize.
- **Not checked yet**, all by hand: UAC on, signing out and shutting down, Windows 10, the foreground lock, Narrator, desktop apps in Store packages such as Notepad and Windows Terminal, which the Sandbox doesn't have, a real mouse and keyboard, and several displays.

## Settings

- **Feature page:** the on/off switch; **Shortcut**, Windows + Shift + Z or None; **Shift + Minimize**; **Apps that go to the tray when minimized**, with **Running app**, **Program file** and **Remove**; a note about the hidden icons menu with **Open taskbar settings**; a note about the windows that can't go to the tray; and a **Permissions & data** card.
- When the feature is first turned on, the shortcut is Windows + Shift + Z, Shift + Minimize is on and no app is listed.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Minimize to Tray off with a warning in the log. An unknown shortcut registers none, with a warning. Listed apps are read as Hot Corners reads its list: a blank app ID or program counts as none, and a listed app with neither or both, or a `null`, is left out.

```json
"features": {
  "minimizeToTray": {
    "enabled": true,
    "shortcut": "WindowsShiftZ",
    "shiftMinimize": true,
    "minimizedApps": [
      { "name": "Outlook", "program": "C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE" },
      { "name": "Photos", "appId": "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" }
    ]
  }
}
```

- `shortcut`: `WindowsShiftZ` or `None`.
- `name` is shown on the page only.

## Not included

- **Close to the tray:** catching a click on Close needs code in the app, and Microsoft's guidelines advise against it.
- **Right-click on Minimize, a title-bar button, or an item in the window menu:** each needs a mouse hook, which sees every click and makes every click wait for PC Companion, or code in every app.
- **Keeping a window in the tray while it's open,** on screen but off the taskbar, as apps that live in the tray do: possible with `ITaskbarList::DeleteTab`, which on Windows 11 24H2 also takes the window out of Alt + Tab until Explorer restarts. It's a mode of its own, for later.
- **Starting an app in the tray, sending a window when it loses the focus, and matching windows by title,** such as a web app in a browser tab: later. Installed web apps have app IDs of their own, so they can be listed.
- **Following a change of the icon alone, and showing when a window in the tray asks for attention** (flashes): only the shell hook (`RegisterShellHookWindow`) reports them, and Microsoft documents it as not intended for general use.
- **One icon per window, or one icon for all windows:** one per window fills the hidden icons menu and the taskbar settings; the **Windows in the tray** submenu already gives one list of all.
- **A shortcut to bring back the last window, or all of them,** and an icon of the user's choosing for an app.
- **Store apps in the shared app frame,** which Windows shows again, and moving windows to a hidden virtual desktop or cloaking them, which Windows refuses for other programs' windows, or allows only through undocumented interfaces.
- **Putting windows back in the tray after a crash or at the next sign-in:** a crash wasn't the user's choice, and apps that Windows starts at sign-in open their windows as they choose.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The APIs are documented and in every supported version: `ShowWindowAsync`, `SetProp`, `GetProp`, `RemoveProp`, `SetWinEventHook`, `RegisterHotKey`, `GetAsyncKeyState`, `Shell_NotifyIcon`, `SendMessageCallback`, `GetClassLongPtr`, `IsWindowEnabled`, `GetLastActivePopup`, `SetProcessShutdownParameters` and `MessageBeep`.
- Windows 10 lists notification icons in the taskbar settings in its own way, and the spike ran on Windows 11 only, so what Windows 10 keeps about the icons, and the rest of the feature there, are tested by hand.

## Security and privacy

- **Reads:** for a window being sent, its styles, owner, whether it's enabled, its class name, to recognize the shared app frame, its app's identity, its title and its icon; while it's in the tray, its title when it changes. With Shift + Minimize on, or apps listed, Windows reports each window starting to minimize, and PC Companion reads then whether Shift and the Windows key are held, and with apps listed, the window's app's identity. It receives no keys or clicks: no keyboard or mouse hook. Titles stay in memory only, while the window is in the tray, and are never written or logged.
- **Windows keeps** the tooltip each notification icon is added with, and a picture of it, in the user's registry. PC Companion adds each icon with the app's name, so no window title is kept.
- **Changes:** other apps' windows: minimizes, hides, shows and brings them to the front, and adds and removes its mark on them. It runs no code in other apps, sends them no input and writes no file. It asks Windows to end the host early at sign-out.
- **Logs:** Debug entries when a window goes to the tray or comes back, and why (the shortcut, Shift + Minimize, a listed app, its icon, a menu, the app, PC Companion exiting, the next start), when a window can't go and why, and how many windows are in the tray. Never titles, app IDs, names or paths, which can name documents, profiles or users. Warnings when Windows refuses the shortcut or the settings section can't be read, and an error when a failure stops the feature.
- **Settings:** the shortcut choice, the Shift + Minimize switch and the list of apps, so a changed settings file can only send listed apps' windows to the tray when they're minimized, and only windows that Windows lets the host change.
- **The mark:** another program running as the user could mark its own hidden windows, or another app's, so that PC Companion shows them at its next start; it could show them itself just as well. Windows refuses a program without admin rights the mark on a window of an app running as administrator, as the spike showed, so a host running as administrator can't be made to show such a window.
- **Network:** none. `PRIVACY.md` gets a line on Minimize to Tray.
- **Rights:** runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Running as administrator, it also sends windows of apps running as administrator, which the user can minimize anyway. Turning the feature off brings every window back and removes the shortcut, hooks and icons.

## Performance

- Between sends, nothing runs: no timer. The minimize hook, set only while Shift + Minimize is on or apps are listed, gets one event per minimize. Each process with windows in the tray has its own hook, whose events for its other windows are dropped at once.
- Sending a window reads one window's details, sets its mark and hides it, well under a millisecond on the feature's thread; reading the app's name and program icon happens once per app, on the thread pool.
- Icons are sent to Explorer only when their picture or tooltip changes.

## Accessibility and localization

- **Keyboard:** the shortcut sends the window in front. Windows + B moves to the notification area, where Enter opens the hidden icons menu and the arrow keys reach the icons; on an icon, Enter or Space brings back the app's last window, and Shift + F10 or the menu key opens its menu. The PC Companion icon's menu lists every window in the tray.
- **Screen readers** read an icon as the app's name followed by its tooltip, such as "Outlook Inbox - Outlook". When a window goes to the tray, Windows moves the focus to the next window, which screen readers announce; a window brought back gets the focus. Minimize to Tray announces nothing of its own: the host has no UI Automation provider, so a screen reader user hears where the focus went, not that the window went to the tray. The page says where windows go.
- **Colors:** the apps' own icons are shown as they are; the window glyph and the menus follow the Windows mode, dark or light, and high contrast.
- **Right to left:** the menus are laid out right to left in right-to-left languages, and Windows shows tooltips in them right to left. Titles show as the apps write them.
- The page works with the keyboard alone, and every switch, list and button has a label for screen readers.
- "3 windows" and the other counts use Windows' plural rules (ICU) for each language. The strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake windows, a fake clock and fake Windows calls:
  - which windows can go: taskbar windows, a palette sending its main window, a disabled main window, the shared app frame, a mark that Windows refuses, the desktop and no window in front, each with or without the warning sound;
  - Shift + Minimize: Shift held, not held, and the Windows key held;
  - listed apps: a window's own app ID, the same app ID read from a package, a program path in another case, and windows of other apps;
  - Show desktop: two windows starting to minimize within 100 ms send neither, one alone sends it, and the Windows key held sends nothing;
  - icons: one per app, the same ID for the same app, the next free ID for a clash, never 1 to 4, and an icon that goes with the app's last window;
  - tooltips: one window, several, no title, cutting at 127 characters without splitting a character, and the plural forms;
  - while in the tray: a title change, the app showing its window, the window closing and its process ending, and Explorer restarting;
  - coming back: the last window first, all, the mark removed, and on exit every window back minimized without taking the front;
  - the next start: marked hidden windows come back, unmarked and visible ones are left alone, with the feature on and off;
  - the settings: defaults, the spec's example, an unknown shortcut, listed apps that don't match, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/MinimizeToTray.ps1`), with test windows the checks start:
  - the Home switch turns the feature on with its defaults, and the page saves the shortcut, Shift + Minimize and the app list, with Running app and Remove; Program file is the same list as Hot Corners', whose checks cover it;
  - Windows + Shift + Z, sent by `SendInput`, sends the test window and its palette to the tray: hidden, off the taskbar, and the app's icon found by its name in the hidden icons menu; selecting the icon brings it back where it was and maximized when it was, in front;
  - Shift + Minimize sends a window, also Edge's, whose title bar Edge draws itself; Minimize without Shift doesn't;
  - a listed app's window goes to the tray when minimized, and Windows + D and Windows + M send nothing;
  - a title change shows in the tooltip; the app showing its window takes it out of the tray; closing it and ending its process remove its icon; restarting Explorer brings the icons back;
  - a test window that stops responding doesn't hold the host: the send and the bringing back end at once, as the host's log shows, and the window follows once it responds;
  - with a second virtual desktop, a window sent from the first comes back on the second;
  - File Explorer and Edge windows go to the tray and come back; Settings and a window with a dialog open don't, and the host logs why;
  - with the host's file labeled Medium integrity, as the spike did, a test window running as administrator doesn't go, and the host logs why;
  - **Exit** brings every window back on the taskbar, minimized; ending the host with `Stop-Process` leaves them hidden, and the next start brings them back;
  - the icon's menu and the **Windows in the tray** submenu in the Windows mode's colors, light and dark, and in Arabic, with screenshots.
- **What the Sandbox can't check:** UAC on, signing out and shutting down, the foreground lock, Windows 10 and Narrator.
- **By hand:**
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - signing out and shutting down with a window in the tray that has unsaved changes, and canceling the sign-out;
  - that selecting an icon brings the window to the front on a PC, where the foreground lock applies;
  - Shift + Minimize with a real mouse, and Windows + D, Windows + M and Windows + Down with a real keyboard;
  - desktop apps in Store packages, such as Notepad and Windows Terminal, a browser with two profiles and an installed web app, and Office;
  - moving an app's icon onto the taskbar, then sending the app again after a restart and after an update;
  - selecting an icon in the hidden icons menu: the window comes to the front, and whether the menu stays open;
  - Narrator reading the icons, their menus and the focus after a send;
  - several displays with different scales, high contrast, and a right-to-left language;
  - Windows 10, including its taskbar settings for notification icons.
