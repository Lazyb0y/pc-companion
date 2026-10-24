# App Windows

Press Alt and the key above Tab to switch to another window of the app in front, such as another File Explorer window.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.appWindows`.
- Inspired by Command + `` ` `` in macOS, and by switching between an app's windows in GNOME and KDE Plasma, which use Alt and the same key.
- Off until the user turns it on.

## Behavior

- **Alt + the key above Tab** brings the app's window that is furthest back to the front. Pressing it again goes on to the next one, so repeated presses visit every window of the app and then come back to the first, as in macOS.
- **Alt + Shift + the key above Tab** goes the other way: the app's second window comes to the front, and the one that was in front goes behind all windows, as with Alt + Esc. One press each way leaves the app's windows in the order they were.
- Each press switches right away, whether Alt is still held or not. No list or preview appears.
- The app in front is the app of the window in front, or of the window that owns it, such as a dialog's main window.
- With only one window, or when no app is in front, such as the desktop or the taskbar, the shortcut does nothing.
- A window with an open dialog comes to the front with its dialog, as with Alt + Tab.
- No window moves or changes size, and always-on-top windows stay on top. Windows of other apps keep their places, except that going back puts the app's old front window behind them.
- Holding the key down switches once; it doesn't repeat.

## Which windows

- The app's windows that Alt + Tab shows: visible windows that aren't tool windows, and aren't owned by another window unless they show on the taskbar.
- Only windows on the current virtual desktop, as Alt + Tab shows by default. Windows hides the others from the screen (cloaks them), as it does for suspended Store apps, and cloaked windows are skipped.
- Minimized windows are skipped, as in macOS, unless **Include minimized windows** is on. Then a minimized window is restored when its turn comes.
- Windows on every display count.
- The desktop and the taskbar, which belong to File Explorer, never count.

## What counts as one app

- The windows the taskbar groups as one app, by their app ID (AppUserModelID):
  - the ID a window sets for itself, as browsers do for each profile;
  - otherwise the ID of the window's Store app package;
  - otherwise the program file.
- Store apps' windows belong to one shared frame process, `ApplicationFrameHost.exe`, so the process alone can't tell them apart.
- An app can also set one ID for its whole process. Windows offers no documented way for another program to read it, so such an app's windows are grouped by program file, which rarely differs from the taskbar.

## The shortcut

- It goes by the key's position: the key above Tab and left of 1, which is `` ` `` on US English keyboards, `^` on German ones and `²` on French ones. GNOME does the same, and Windows Terminal binds the Windows key with this key, by position, for its Quake mode.
- **Shortcut** offers Alt, the default, or the Windows key with the key above Tab, as GNOME does. Shift goes the other way with either.
- **How:** `RegisterHotKey`, the Windows API for global shortcuts, with `MOD_NOREPEAT`. No keyboard hook: Windows tells PC Companion only when the shortcut is pressed.
- Windows matches shortcuts by virtual key, which depends on the keyboard layout of the window in front: the key above Tab is `VK_OEM_3` in US English, `VK_OEM_5` in German and `VK_OEM_7` in French. The host maps the key (scan code `0x29`) to its virtual key in that layout (`MapVirtualKeyEx`), and registers the shortcut again when the virtual key differs.
- The layout is read from the window with the keyboard focus. A Store app's frame, which is the window in front, belongs to a shared process whose layout doesn't follow layout switches.
- The host checks each time another window comes to the front, which an out-of-context WinEvent hook (`EVENT_SYSTEM_FOREGROUND`) reports without running code in other programs.
- Windows reports no layout change within the same window, such as with Left Alt + Shift or Windows + Space. So while the installed layouts don't all put the same virtual key above Tab, as with US English and German, the host also checks the front window's layout once a second. With one layout, or layouts that agree, no timer runs; whether they agree is checked again each time another window comes to the front.
- While the layouts disagree, the host also checks again 100 ms after another window comes to the front: Windows can switch that window's layout a moment after it comes to the front, so a quick next press would otherwise miss.
- While the feature is on, other apps don't get the shortcut. JetBrains IDEs, for example, use Alt + `` ` `` for their version control menu. The feature page says so.
- The Microsoft Japanese IME uses Alt + `` ` `` to turn Japanese input on and off, and keeps it: while Japanese input is active, Alt + the key above Tab doesn't reach PC Companion. Windows + the key above Tab works with it.
- When another program already has the shortcut, Windows refuses it. The shortcut then does nothing, and a warning is logged.

## Apps running as administrator

- Windows keeps the input and windows of apps running as administrator away from programs that don't run as administrator. While such an app is in front, the shortcut does nothing; the feature page says so. Always run as administrator avoids it.

## Threads

- App Windows has no thread of its own: between presses it only checks the layout, and a press is short.
- **The host's UI thread** runs `Apply`, which reads the section and registers or removes the shortcut and the foreground hook, the layout checks, and each press: listing the windows, reading their apps' identity and switching.
- **The thread pool** runs only the layout check's timer, which passes the check to the UI thread with `MessageWindow.BeginInvoke`.
- **Nothing waits for the windows' programs,** so one that stopped responding can't hold the host. Listing the windows and reading their apps' identity send them no messages. Restoring a minimized window (`ShowWindowAsync`) and putting the old front window behind the others (`SetWindowPos` with `SWP_ASYNCWINDOWPOS`) are passed on to the window's program, which carries them out when it handles its messages, and `SetForegroundWindow` doesn't wait for it either. So the windows of a program that stopped responding switch once it responds again.
- **Failures:** the press and the checks run inside the feature's error boundary. A failure is logged and keeps the feature off until the host restarts, like a failing `Apply`.

## Spike results

A spike in Windows Sandbox, with keys sent by scan code, confirmed:

- Each press brings the next window to the front, whether Alt is held or released between presses, and with the Windows key too. A minimized window is restored when its turn comes. One press each way leaves the order as it was.
- Putting the old front window right behind the app's last window, with `SetWindowPos`, reports success but leaves File Explorer's windows, each in its own process, where they were. Putting it behind all windows works, and it stays in front of the desktop.
- Releasing Alt or the Windows key after the shortcut opens no menu bar and no Start, while Alt or the Windows key pressed alone does.
- On the German layout, the shortcut takes the `^` dead key without changing the next character: an `e` typed after it stays `e`.
- Settings, a Store app in the shared frame process, has its app ID on its frame window. File Explorer windows have none and are grouped by program file.
- None of these reports a layout change within the same window: `WM_INPUTLANGCHANGE` to the host's window, the shell's `HSHELL_LANGUAGE`, the text input framework's language and profile notifications on the host's thread, and every out-of-context WinEvent. Windows Terminal maps its Quake mode key once and doesn't follow layout changes either.
- Windows lets a program bring a window to the front after the user pressed its shortcut, and every press did. Windows Sandbox didn't apply the foreground lock, which otherwise stops programs from taking the front, so this is also tested by hand.
- Apps running as administrator can't be tested there, because everything in Windows Sandbox runs as administrator.

## Settings

- **Feature page:** the on/off switch; **Shortcut**, Alt or the Windows key with the key above Tab, shown with the key's character in the current layout, such as "Alt + `` ` ``"; **Include minimized windows**, off by default; a note about apps that use the same shortcut and apps running as administrator; and a **Permissions & data** card.
- When the feature is first turned on, the shortcut is Alt + the key above Tab.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns App Windows off with a warning in the log. An unknown shortcut registers none, with a warning.

```json
"features": {
  "appWindows": {
    "enabled": true,
    "shortcut": "AltAboveTab",
    "includeMinimized": false
  }
}
```

- `shortcut`: `AltAboveTab` or `WindowsAboveTab`.

## Not included

- Showing all of the app's windows at once, as App Exposé in macOS does.
- A list of the windows while Alt is held, as in GNOME and KDE Plasma.
- Windows on other virtual desktops.
- Other shortcuts.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- Global shortcuts, WinEvent hooks, cloaked windows and app IDs are in every supported version.

## Security and privacy

- **Reads:** when the shortcut is pressed, the top-level windows' styles, owners, state and whether they're cloaked, and their app's identity: the window's app ID, the app's package, or the program's path. Never window titles or contents. Nothing is stored or sent; the list is dropped after the press.
- **Keyboard:** no keyboard hook; Windows reports only the shortcut. The foreground hook and the once-a-second check read only which window is in front and its keyboard layout.
- **Changes:** only the front app's windows: which one is in front, their order behind it, and restoring a minimized one. No input is sent, no program starts and no file is written.
- **Logs:** Debug entries with the direction and the number of the app's windows. Never titles, app IDs or paths, which can name documents, profiles or users. A refused shortcut is logged as a warning.
- **Settings:** only the shortcut choice and the switch, so a changed settings file can't make PC Companion do anything else, also while it runs as administrator.
- **Rights:** runs without admin rights. With Always run as administrator, it also switches the windows of apps running as administrator, as the user could with Alt + Tab. Turning the feature off removes the shortcut and the foreground hook.

## Performance

- Between presses, the foreground hook checks the layout each time another window comes to the front. A check takes about 1 µs, and the shortcut is registered again only when the key's virtual key changes.
- The once-a-second check runs only while the installed layouts disagree on the key above Tab.
- A press lists the windows once and reads app IDs only for windows that can be switched to. In Windows Sandbox, a press took under 20 ms with 578 top-level windows open, 67 of them switchable.

## Accessibility and localization

- The page works with the keyboard alone, and its list and switch have labels for screen readers.
- Switching moves the keyboard focus to the window in front, so screen readers announce it as they do after Alt + Tab.
- The shortcut works in every keyboard layout. The page shows the key's character in the user's layout and calls it "the key above Tab".
- The strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests** with fake window lists:
  - which windows count: hidden, cloaked, tool, owned and minimized windows, with and without Include minimized windows, and the desktop and the taskbar;
  - what counts as one app: a window's own app ID, a Store app in the shared frame process, and the program file;
  - forward and back visit every window and undo each other;
  - one window, no app in front, a dialog in front, and always-on-top windows.
- **Unit tests for the shortcut**, with fake layouts, a fake clock and fake registration: Shift for back, registering again only when the virtual key changes, the once-a-second check running only while the layouts disagree, and a warning when Windows refuses the shortcut.
- **Sandbox checks** (`scripts/sandbox/groups/AppWindows.ps1`), with three File Explorer windows the checks open: the Home switch turns the feature on, and its page saves the shortcut and Include minimized windows. Alt + the key above Tab, sent by `SendInput` by scan code, brings each window to the front in turn, the one furthest back first, and a press for each window leaves them in their first order; Shift goes back, and a press forward undoes it; the host logs the switches. With the German layout added, the same key still switches after switching to it in the same window. With Include minimized windows on, a minimized window comes back restored and in front. A test program with two windows, which stops responding for a few seconds when asked, doesn't hold the host: going back from one of its windows, and restoring the other while it's minimized, end at once, as the host's log shows, and once the program responds again, its windows are where the switch put them. Turned off, the key switches no window. Everything runs as administrator in Windows Sandbox, so apps running as administrator are tested by hand.
- **By hand:**
  - that each press brings a window to the front on a PC, where Windows' foreground lock applies;
  - File Explorer, a browser with two profiles, and a Store app with several windows;
  - an app running as administrator in front, with and without Always run as administrator;
  - several virtual desktops and displays;
  - the French and German layouts, and switching layouts while the same window stays in front;
  - the Japanese IME's own Alt + `` ` `` on a physical keyboard, and the Windows key option with it;
  - Windows Terminal with the Windows key option;
  - and that releasing Alt opens no menu bar in the window that came to the front.
