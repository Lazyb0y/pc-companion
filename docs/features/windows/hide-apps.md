# Hide Apps

Put away every window of the app in front, or of every other app, with one shortcut, and bring them back as they were.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.hideApps`. Issue: [#31](https://github.com/Lazyb0y/pc-companion/issues/31).
- Inspired by Hide, Hide Others and Show All in macOS.
- Off until the user turns it on.

## Behavior

- **Windows + Ctrl + H** (Hide app) minimizes every window of the app in front, as the taskbar groups them, such as every File Explorer window. The window behind them comes to the front, as when a window is minimized.
- **Windows + Ctrl + Shift + H** (Hide others) minimizes every window of every other app. The app in front keeps all of its windows and the focus.
- **Pressing Hide others again,** while the same app is in front, brings back the windows it minimized as they were: in their order, behind the app in front, at their place and size, maximized or snapped as they were.
- **When a window that Hide Apps minimized comes back,** from its taskbar button, Alt + Tab, starting the app again or the app itself, the app's other windows that Hide Apps minimized come back with it, right behind it, as an app comes back whole in macOS.
- **Show all hidden windows,** in the PC Companion icon's menu, brings back every window that Hide Apps minimized.
- Windows only minimize: they keep their taskbar buttons and stay in Alt + Tab, so none is ever lost, whatever happens to PC Companion; see [Never losing a window](#never-losing-a-window).
- Some windows stay, such as always-on-top windows and windows with a dialog open. Windows' warning sound plays when a window that should go stays, and the page explains why; see [Windows that stay](#windows-that-stay).

## Compared with other systems

- **Windows** has Windows + Home, which minimizes every window but the one in front and brings them back when pressed again. It keeps only that one window, not the rest of its app, and minimizes always-on-top windows, which in the spike came back no longer always on top. Windows + M and Windows + D minimize every window, on every display. Windows 11 dropped minimizing all of an app's windows from the taskbar, which Windows 10 offered for a group of buttons (Shift + right-click), so Windows has no way to minimize one app. Hide Apps keeps every window of the app in front, leaves always-on-top windows alone, can stay on the display in front, and has Hide app.
- **macOS** hides apps with Command + H and Option + Command + H ([Apple](https://support.apple.com/en-us/102650)). Hide Others can't be undone, Show All brings back every hidden app, not only those Hide Others hid, Hide Others acts on every display, and hidden apps look the same as running ones in the Dock. Pressing Hide others again brings back exactly the windows it minimized, Hide others can stay on the display in front, and minimized windows show as minimized on the taskbar. Like macOS, an app comes back whole when one of its windows does.
- **GNOME** has no Hide others, and its Show desktop leaves the windows minimized when a new window opens ([mutter#155](https://gitlab.gnome.org/GNOME/mutter/-/issues/155)). Xfce's Show desktop acts on every workspace. KDE Plasma added minimizing every other window only in version 6.6 ([197952](https://bugs.kde.org/show_bug.cgi?id=197952)), and its earlier scripts lost track of the windows they minimized and brought them back out of order. Hide Apps keeps its own list of the windows it minimized, which new windows and switching apps don't change, and brings them back one at a time in their order.
- **Tools for Windows that hide windows** take them off the taskbar: users report windows lost when the tool is ended or crashes, saved lists that are wrong after a restart, Store apps that can't be hidden, and tools whose hooks set off anti-cheat software. Hide Apps only minimizes, so windows stay on the taskbar whatever happens to PC Companion, and it runs no code in other apps and uses no keyboard or mouse hook.
- **Taking windows off the taskbar** is what [Minimize to Tray](minimize-to-tray.md) does, for one window at a time, with an icon to bring it back.

## Which windows go

### Hide app

- The app in front is the app of the window in front, or of the window that owns it (`TopLevelWindows.RootOwner`), such as a dialog's main window.
- Its windows that the taskbar shows (`TopLevelWindows.MayHaveTaskbarButton`) and that aren't minimized yet, on every display, with the exceptions in [Windows that stay](#windows-that-stay). Owned windows, such as palettes, go with their main window, as Windows hides them with it.
- The window in front is minimized last (`SW_MINIMIZE`), so Windows brings the window behind the app's windows to the front; the others are minimized without taking the front (`SW_SHOWMINNOACTIVE`).
- With no app in front, such as the desktop or the taskbar, the warning sound plays.

### Hide others

- Every window that the taskbar shows and that isn't minimized yet, of every app but the one in front, with the exceptions in [Windows that stay](#windows-that-stay). Each is minimized without taking the front (`SW_SHOWMINNOACTIVE`), so the app in front keeps the focus.
- **Hide others only on the display in front,** a switch on the page, off by default: Hide others then leaves windows on other displays alone. A window is on the display that has most of it (`MonitorFromWindow`).
- PC Companion's settings window counts as any other app's window.

### What counts as one app

- The windows the taskbar groups as one app, with the same code as App Windows, Minimize to Tray and Hot Corners (`AppOfWindow`): the app ID the window sets for itself, as browsers do for each profile and installed web app; otherwise its Store app package's app ID; otherwise its program file.
- So each browser profile and each installed web app is an app of its own, and every File Explorer window belongs to File Explorer, while the desktop and the taskbar never count.

### Windows that stay

- **The app in front's windows,** for Hide others.
- **Always-on-top windows,** for Hide others: the user put them there to keep them in sight. Windows + Home minimizes them, and in the spike an always-on-top window it brought back was no longer always on top. Hide app minimizes the app's always-on-top windows too, and they come back always on top.
- **Windows without a Minimize button,** as Windows + M leaves them.
- **A window with a dialog open,** such as a Save dialog, recognized by its main window being disabled (`IsWindowEnabled`): minimizing it canceled a Windows Forms dialog in the spike, as for Minimize to Tray. Windows + Home, Windows + M and Windows + D leave such windows alone too.
- **Windows on other virtual desktops,** which Windows keeps out of sight (cloaked), as Alt + Tab leaves them out by default.
- **Windows that are already minimized or hidden,** such as those in Minimize to Tray's tray.
- **The desktop, the taskbar and the shell's other windows,** which the taskbar doesn't show.
- **The stand-in Windows shows for a window that stopped responding** (class `Ghost`, in Desktop Window Manager's process). The window itself is asked to minimize, and does once its app responds.
- **Apps running as administrator** go only if they accept being asked; see [Apps running as administrator](#apps-running-as-administrator).
- Windows' warning sound (`MessageBeep` with `MB_ICONEXCLAMATION`) plays when a press leaves on screen a window that should have gone, one with a dialog open or one Windows refused, and when a press does nothing, such as with nothing in front, nothing to minimize or nothing to bring back. Windows that stay by design, such as always-on-top ones, play no sound.
- The feature page lists which windows stay and why. Its note about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.
- Each case is logged at Debug level with its reason, never with the app.

## The shortcuts

- **Windows + Ctrl + H** for Hide app and **Windows + Ctrl + Shift + H** for Hide others, registered with `RegisterHotKey` and `MOD_NOREPEAT`, so Windows tells PC Companion only when a shortcut is pressed. No keyboard hook. Shift means "the others", as Shift goes the other way in App Windows.
- Windows uses Windows + H for voice typing, and Windows + Alt + H while voice typing is open; a popular mouse utility uses Windows + Shift + H; Ctrl + Alt + H is AltGr + H on many keyboard layouts; and apps use Ctrl + H for Replace or History. Windows + Ctrl + H and Windows + Ctrl + Shift + H aren't in Windows' list of keyboard shortcuts, and Windows let a program register both on Windows 11 builds 26100 and 26300.
- **Hide app** and **Hide others** on the page each offer their shortcut or None.
- When another program already has a shortcut, Windows refuses it and a warning is logged. PC Companion tries again after any change to the settings.

## Bringing windows back

### Pressing Hide others again

- While the app in front is the one Hide others kept, the shortcut brings back every window that Hide others minimized and that's still minimized.
- With another app in front, it hides that app's others. Windows that Hide others minimized before stay minimized and come back with the next press's windows.

### The app's other windows

- When a window that Hide Apps minimized comes back, by any means, the app's other windows that Hide Apps minimized come back too, right behind it, in their order, without taking the front.
- **How it's noticed:** an out-of-context WinEvent hook for `EVENT_SYSTEM_MINIMIZEEND`, which Windows raises as any window starts to come back from minimized, set only while windows are in the list. The spike saw it for a click on the taskbar button, Alt + Tab, the app restoring its window itself and another program restoring it.
- **Bring back an app's other windows with it,** a switch on the page, on by default. Off, only the window the user chose comes back.

### Show all

- **Show all hidden windows,** in the PC Companion icon's menu above Settings and Exit while the list has windows, brings back every window that Hide app and Hide others minimized. With the menu open, no app's window is in front, so the windows come back in their order behind the windows on screen, and where Windows restores them when there's none. The top window on screen gets the front back, as before the menu opened, and the windows come back once it has it, or after half a second: otherwise Windows could give the front to a window coming back, even one on another virtual desktop, and switch to that desktop, as the Sandbox checks showed. Even so, the Sandbox checks saw Windows switch to the other virtual desktop some of the time, when windows Hide Apps minimized there came back from the menu; see [Not included](#not-included). The host asks the feature for it each time the menu opens (`IHostMenuItems`), and the feature answers from what it already knows.

### How

- **In place:** each window is restored without taking the front (`ShowWindowAsync` with `SW_SHOWNOACTIVATE`). Windows restores it at its place and size, maximized or snapped as it was, and leaves its place in the order of windows and its virtual desktop. Activating it instead would switch to its virtual desktop, as the spike showed.
- **Settled:** once they're all back, or after a second, PC Companion waits 400 ms more: restoring still moves some windows after they show, such as maximized and File Explorer ones, and a window moved meanwhile ended in the wrong place in the Sandbox checks. A window that moves itself as it comes back, as Character Map did in the spike, is put in order with the others.
- **In order:** then, one at a time in the order they had when they were minimized, each window is put right behind the one before (`SetWindowPos` with `SWP_ASYNCWINDOWPOS` and `SWP_NOACTIVATE`), the first right behind the app in front's last window, or right behind the window that came back. Each app moves its window when it handles its messages, so PC Companion waits for each move, up to 300 ms, before the next: the spike showed that a window moved behind one that wasn't in place yet lands in the wrong place, and that every way of moving them all at once came back out of order. A move is done when the window is the first window Alt + Tab shows behind the one before: owned windows, such as palettes, sit in front of their owner, and Windows' own helper windows, such as a Store app's core window and File Explorer's hidden-from-view ones, can sit anywhere. It's checked each time Windows reports that the order of windows changed (`EVENT_OBJECT_REORDER`, as System Meters watches the taskbar), and by a timer in case no report comes.
- In the Sandbox checks, bringing back five windows took about 440 ms, 400 ms of it the wait for them to settle.
- A window of an app that stopped responding (`IsHungAppWindow`) isn't waited for: it comes back when its app responds, behind the others.
- Only windows still minimized and still in the list come back. Windows the user minimized themselves were never in it.

### The list

- The feature's thread keeps, in memory only, each window it minimized: its handle, its process, its app, its place in the order and which shortcut minimized it.
- A window leaves the list when it comes back, by any means, or closes (`EVENT_OBJECT_DESTROY`, watched for the processes with windows in the list, as Minimize to Tray watches them). New windows, other windows coming and going and switching apps don't change it.

## Apps running as administrator

- Windows keeps the windows of apps running as administrator away from programs that don't run as administrator: in the spike, with a copy labeled to run without admin rights, Windows refused minimizing, restoring and moving such a window (error 5).
- So without admin rights, PC Companion asks such an app instead, as Windows' own minimize all does: it posts `WM_SYSCOMMAND` with `SC_MINIMIZE`, and `SC_RESTORE` to bring the window back. Most apps do as asked; an app may ignore it. In the spike, the request minimized the window and left the window in front where it was.
- A window brought back by request takes the front, as the spike showed, so PC Companion then gives the front back to the window that had it (`SetForegroundWindow`), which Windows allows right after the user pressed a shortcut or chose a menu item.
- Their place in the order can't be set: they come back where Windows puts them.
- Windows reports shortcuts while an app running as administrator is in front, so Hide app asks that app's windows to minimize.
- With Always run as administrator, or with UAC off, they're like any other window.

## With Minimize to Tray

- Windows in Minimize to Tray's tray are hidden, so Hide Apps never minimizes them, and Show all doesn't bring them back.
- Minimize to Tray sends a listed app's window to the tray when it's minimized, and a window minimized with Shift held. It leaves alone the windows Hide Apps minimizes, so they stay on the taskbar and come back with Hide others or Show all: the two features share a list, within the module, of the windows in Hide Apps' list, to which Hide Apps adds each window before it minimizes it, and Minimize to Tray logs why it left the window.
- With Minimize to Tray off, nothing changes.

## Never losing a window

- Hide Apps only minimizes: every window keeps its taskbar button and stays in Alt + Tab. When PC Companion exits, for any reason, crashes or is updated, or the user signs out, the windows stay minimized on the taskbar, where the user brings them back as any minimized window.
- Only the list goes: after PC Companion starts again, pressing Hide others again or Show all doesn't bring back the windows minimized before. No window is marked and no file is written, so there's nothing to recover.
- Turning the feature off, or **Exit**, leaves the windows minimized as they are.

## Threads

- Hide Apps runs on a thread of its own, derived from `MessageLoopThread<TSettings>`: [Threads and processes](../../architecture/features.md#threads-and-processes) keeps work on the host's UI thread to a few milliseconds, and a press lists and minimizes every window, 12.5 ms for 50 windows in the spike, while bringing windows back waits for each window in turn. The shortcuts and hooks need a message loop, which the thread pool doesn't have.
- **The host's UI thread** runs only `Apply`, which reads the section and passes it to the feature's thread, and the **Show all hidden windows** item, answered from a count the feature's thread keeps. A chosen item is passed to the feature's thread.
- **The feature's thread** has its own hidden window and runs everything else: both shortcuts, the WinEvent hooks, listing the windows and reading their apps, minimizing, bringing back and ordering windows, giving the front back, and the warning sound. It never blocks while it waits for a window to move: the next move starts from the order event or the timer, so the thread keeps handling messages meanwhile.
- **Nothing waits for another program's window:** `ShowWindowAsync`, `PostMessage` and `SetWindowPos` with `SWP_ASYNCWINDOWPOS` pass the request on to the window's program, and reading styles, owners, state, display and app identity sends no messages.
- **Stopping** removes the shortcuts, hooks and window, leaves the windows as they are, and ends the thread.
- **Failures:** a failure on the feature's thread is logged and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), with test windows it created and File Explorer, Edge, Settings and Character Map, confirmed:

- **Hide others** with `SW_SHOWMINNOACTIVE` kept the window in front in front, and a palette went with its main window. Listing and minimizing took 3 to 26 ms for 9 windows and 12.5 ms for 50. `SW_MINIMIZE` on a window behind didn't change the window in front.
- **Size and state:** every window came back at its place and size, maximized and snapped windows as they were, and an always-on-top window stayed always on top.
- **Order:** `SW_SHOWNOACTIVATE` restores a window without changing its place in the order, and where minimized windows end up in the order differs from one minimize to the next. Every way of bringing all windows back at once came back out of order. Putting each right behind the one before, waiting for each, gave the right order in every run. Moving the minimized windows into order before restoring them, waiting for each move, took 0.1 to 15 ms per window, usually 1 to 3, and came back in order except for Character Map, which moved itself in front when restored.
- **An app coming back whole:** `EVENT_SYSTEM_MINIMIZEEND` came for a click on the taskbar button, Alt + Tab, the app restoring itself and another program restoring it; the app's other window, brought back and put right behind it, ended in the right order, also for File Explorer.
- **Settings,** a Store app in Windows' shared app frame, stayed minimized and came back, unlike when it's hidden.
- **Apps running as administrator:** a copy labeled to run without admin rights was refused `ShowWindowAsync` and `SetWindowPos` (error 5). A posted `SC_MINIMIZE` minimized the window and left the window in front where it was; a posted `SC_RESTORE` brought it back and gave it the front, and `SetForegroundWindow` then gave the front back.
- **A dialog:** minimizing a Windows Forms window with a dialog open canceled the dialog. Windows + Home, Windows + M and Windows + D left that window alone, and the window without a Minimize button.
- **Virtual desktops:** windows on another desktop were cloaked and left out. A window restored with `SW_SHOWNOACTIVATE` came back on its own desktop; restoring and activating it switched desktops.
- **An app that stopped responding:** `ShowWindowAsync` returned in 0.01 ms, and the window minimized once the app responded. Once, Windows showed a stand-in window of class `Ghost`, in Desktop Window Manager's process, that looked like a taskbar window.
- **Windows' own shortcuts:** Windows + Home worked with the title bar shake setting off, on and absent. It minimized the app in front's second window and the always-on-top window, and brought the others back in their order behind the windows that stayed. Windows + Shift + M after Windows + M, and Windows + D twice, also brought them back in order.
- **Not checked yet:** whether each move was seen through `EVENT_OBJECT_REORDER` or the timer; the Sandbox checks saw five windows put in order about 40 ms after they settled. By hand: UAC on, the foreground lock, several displays, Windows 10, Narrator and a real keyboard.

## Settings

- **Feature page:** the on/off switch; **Hide app**, Windows + Ctrl + H or None; **Hide others**, Windows + Ctrl + Shift + H or None; **Hide others only on the display in front**; **Bring back an app's other windows with it**; a note about the windows that stay; and a **Permissions & data** card.
- When the feature is first turned on, both shortcuts are set, Hide others acts on every display and an app's other windows come back with it.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Hide Apps off with a warning in the log. An unknown shortcut registers none, with a warning.

```json
"features": {
  "hideApps": {
    "enabled": true,
    "hideAppShortcut": "WindowsControlH",
    "hideOthersShortcut": "WindowsControlShiftH",
    "frontDisplayOnly": false,
    "bringBackWholeApp": true
  }
}
```

- `hideAppShortcut`: `WindowsControlH` or `None`. `hideOthersShortcut`: `WindowsControlShiftH` or `None`.

## Not included

- **Hiding windows off the taskbar,** as macOS hides apps: the app would look closed, Windows shows Store apps in its shared app frame again, and windows could be lost. Minimize to Tray does it for one window, with an icon to bring it back.
- **A list of apps that Hide others leaves alone,** such as a music player: later.
- **A shortcut for Show all:** later, with shortcuts as triggers ([#39](https://github.com/Lazyb0y/pc-companion/issues/39)).
- **Hide app and Hide others in the PC Companion icon's menu:** once the menu opens, the taskbar is in front, so there's no app in front to act on.
- **A Hot Corners action:** Hot Corners is in another module; see [#152](https://github.com/Lazyb0y/pc-companion/issues/152).
- **Hiding apps automatically after a time without use, muting hidden apps, or hiding their notification icons,** as some tools do.
- **Windows on other virtual desktops,** which aren't in sight.
- **Staying on the virtual desktop that's showing when Show all brings back windows from another one:** in the Sandbox checks, about 35 ms after Show all was chosen from the PC Companion icon's menu, Windows sometimes gave the front to a window minimized on another virtual desktop, before Hide Apps brought it back, and switched to that desktop. Hide Apps gives the front to a window on the desktop that's showing first, which helped some of the time; the cause is still to be found.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The APIs are documented and in every supported version: `ShowWindowAsync`, `SetWindowPos`, `PostMessage`, `SetForegroundWindow`, `SetWinEventHook`, `RegisterHotKey`, `IsHungAppWindow`, `IsWindowEnabled`, `MonitorFromWindow`, `DwmGetWindowAttribute` and `MessageBeep`.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:** at a press, for the visible top-level windows: their styles, owners, whether they're enabled, minimized and cloaked, their display and class name, and for those the taskbar shows, their app's identity. While windows are in the list, Windows reports each window starting to come back from minimized, and the closing of windows of the processes in the list; while they're being brought back, it reports changes in the order of windows, and PC Companion reads that order. It reads no window titles or contents and receives no keys or clicks: no keyboard or mouse hook.
- **Changes:** other apps' windows: minimizes them, brings them back, orders them and gives the front back. It asks apps running as administrator to minimize and come back when it can't do it itself. It runs no code in other apps, sends them no input and writes no file.
- **Logs:** Debug entries for each press and bringing back: the shortcut or menu item, how many windows went or came back, how many stayed and why, how long it took, and how long putting the windows in order took, with how many were skipped or didn't move in time. Never titles, app IDs, names or paths, which can name documents, profiles or users. Warnings when Windows refuses a shortcut, when a shortcut is unknown or the settings section can't be read, and when Windows doesn't give the front back, and an error when a failure stops the feature.
- **Settings:** the two shortcut choices and two switches, so a changed settings file can only change which shortcuts minimize windows.
- **Network:** none. `PRIVACY.md` gets a line on Hide Apps.
- **Rights:** runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Running as administrator, it also minimizes the windows of apps running as administrator, which the user can minimize anyway. Turning the feature off removes the shortcuts and hooks and leaves every window as it is.

## Performance

- Between presses, nothing runs while the list is empty. While it has windows, the hook reports each window that comes back from minimized, and each process with windows in the list has a hook whose events for its other windows are dropped at once. The order hook is set only while windows are being brought back.
- A press lists the windows once and reads app identity only for windows the taskbar shows: 12.5 ms for 50 windows in the spike, on the feature's thread.
- Bringing back takes a few milliseconds per window, waiting for each app in turn.

## Accessibility and localization

- **Keyboard:** both shortcuts. **Show all hidden windows** is in the PC Companion icon's menu, which Windows + B and the arrow keys reach.
- **Screen readers:** after Hide app, Windows moves the focus to the next window, which screen readers announce. Hide others and bringing windows back leave the focus where it is. Hide Apps announces nothing of its own: the host has no UI Automation provider. The page says what each shortcut does.
- **Colors and layout:** the page and the menu follow the Windows mode, dark or light, and high contrast, and are laid out right to left in right-to-left languages.
- The page works with the keyboard alone, and every switch and list has a label for screen readers.
- The shortcuts' names and the strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake windows, a fake clock and fake Windows calls:
  - which windows go: the app in front's windows, including a browser profile's app ID and a package's app ID, always-on-top windows, windows without a Minimize button, a window with a dialog open, cloaked windows, windows on other displays with the switch on and off, minimized and hidden windows, `Ghost` windows, and nothing in front, each with or without the warning sound;
  - Hide app: the window in front minimized last;
  - Hide others pressed again: the same app in front brings the windows back, another app in front hides its others and keeps the earlier windows for the next press;
  - bringing back: the order, waiting for each move, a move that times out, an app that stopped responding, and apps running as administrator asked by request with the front given back;
  - an app coming back whole: a window that comes back brings back its app's others behind it, and not with the switch off;
  - the list: windows that close, windows the user brings back, and new windows that don't change it;
  - the list shared with Minimize to Tray;
  - the settings: defaults, the spec's example, an unknown shortcut and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/HideApps.ps1`), with test windows the checks start:
  - the Home switch turns the feature on with its defaults, and the page saves both shortcuts and both switches;
  - Hide app, sent by `SendInput`, minimizes both windows of a test app and its palette, keeps their taskbar buttons and brings another window to the front;
  - when the test app restores one of its windows, the other comes back right behind it;
  - Hide others keeps both of the app in front's windows and the focus, minimizes other test apps' windows, Settings and File Explorer's, leaves an always-on-top window and a window without a Minimize button, and the host logs why;
  - pressing Hide others again brings the windows back in their order, behind the app in front, maximized as they were, and the focus stays; the host logs how long putting them in order took; a window opened between the presses doesn't change which come back;
  - a window with a dialog open stays, and the host logs why;
  - with a second virtual desktop, Hide others there leaves the first desktop's windows alone, and Show all brings them back; whether Windows switched to the first desktop is reported, not checked: see [Not included](#not-included);
  - a test window that stops responding doesn't hold the host, as the host's log shows, and comes back once it responds;
  - with the host's file labeled Medium integrity, as the spike did, a test window running as administrator is asked to minimize and come back, and the front goes back to the window that had it;
  - a window of an app listed in Minimize to Tray stays on the taskbar when Hide others minimizes it;
  - ending the host with `Stop-Process` leaves the windows minimized on the taskbar;
  - the **Show all hidden windows** item in the PC Companion icon's menu, with a screenshot, brings back the windows;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** UAC on, the foreground lock, several displays, Windows 10 and Narrator.
- **By hand:**
  - an app running as administrator with UAC on, with and without Always run as administrator, including whether the front goes back;
  - that the front goes back after bringing windows back on a PC, where the foreground lock applies;
  - both shortcuts on a real keyboard, also with an app that uses Windows + Ctrl + H;
  - Hide others only on the display in front, with several displays at different scales;
  - desktop apps in Store packages, such as Notepad and Windows Terminal, a browser with two profiles and an installed web app, Office, and a full-screen game;
  - Narrator after each shortcut, high contrast, a right-to-left language, and the menu item in the Windows mode's colors;
  - a click on a minimized window's taskbar button bringing back its app's other windows;
  - Show all from the PC Companion icon's menu with windows minimized on another virtual desktop, and whether Windows switches to it;
  - Windows 10.
