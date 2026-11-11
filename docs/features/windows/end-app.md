# End App

Press a shortcut, click the window of an app that stopped responding, confirm, and PC Companion ends that app.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.endApp`. Issue: [#34](https://github.com/Lazyb0y/pc-companion/issues/34).
- Inspired by KDE Plasma's Ctrl + Alt + Esc, which ends the app of the window clicked.
- Off until the user turns it on.

## Behavior

- **Ctrl + Alt + Esc,** or **End an app…** in the PC Companion icon's menu, starts choosing an app: the pointer becomes a crosshair, and a red frame shows the window under it.
- **A click on a window, or Enter,** asks first: a confirmation names the app, says whether it's responding and that anything not saved is lost. **End app** ends it.
- **Esc, a right-click or the shortcut again** cancels. While choosing, clicks and keys don't reach the apps.
- **Only that app ends:** the window's process, and the processes it started that run the same program, such as a browser's helpers. Other copies of the program and other programs keep running.
- **Some apps never end,** such as File Explorer, which also runs the desktop and the taskbar, and Windows' own processes. Their frame is gray, and a click says why and offers Task Manager; see [Apps that never end](#apps-that-never-end).
- **Apps running as administrator** end only while PC Companion runs as administrator; see [Apps running as administrator](#apps-running-as-administrator).

## Compared with other systems

- **Windows** offers three ways:
  - **Task Manager** ends an app chosen from its list, which marks apps that aren't responding. A frozen full-screen app can keep it out of sight.
  - **End task** on a taskbar button, on Windows 11 only, is off until turned on in Settings. It asks nothing, ends every process of the app's program, and isn't offered for File Explorer.
  - **Closing a window that stopped responding** offers "Close the program", but only once Windows shows its stand-in for the window, and only through a title bar, which full-screen apps and games don't have.

  End App lets the user point at the window itself, at any time and full screen too, asks first, and ends only that app.
- **KDE Plasma's** Ctrl + Alt + Esc (Meta + Ctrl + Esc since Plasma 6) ends the app of the window clicked, at once, without asking; Esc or a right-click cancels. KWin also asks when a frozen window is closed: Terminate or Wait Longer, and the question closes when the app recovers. End App keeps the pointing and the ways to cancel, asks first, chooses its default by the app's state, and can be used with the keyboard alone.
- **GNOME** asks "is not responding" with Force Quit and Wait. Users report the question taking keys typed into other windows, so that Return forces the app to quit ([mutter#3533](https://gitlab.gnome.org/GNOME/mutter/-/issues/3533)), and that it doesn't warn that work is lost ([gnome-shell#6770](https://gitlab.gnome.org/GNOME/gnome-shell/-/issues/6770)). End App's confirmation shows only after the user chooses an app, says what is lost, and defaults to Cancel while the app responds.
- **macOS** has Force Quit Applications (Option + Command + Esc): a list that marks apps that aren't responding, and a confirmation. No pointing.
- **xkill** on X11 ends the program of the window clicked, without asking.
- **Tools for Windows** that end the app in front with a shortcut, launchers that end a window's app chosen from a search list, and process managers:
  - a tool that ends the app in front ended Explorer, and with it the desktop and the taskbar, until it got a list of exceptions;
  - some launchers end without asking, and users report errors for apps running as administrator.

  End App never ends Windows' own processes, says why it can't end an app, and ends only that app's processes.
- **Tools that end a process by its ID** can end another process once Windows reuses the ID. End App holds on to the process from the moment the user chooses it until it ends.

## Choosing an app

### Starting and stopping

- **The shortcut,** or **End an app…** in the PC Companion icon's menu, while the feature is on.
- **PC Companion's own window over every display:** invisible, always on top, off the taskbar and out of Alt + Tab (`WS_EX_NOREDIRECTIONBITMAP`, `WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW`). It takes the front, which Windows allows right after a shortcut or a menu choice. While it's there, clicks and keys go to it, not to the apps, with no mouse or keyboard hook.
- **Choosing stops** when an app is chosen, on Esc, a right-click or the shortcut, when the window loses the front, as for Start, Alt + Tab, Ctrl + Alt + Del or locking the PC, when the displays change, and after 30 seconds without a pointer move or a key. So the invisible window never stays and keeps clicks from the apps.

### The window under the pointer

- **Front to back:** the first visible top-level window of another program whose visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`) holds the pointer, in the order `EnumWindows` gives. `WindowFromPoint` would find PC Companion's own window, which covers everything.
- **Skipped:** PC Companion's own windows, windows on other virtual desktops (cloaked), and windows that let clicks through (`WS_EX_LAYERED` with `WS_EX_TRANSPARENT`).
- **Then its main window:** the window at the end of its chain of owners (`TopLevelWindows.RootOwner`), such as a dialog's main window.
- **Windows' stand-in for a window that stopped responding** (class `Ghost`, in Desktop Window Manager's process) stands for the window behind it that stopped responding (`IsHungAppWindow`) with the same bounds (`GetWindowRect`). With none or several such windows, End App can't tell which app it is, and says so. The undocumented `HungWindowFromGhostWindow` isn't used.
- **The desktop and the taskbar** are File Explorer's windows: gray frame.

### Keys and clicks

- **A click, Enter or Space** chooses the framed window's app.
- **Tab and Shift + Tab** move the frame to the next and previous window that the taskbar shows (`TopLevelWindows.MayHaveTaskbarButton`), other than the desktop and the taskbar, front to back, on every display. Moving the pointer frames the window under it again.
- **Esc, a right-click or the shortcut** cancels.
- **The frame starts on the window in front.** After the shortcut, that's usually the app that stopped responding, so Ctrl + Alt + Esc, Enter, Enter ends it. From the menu, it starts on the first window Alt + Tab would show.

### The frame and the pointer

- **The frame:** 4 effective pixels, at the window's DPI, just outside the window's visible frame, drawn by four windows of PC Companion's own that let clicks through (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`). It has square corners; Windows 11's rounded corners stay inside it.
- **Its color:** red (`#E81123`) for an app that can be ended, gray (`#8A8A8A`) for one that can't. In high contrast, the theme's Highlight and Gray Text colors.
- **The pointer:** Windows' crosshair (`IDC_CROSS`) over an app that can be ended, and its Unavailable pointer (`IDC_NO`) over one that can't, so the frame's color isn't the only sign. They follow the user's pointer scheme and size.

## Which processes end

### The app's processes

- **The app's process:**
  - the chosen window's process (`GetWindowThreadProcessId`);
  - for Windows' stand-in, the process of the window that stopped responding;
  - for a Store app in Windows' shared frame (`ApplicationFrameWindow`), such as Settings, the process of its core window (`Windows.UI.Core.CoreWindow`), never `ApplicationFrameHost.exe`, which runs every such app;
  - for a console window, the console program that Windows reports for it; its console host ends with it.
- **Its helpers:** the processes it started that run the same program file, and theirs, such as the helper processes of browsers and Electron apps, which otherwise keep running (spike). A process counts when its parent's process ID is the app's and it started after the app (`GetProcessTimes`), so a reused process ID doesn't.
- **Never:** other processes with the same program, such as another copy the user started, and processes of other programs that the app started, such as a terminal's shells or apps a launcher started: they may hold other work.
- **Holding on:** as the app is chosen, PC Companion opens its process (`PROCESS_TERMINATE`, `SYNCHRONIZE` and `PROCESS_QUERY_LIMITED_INFORMATION`) and keeps it open until it ends, so a process ID reused meanwhile can't be hit. When Windows refuses, the app can't be ended, and End App says why.

### Apps that never end

- **PC Companion's host.**
- **File Explorer** (`explorer.exe` in the Windows folder), whose process also runs the desktop and the taskbar. Its folder windows can run in a process of their own (spike), which doesn't run the desktop; they're left alone too, as one rule for File Explorer. The explanation points to Task Manager's Restart.
- **Windows' own processes behind the desktop,** a fixed list by program file in the Windows folder: `dwm.exe`, `ApplicationFrameHost.exe`, `conhost.exe`, `sihost.exe`, `ctfmon.exe`, `ShellExperienceHost.exe`, `StartMenuExperienceHost.exe`, `SearchHost.exe`, `SearchApp.exe`, `TextInputHost.exe`, `LockApp.exe`, `taskhostw.exe`, `RuntimeBroker.exe`, `svchost.exe`, `fontdrvhost.exe`, `csrss.exe`, `winlogon.exe`, `wininit.exe`, `services.exe`, `lsass.exe` and `smss.exe`.
- **Processes Windows marks critical** (`IsProcessCritical`). Windows marks only a few, such as `csrss.exe` and `wininit.exe`, and not `dwm.exe`, `explorer.exe`, `winlogon.exe` or `lsass.exe` (spike), so the list is needed.
- **Processes Windows doesn't let PC Companion open:** other users' apps, protected processes such as some games' anti-cheat software, and apps running as administrator while PC Companion doesn't.
- Each gets the gray frame and the Unavailable pointer, and choosing it explains why; see [The confirmation](#the-confirmation).

### Apps running as administrator

- **Without admin rights,** with UAC on, Windows refuses PC Companion the right to end them. End App reads whether the app runs as administrator (`Elevation.IsProcessElevated`) to explain: "Notepad runs as administrator, so PC Companion can't end it.", with **Open Task Manager**.
- **With Always run as administrator, or with UAC off,** Windows grants the right, and they end like any other app.
- **What Windows grants decides,** not a comparison of rights: in the spike, a copy labeled Medium integrity was granted the right, since its token kept the Administrators group, which UAC takes away. So the refusal is tested by hand with UAC on.
- **The page's note** about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.

## The confirmation

- **When:** after the user chooses an app that can be ended. The thread pool first reads the app's name, its icon and its processes; meanwhile the frame stays.
- **How:** a Windows task dialog on a thread of its own, as Display Profiles' question is, centered on the display of the app's window and in front of other windows. It takes the front from PC Companion's covering window, which then closes (spike).
- **What it says:**
  - **Title:** End App. **Heading:** "End Notepad?", with the app's icon. The app's name is the one Windows shows: its name in Start's Apps folder for an app ID, otherwise its program's description (`AppDisplay`).
  - **Not responding** (`IsHungAppWindow` on the app's window): "Notepad isn't responding. Anything not saved in it will be lost." **End app** is the default.
  - **Responding:** "Notepad is responding. To keep your work, close it as usual. Ending it loses anything not saved." **Cancel** is the default.
  - **Below:** the program file and how many processes end, such as "Ends 12 processes of msedge.exe."
- **While it's open,** it looks every half second:
  - **The app's state changes:** the dialog changes to the other text and default (`TDM_NAVIGATE_PAGE`, spike).
  - **The app exits:** the dialog closes, and nothing more happens.
- **Esc, Cancel or closing it** ends nothing.
- **There's no setting to skip it.**
- **An app that can't be ended:** the same kind of dialog says "PC Companion can't end Notepad" and why: it runs as administrator; it's part of Windows; it's File Explorer, which also runs the desktop and the taskbar, so restart it in Task Manager; Windows doesn't let PC Companion end it; or two windows that stopped responding are at the same place. **Open Task Manager** and **Close**, the default.
- **Open Task Manager** opens Task Manager as System Meters does (`ShellActions.OpenTaskManager`): through the shell and without arguments, so that Windows gives it admin rights for an administrator's account without asking, as it does from the taskbar. To be checked by hand with UAC on.

## Ending

- **End app** ends the app's process (`TerminateProcess` on the process it holds, exit code 1), then lists its helpers again at that moment and ends each the same way, after checking its parent and start time.
- **Windows ends a process asynchronously,** so the thread pool waits up to 5 seconds for each. In the spike, test programs exited 11 to 14 ms after the call, whether they responded or not, and their windows and Windows' stand-ins went at once.
- **An app still running after 5 seconds:** a notification says "Couldn't end Notepad." (`IHostNotifications`), and a warning is logged.
- **Nothing is asked of the app first,** such as `WM_CLOSE`: a frozen app can't answer, and the confirmation tells the user to close a responding one as usual.
- **No crash report:** Windows Error Reporting isn't involved, and no Windows dialog appeared (spike).

## The shortcut and the menu item

- **Ctrl + Alt + Esc,** KDE Plasma's long-standing shortcut for the same, when the feature is first turned on. **Windows + Ctrl + Esc,** Plasma 6's, is the other choice, or **None**.
- **Registration:** `RegisterHotKey` with `MOD_NOREPEAT`, so Windows tells PC Companion only when the shortcut is pressed. No keyboard hook.
- **Why these:** Ctrl + Shift + Esc opens Task Manager and Ctrl + Esc opens Start, while Ctrl + Alt + Esc and Windows + Ctrl + Esc aren't in Windows' list of keyboard shortcuts. Windows let a program register both on build 26300, and Ctrl + Alt + Esc on build 26100 (spike). Ctrl + Alt is AltGr on many keyboard layouts, but AltGr + Esc types nothing.
- **Ctrl and Alt reach the app in front** before Esc, as for any shortcut (spike). Their release goes to the covering window, which has the focus by then, so the app's menu bar doesn't open.
- **While an app running as administrator is in front,** Windows still reports the shortcut, as for Hide Apps, and the covering window takes the front (spike).
- **A shortcut another program has:** Windows refuses it, and a warning is logged. PC Companion tries again after any change to the settings. The page shows a warning note at the shortcut, which goes away once Windows takes it; see [Running in the host](../../architecture/features.md#running-in-the-host).
- **End an app…** is in the PC Companion icon's menu above Settings and Exit while the feature is on (`IHostMenuItems`). Choosing it starts choosing an app once the menu closes.

## With the other features

- **Windows hidden or minimized** by Minimize to Tray, Drop-down Apps or Hide Apps aren't on screen, so they can't be chosen.
- **Ending an app with such windows** is like the app closing: Minimize to Tray removes their icons, Drop-down Apps forgets the window, and Hide Apps takes them off its list.

## Threads

- **A thread of its own,** derived from `MessageLoopThread<TSettings>`: while the user chooses, every pointer move and key is handled at once, and the shortcut needs a message loop, which the thread pool doesn't have.
- **The host's UI thread** runs only `Apply`, which reads the section and passes it to the feature's thread; the **End an app…** item, answered from the settings; and the notification.
- **The feature's thread** has its own hidden window and runs:
  - the shortcut and the menu item's choice;
  - the covering window, the frame and the pointer;
  - finding the window under the pointer and the Tab order, matching stand-ins, and whether the app responds;
  - the checks in [Apps that never end](#apps-that-never-end): the program file (`QueryFullProcessImageName`), whether it's critical and whether it runs as administrator;
  - opening the app's process.
- **The thread pool** runs:
  - reading the app's name and icon, which the shell can take a while to give;
  - listing the processes for its helpers, about 25 ms;
  - ending the processes and waiting for them;
  - starting Task Manager.

  Each passes its result back to the feature's thread with `MessageWindow.BeginInvoke`.
- **The dialog's own thread** shows the confirmation, as Display Profiles' question has one.
- **Nothing waits for the app:** End App sends it no message. Reading windows' places, classes, owners and states, and whether they respond, sends no message that waits.
- **Stopping** removes the shortcut, closes the covering window, the frame and any dialog as if canceled, and ends the thread.
- **Failures:** a failure on the feature's thread is logged and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), at 100% scale on one display, with input sent by `SendInput`, confirmed the following. It ended only test programs it started, and compared the running processes before and after each, as the Sandbox checks will.

- **The covering window,** with `WS_EX_NOREDIRECTIONBITMAP` and with `WS_EX_LAYERED` at alpha 1 alike:
  - it showed and took the front 9 to 38 ms after the shortcut, also from a copy labeled Medium integrity while a test window running as administrator was in front;
  - it was invisible, and `WindowFromPoint` found it everywhere;
  - it set the crosshair over test windows and the Unavailable pointer over the desktop;
  - it got the left and right clicks, Tab, Shift + Tab, Enter and Esc, and the windows under it got none of them, only the shortcut's Ctrl and Alt, before it showed;
  - the frame showed around the window, and clicks went through it.
- **Choosing:** front to back, it found the test windows, the desktop (`Progman`) and the taskbar (`Shell_TrayWnd`), both File Explorer's.
- **Windows' stand-in:**
  - it came 5.1 to 5.2 seconds after a window in front stopped responding, and for another only after more than 8 seconds;
  - it's of class `Ghost`, in `dwm.exe`, titled with " (Not Responding)", not owned, and takes the front; `IsHungAppWindow` is true for it;
  - its bounds were exactly those of the window that stopped responding, which stayed visible to `IsWindowVisible`, behind it, and matching by bounds found that window, as the undocumented `HungWindowFromGhostWindow` did.
- **Ending:** test programs, frozen or responding, exited 11 to 14 ms after `TerminateProcess`; their windows and stand-ins went at once, and no window of Windows' appeared. Nothing else ended.
- **Helpers:** a test program's two helpers of the same program and one of another program kept running after it ended. Edge's window belonged to its browser process, which had 11 descendants of the same program: all 12 of its `msedge.exe` processes. Its `identity_helper.exe` would keep running.
- **A console window:** Windows reported the console program as its process, and ending it ended its console host and closed the window.
- **Settings,** in the shared frame, mapped to `SystemSettings.exe` through its core window.
- **File Explorer:** a folder window opened from a program ran in an `explorer.exe` of its own, not the shell's.
- **Critical processes:** `csrss.exe`, `services.exe`, `smss.exe` and `wininit.exe` were; `winlogon.exe`, `lsass.exe`, `dwm.exe`, `explorer.exe`, `sihost.exe` and the shell's hosts weren't.
- **The dialog,** a task dialog on its own thread, took the front from the covering window. With End app as the default, Enter answered End app; after it changed while open to Cancel as the default, Enter answered Cancel; Esc answered Cancel.
- **Apps running as administrator:** the copy labeled Medium integrity was granted `PROCESS_TERMINATE` on the test window running as administrator, since its token kept the Administrators group. It ended nothing with it.
- **Simulated input and stand-ins:** while a stand-in was in front, the shortcut sent by `SendInput` never reached the spike, also from a program running as administrator, most likely because the stand-in belongs to a system process. A real keyboard isn't held back that way, which is tested by hand.
- **Not checked yet:** two frozen windows at the same place; a real keyboard; UAC on; Open Task Manager; several displays and scales; high contrast; Narrator; full-screen games; Windows 10.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Shortcut**, Ctrl + Alt + Esc, Windows + Ctrl + Esc or None;
  - **Choosing an app**, a card with the keys: click or Enter chooses, Tab moves the frame, Esc or a right-click cancels, and **End an app…** in the PC Companion icon's menu does the same;
  - a note about the apps that can't be ended, including apps running as administrator;
  - a **Permissions & data** card.
- **When the feature is first turned on,** the shortcut is Ctrl + Alt + Esc.
- **The switch on Home** changes only `enabled` and keeps the other settings.
- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log. An unknown shortcut registers none, with a warning.
- **The icon** is drawn as [Icons](../../design/icons.md) says: a violet app window with a red stop sign at its bottom right.

```json
"features": {
  "endApp": {
    "enabled": true,
    "shortcut": "ControlAltEscape"
  }
}
```

- `shortcut`: `ControlAltEscape`, `WindowsControlEscape` or `None`.

## Not included

- **Ending every process of a program,** or the processes of other programs that an app started.
- **End as administrator,** through the elevated helper with a UAC prompt: it would need a new elevated command that ends processes, with a security review of its own. Task Manager does it meanwhile.
- **Asking the app to close first, or a Wait button:** closing as usual asks the app, and Cancel waits.
- **Skipping the confirmation.**
- **Starting the app again** after ending it.
- **A Hot Corners action:** Hot Corners is in another module; see [#152](https://github.com/Lazyb0y/pc-companion/issues/152).
- **Telling apart two frozen windows at the same place,** which only the undocumented `HungWindowFromGhostWindow` could do. End App says it can't tell which.
- **A frame with rounded corners.**
- **Apps of other users or other sessions.**
- **Crash or hang reports** of the app.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - windows: `CreateWindowEx` with `WS_EX_NOREDIRECTIONBITMAP` (Windows 8 and later), `SetForegroundWindow`, `EnumWindows`, `GetAncestor`, `GetWindowRect`, `DwmGetWindowAttribute`, `IsHungAppWindow`, `GetWindowThreadProcessId`, `SetLayeredWindowAttributes`, `SetCursor` and `LoadCursor`;
  - input: `RegisterHotKey`;
  - processes: `OpenProcess`, `TerminateProcess`, `WaitForSingleObject`, `GetProcessTimes`, `QueryFullProcessImageName`, `IsProcessCritical` (Windows 8.1 and later), `CreateToolhelp32Snapshot` and `GetTokenInformation`;
  - dialogs: `TaskDialogIndirect`.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads,** only while the user chooses an app:
  - the visible top-level windows' places, classes, styles, owners and processes, and whether they respond;
  - the pointer and the keys, only through PC Companion's own window while it covers the screen: no mouse or keyboard hook.

  For the chosen app: its name, icon, program file and rights, and the processes it started, from the list of running processes with each one's parent, program file and start time. It reads no window titles or contents.
- **Changes:** it ends the chosen app's processes after the user confirms, and nothing else. It starts Task Manager when asked. It writes no file and sends no input to other apps.
- **Process rights:** it opens only the chosen app's processes, with the rights to end them, wait for them and read their basic details, from the choice until they end.
- **Safety:**
  - It never ends PC Companion's host, File Explorer, Windows' own processes or critical processes, and ends only after the user chooses an app and confirms.
  - A program that could drive PC Companion's window with simulated input could end the app itself with the same rights. Windows keeps programs with fewer rights from sending input to it.
- **Settings:** a changed settings file can only choose the shortcut.
- **Logs:**
  - **Debug entries:** choosing started, by the shortcut or the menu, and stopped, and why; whether the chosen app responded; why it can't be ended; how many processes ended and how long it took.
  - **Never** titles, app names, app IDs, paths or process IDs, which can name documents, profiles or users.
  - **Warnings** when Windows refuses the shortcut, when the shortcut is unknown or the section can't be read, and when an app is still running after 5 seconds.
  - **An error** when a failure stops the feature.
- **Network:** none. `PRIVACY.md` gets a line on End App.
- **Rights:** it runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Running as administrator, it can also end apps running as administrator, as Task Manager running as administrator can. Turning the feature off removes the shortcut and closes anything it shows.

## Performance

- **Between uses,** nothing runs; only the shortcut is registered.
- **While choosing,** each pointer move lists the top-level windows front to back and reads each one's frame until one holds the pointer, on the feature's thread. The Sandbox checks measure it.
- **Ending** took 11 to 14 ms per test program in the spike; listing the processes for helpers takes about 25 ms, on the thread pool.

## Accessibility and localization

- **Keyboard:**
  - the shortcut, then Tab, Shift + Tab, Enter and Esc, so no mouse is needed;
  - **End an app…** in the PC Companion icon's menu, which Windows + B and the arrow keys reach;
  - the dialog, with the default button chosen by the app's state.
- **Screen readers:**
  - The covering window's name, which screen readers read as it takes the focus, is "End App: choose an app to end", followed by the framed app's name and state, such as "Notepad, not responding". It changes with each Tab (`EVENT_OBJECT_NAMECHANGE`); whether Narrator reads the change is checked by hand.
  - Screen readers read the dialog's heading, text and buttons, as for Display Profiles' question.
  - End App announces nothing else: the host has no UI Automation provider.
- **Colors and layout:**
  - The frame is red or gray, in high contrast the theme's colors, and the pointer's shape also says whether the app can be ended.
  - Windows draws task dialogs light, also in dark mode. The dialog uses the high contrast colors and is laid out right to left in right-to-left languages.
  - The page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages.
- **Motion:** none.
- **Strings:** translated like the other pages', marked "Needs native review." App names are the ones Windows shows, in the user's language where the app has one.

## Testing

- **Unit tests,** with fake windows, fake processes, a fake clock and fake Windows calls:
  - **choosing:** front to back, skipping PC Companion's own, cloaked and click-through windows, the main window of a dialog, a stand-in with none, one and two windows of its bounds, where the frame starts, and the Tab order;
  - **stopping:** Esc, a right-click, the shortcut, losing the front, a change of displays and 30 seconds without input;
  - **apps that never end:** the host, File Explorer, each listed program in the Windows folder and the same name elsewhere, critical processes, and a process Windows refuses, each with its explanation;
  - **the app's processes:** the shared frame's core window, a console program, helpers by parent and start time, a reused process ID, other programs' processes and other copies left running;
  - **the confirmation:** responding and not, each default, the switch when the state changes, and closing when the app exits;
  - **ending:** the held process, helpers checked again, the 5-second wait and its notification;
  - **the settings:** defaults, the spec's example, an unknown shortcut and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/EndApp.ps1`), with test programs the checks build and start:
  - **Safety:**
    - The checks end only test programs they started. They confirm **End app** only in a dialog that names one of them; any other dialog is canceled, and the check fails.
    - Each check compares the running processes before and after, and fails and stops the group when any other process with a window, or one of Windows' own, is gone.
  - **the page:** the Home switch turns the feature on with its defaults, and the page saves the shortcut; with a test program holding Ctrl + Alt + Esc, the page shows its note, until the program lets it go and a setting changes;
  - **choosing:**
    - the shortcut, with a responding test window in front, and the menu item start choosing;
    - clicks and keys don't reach the test windows, and the pointer is the crosshair or Unavailable;
    - the frame follows the pointer and Tab, with screenshots;
    - Esc, a right-click, the shortcut and Alt + Tab stop it, and no window of PC Companion's is left;
  - **a frozen test window,** chosen through the menu item, since simulated input doesn't reach PC Companion while a stand-in is in front: its stand-in maps to it, the dialog says it isn't responding with End app as the default, and Enter ends it and its two helpers of the same program, while its helper of another program keeps running;
  - **a responding test window:** Cancel is the default, and Enter ends nothing;
  - **the state changing:** a test window that responds again while asked changes the dialog to Cancel as the default, and one that exits while asked closes it;
  - **a console test program** ends, with its console window;
  - **Settings,** in the shared frame: the dialog names Settings, and the check cancels;
  - **File Explorer:** the desktop, the taskbar and a folder window get the gray frame and the explanation, and File Explorer still runs after **Close**;
  - **the page** in English and Arabic, in light and dark, with screenshots;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** a real keyboard, also with a stand-in in front; UAC on, so neither the refusal for apps running as administrator nor Task Manager's admin rights; several displays and scales; high contrast; Narrator; full-screen games; Windows 10.
- **By hand:**
  - Ctrl + Alt + Esc on a real keyboard with a frozen app's stand-in in front, and with a full-screen app or game that stopped responding;
  - an app running as administrator with UAC on: the gray frame, the explanation and **Open Task Manager**, for an administrator's and a standard account; and with Always run as administrator, ending it;
  - browsers, Office, Windows Terminal and a Store app that stop responding, and their helpers ending with them;
  - several displays at different scales, and the frame at 125% to 200%;
  - high contrast, Narrator while choosing and in the dialog, and a right-to-left language;
  - Windows 10.
