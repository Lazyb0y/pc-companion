# Drop-down Apps

Slide a chosen app's window down from the top of the screen with a shortcut, and back up out of the way with the same shortcut, as drop-down terminals do, for any app.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.dropDownApps`. Issue: [#33](https://github.com/Lazyb0y/pc-companion/issues/33).
- Inspired by Yakuake and the i3 scratchpad.
- Off until the user turns it on.

## Behavior

- The user lists apps on the feature's page and gives each its own shortcut, such as **Windows + Ctrl + the key above Tab** for a terminal and **Windows + Ctrl + Alt + 1** for a notes app.
- **Pressing an app's shortcut** slides its window down from the top of the display with the pointer, across the width and down to the height the page sets, and the window takes the focus. Pressing it again slides the window back up, hides it, and gives the focus back to the window that had it.
- **When the window is shown but another window is in front,** the shortcut brings it to the front instead; when it's on another virtual desktop, it brings it to this one.
- **When the app isn't running,** the shortcut starts it and drops down its first window.
- **While it's hidden,** the window is off the taskbar and out of Alt + Tab. While it's shown, it's an ordinary window of its app.
- **It hides when another app comes to the front,** unless the page says otherwise, and can stay on top while it's shown.
- No window is ever lost: whatever happens to PC Companion, a hidden window comes back on the taskbar; see [Never losing a window](#never-losing-a-window).
- Some windows can't drop down, such as those of apps running as administrator while PC Companion doesn't. Windows' warning sound plays, and the page says which and why; see [Windows that can't drop down](#windows-that-cant-drop-down) and [Recent results](#recent-results).

## Compared with other systems

- **Windows** has Windows Terminal's quake mode, for Terminal's own window only ([docs](https://learn.microsoft.com/en-us/windows/terminal/tips-and-tricks#quake-mode)):
  - **Fixed size:** the window always covers the full width and the top half of the display. Its height has no setting ([#11162](https://github.com/microsoft/terminal/issues/11162)), a dragged height isn't remembered ([#12594](https://github.com/microsoft/terminal/issues/12594)), and its width can't change ([#11174](https://github.com/microsoft/terminal/issues/11174)).
  - **Hiding on focus loss** is one setting for every Terminal window ([#9992](https://github.com/microsoft/terminal/issues/9992)), and the window never stays on top.
  - **Its shortcut** works only while Terminal runs ([#9996](https://github.com/microsoft/terminal/issues/9996)), and its key is mapped once, so it doesn't follow a switch of keyboard layout.
  - **Users report** the slide tearing ([#12293](https://github.com/microsoft/terminal/issues/12293)) and the focus not coming back after the window hides ([#10450](https://github.com/microsoft/terminal/issues/10450)).
  - Drop-down Apps works for any app, several apps each with its own shortcut. It starts the app, sizes the window to the current work area at each show, and lets the user choose hiding on focus loss, keeping on top and the display. It gives the focus back, and its shortcuts follow the keyboard layout.
- **Tools for Windows that drop down any app** hide the window by moving it off the screen. Users report:
  - windows left off-screen after the tool hung or ended;
  - a slide that crosses a display stacked above;
  - apps running as administrator refused without a word;
  - Store apps that keep their taskbar button.

  Drop-down Apps hides the window and marks it, never puts it off the screen, doesn't slide across another display, and says on the page why an app can't drop down.
- **macOS:** iTerm2's hotkey window drops down its own terminal and hides when another app is chosen, unless it's pinned. Tools that do it for any app only hide and show the whole app, without a slide, a size or a place.
- **Linux:**
  - **Yakuake, Guake and Tilda** drop down their own terminal. They use the display with the pointer, set the width and height in percent, keep the window on top and off the taskbar, and focus the window instead of hiding it when it's open but not in front.
  - **The i3 and Sway scratchpad** hides any window and brings it back centered, with no animation and no starting of apps.
  - **A GNOME extension** drops down any app with its own shortcut. But it minimizes the window, so it stays in the dock, uses a fixed display, and doesn't give the focus back.
  - **Their users report** the focus going to the wrong window after a hide ([Guake #1816](https://github.com/Guake/guake/issues/1816)), hiding on focus loss set off by input method popups ([Guake #1643](https://github.com/Guake/guake/issues/1643)), the window on the wrong display ([KDE 445158](https://bugs.kde.org/show_bug.cgi?id=445158)), and apps that open their window from another process.
  - Drop-down Apps keeps what each does well:
    - the display with the pointer, read at the press;
    - sizes in percent;
    - focusing instead of hiding;
    - starting the app;
    - a shortcut for each app.

    It also gives the focus back to the window that had it, leaves input method and shell popups alone when it hides on focus loss, and matches windows by app identity, so launchers and apps that hand over to a running copy work.

## Which window drops down

### The apps

- **Apps that drop down,** a list on the page, as Hot Corners and Minimize to Tray list apps: **Running app** lists the apps with windows on the taskbar, **Program file** picks a program with the Open dialog, and **Remove** takes one out.
- **What counts as one app,** as the taskbar groups them, with the same code as Hide Apps, Minimize to Tray and Hot Corners (`AppOfWindow`): the app ID the window sets for itself, as browsers do for each profile and installed web app; otherwise its Store app package's app ID; otherwise its program file. So each browser profile and installed web app can drop down on its own.

### Finding the window

- Each app has at most one drop-down window. Once a window has dropped down, it stays the app's drop-down window until it closes.
- **When the app has none yet,** the shortcut takes the app's front-most window that the taskbar shows (`TopLevelWindows.MayHaveTaskbarButton`). That's the one the user used last.
- The app's minimized windows, its windows on other virtual desktops and its windows in Minimize to Tray's tray count too.
- The app's other windows are left as they are.

### Starting the app

- When the app has no such window, the shortcut starts it, with the user's rights, never with admin rights, as Hot Corners starts programs:
  - **A program file** starts in its own folder, without arguments (`IHostCommands.StartProgram`). A program that's missing, or isn't an `.exe`, isn't started.
  - **An app ID** opens through Start's Apps folder (`shell:AppsFolder\<app ID>`), with a new `IHostCommands.StartApp`. It never runs with admin rights either: a host running as administrator has its copy without them open it, as for programs. The app ID is first looked up in the Apps folder (`SHCreateItemFromParsingName`): Windows opens File Explorer at Documents, without an error, for an app ID that isn't there, as the spike showed. One that isn't found isn't opened.
- The first window of the app that the taskbar shows and that isn't cloaked, within 30 seconds, drops down. A Store app's frame first shows cloaked and small, as the spike showed. The window can show at its own place for a moment before it's taken.
- Windows are matched by app identity, not by process, so an app started through a launcher, or one that hands the start over to its running copy, is found.
- Pressing the shortcut again while the app starts doesn't start it again. When no window comes within 30 seconds, nothing drops down, and a recent result says so.
- An app that hid its own window, such as one that closes to its notification icon, usually shows that window when started again, and that window then drops down. Drop-down Apps never shows a window it didn't hide.

### When the window closes

- Its app has no drop-down window any more. The next press takes another of its windows or starts it again.
- **The app restarts:** its new window is found by app identity at the next press.

### Windows that can't drop down

- **Apps running as administrator,** while PC Companion runs without admin rights: Windows refuses to move, hide or mark their windows for a program without admin rights, as the spike showed. With Always run as administrator, or with UAC off, they drop down like any other.
- **Store apps in Windows' shared app frame** (`ApplicationFrameWindow`, in `ApplicationFrameHost.exe`), such as Settings:
  - A hidden frame keeps its taskbar button.
  - Minimized and hidden, Windows shows it again within seconds (spike).
  - Store apps that run as desktop apps, such as Windows Terminal and most new apps, have ordinary windows and drop down.
- **A window with a dialog open,** such as a Save dialog, recognized by the window being disabled (`IsWindowEnabled`): hiding a Windows Forms dialog cancels it, as minimizing its window does, and an app can't be told apart in advance. The window stays where it is.
- **The desktop, the taskbar and the shell's other windows,** which the taskbar doesn't show.
- **The warning sound:** when a press can't show, hide or start the app's window, Windows' warning sound plays (`MessageBeep` with `MB_ICONEXCLAMATION`), and a recent result says why.
- The page lists what can't drop down and why. Its note about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.
- Each case is logged at Debug level with its reason, never with the app.

## The shortcuts

- **Each app on the list** has a shortcut, chosen in its row:
  - **Windows + Ctrl + the key above Tab**
  - **Windows + Alt + the key above Tab**
  - **Windows + Ctrl + Alt + 1** to **9**
  - **None**
- **A shortcut belongs to one app:** choosing one that another app has leaves that app with None.
- **An app that's added** gets the first shortcut no other app has, so the first app gets Windows + Ctrl + the key above Tab.
- **Registration:** with `RegisterHotKey` and `MOD_NOREPEAT`, so Windows tells PC Companion only when a shortcut is pressed. No keyboard hook.
- **By key position:**
  - The key above Tab and the number keys are found by their position, on every keyboard layout: scan code `0x29` and `0x02` to `0x0A`, mapped to their virtual key in the layout of the window in front (`MapVirtualKeyEx`).
  - Each shortcut is registered again when the layout changes, as App Windows' shortcut is (`ShortcutKeeper`, made to register a shortcut without a Shift version).
  - The page shows the key above Tab with its character in the user's layout, such as "Windows + Ctrl + `` ` ``".
- **Why these shortcuts:**
  - Windows Terminal's quake mode uses Windows + the key above Tab, which is also App Windows' second shortcut, so it isn't offered.
  - Windows uses Windows + Ctrl and Windows + Alt with the numbers for its taskbar buttons, and Ctrl + Alt is AltGr on many layouts, so the numbers come with all three keys.
  - In the spike, Windows let a program register every shortcut on the list on Windows 11 build 26100. Pressing them reached it in 1 to 4 ms and opened nothing of Windows'.
- When another program already has a shortcut, Windows refuses it, a warning is logged and a recent result says so. PC Companion tries again after any change to the settings. A warning note below the app list names the shortcut and its app, such as "Another program uses Windows + Ctrl + `, so the shortcut for Terminal does nothing here.", until Windows takes it; see [Running in the host](../../architecture/features.md#running-in-the-host).

## Showing

### Where

- **The display:** the one with the pointer when the shortcut is pressed (`GetCursorPos`, `MonitorFromPoint`), or the main display, a choice on the page.
- **The place:**
  - At the top of that display's work area, so it never covers the taskbar or a bar docked at the top.
  - **Width** and **Height** on the page, as percentages of the work area. The width is 100% and the height 50% when the feature is first turned on.
  - Centered across the work area's width, from left to right whatever the language's direction.
- **The window's visible frame** fills that place: Windows draws invisible borders for resizing outside it (`DWMWA_EXTENDED_FRAME_BOUNDS`), 7 pixels left, right and below at 100% scale in the spike, and they're placed outside the work area.
- **Recomputed at each show,** from the work area as it is then, so a change of displays, taskbar or scale is followed.
- **A size the user drags** while the window is shown is kept for that app, as a share of the work area, until PC Companion exits. The page's sizes stay as they are.
- **An app that won't be smaller** than its minimum size keeps it: the window is centered again from its actual size, as Windows enforces the minimum (spike).
- **Another display scale:** when the display has another scale than the window had, the app may resize the window as it moves (`WM_DPICHANGED`), so its size is set again once the move is done: 100 ms after the window is in place, its size is checked, set again if the app changed it, and checked once more 100 ms later.

### Sliding in

1. **Placed while hidden:** the hidden window is moved just above the top of the work area, at its full size, with `SetWindowPos` and `SWP_ASYNCWINDOWPOS`. The spike saw it shown afterwards at the new place, never at the old one.
2. **Shown and activated:** it's shown without taking the front (`ShowWindowAsync` with `SW_SHOWNA`), then brought to the front with its open dialog, if any (`SetForegroundWindow` on `GetLastActivePopup`), as the slide starts. Windows allows this right after the user pressed one of PC Companion's shortcuts. Keys typed during the slide reach the window, as the spike showed.
3. **Slides:** the window moves down to its place over about 150 ms, quickly at first and slowing at the end.
   - **One move per frame** that Windows draws (`DwmFlush`), each with `SetWindowPos` and `SWP_ASYNCWINDOWPOS`.
   - **A slow app isn't sent more moves:** while the window hasn't reached the last move, the next one isn't sent, for up to two frames. In the spike, an app that took 80 ms per move finished in about 290 ms this way, and in about 485 ms without it.
   - **The slide always ends on time:** the last move goes to the window's place. A window whose app puts it elsewhere stays where its app put it, and one whose app stopped responding goes there when the app responds.
4. **Its owned windows,** such as palettes, are shown again once the slide ends, at their own place.

- **No slide:**
  - when Windows' animation effects are off (Accessibility > Visual effects, `SPI_GETCLIENTAREAANIMATION`);
  - when **Slide in and out** is off on the page;
  - when another display touches the top edge of the display the window shows on, since a slide would cross it.

  Then the window is shown at its place at once, and hidden at once.
- **Minimized or maximized windows:** a hidden window that's minimized or maximized, such as one taken from the taskbar or maximized by the user, is shown with `SetWindowPlacement` instead.
  - **How:** its normal place is set just above the work area, and it's shown without taking the front (`SW_SHOWNOACTIVATE`). Moving a hidden maximized window leaves it maximized, and restoring it first shows it for a moment at its old place, as the spike showed.
  - **On the thread pool:** `SetWindowPlacement` waits for the app (4.7 seconds for one that stopped responding, in the spike), so it runs there and the slide starts when it returns.
  - **Its invisible borders** aren't known while it's minimized, so its size is set again once it shows, as for another display scale.
  - The place and state the window had before it first dropped down are kept, to put it back when the feature lets it go; see [Never losing a window](#never-losing-a-window).

### Keep on top

- **Keep on top while shown,** a switch on the page, off when the feature is first turned on.
- **On:** the window is put on top (`SetWindowPos` with `HWND_TOPMOST` and `SWP_ASYNCWINDOWPOS`) as it shows, and taken off (`HWND_NOTOPMOST`) as it hides.
- A window that's on top by its own app's choice is left on top.
- A second mark records that PC Companion put it there, so a crash doesn't leave it on top; see [Never losing a window](#never-losing-a-window).

## Hiding

### With the shortcut

1. **The front goes back:** as the slide starts, the front goes back to the window that had it before the drop-down window came to the front (`SetForegroundWindow`), so typing goes there at once. This happens if that window still exists and is shown, not minimized and not cloaked. Otherwise Windows chooses, as it does when a window hides. In the spike, Windows chose the window that had the front before too.
2. **Owned windows hide first:** its owned windows, such as palettes, are hidden first and kept in a list. Windows doesn't hide them with their owner, and they don't move with it.
3. **Marked:** the window is marked; see [Never losing a window](#never-losing-a-window).
4. **Slides up and hides:** it slides up above the work area as it slid in, then hides (`ShowWindowAsync` with `SW_HIDE`), and is taken off the top when Keep on top is on.

### When another app comes to the front

- **Hide when another app comes to the front,** a choice on the page:
  - **On any display,** when the feature is first turned on;
  - **Only on its display,** when the window that came to the front is mostly on the drop-down window's display (`MonitorFromWindow`), so the user can work on another display with the drop-down window in sight;
  - **Never.**
- **How it's noticed:** an out-of-context WinEvent hook for `EVENT_SYSTEM_FOREGROUND`, set only while a drop-down window is shown.
- **The decision waits 150 ms,** then reads the window in front again and decides on that one: Alt + Tab first brings hidden helper windows of the shell to the front (spike).
- **Windows that don't count as another app,** from the spike:
  - the drop-down window's own windows: those it owns, such as its dialogs, and other windows of its process;
  - windows that never take the focus (`WS_EX_NOACTIVATE`), such as the emoji panel and Clipboard history;
  - tool windows (`WS_EX_TOOLWINDOW`), such as Quick Settings, the taskbar and the helper windows Alt + Tab shows first;
  - the shell's own top-level windows (class `Windows.UI.Core.CoreWindow`), such as Start, Search and the notification center: they're on top and pass the taskbar's test, so they're recognized by class. Opening an app from Start brings the app to the front, which does hide the drop-down window.
- **The desktop does count:** a click on it hides the window, although the desktop (`Progman`, `WorkerW`) is a tool window.
- **While a mouse button is held,** as in a drag and drop from the drop-down window to another app, the decision waits until it's let go (`GetAsyncKeyState`, checked every 100 ms while it's held).
- **A window with a dialog open** doesn't hide; see [Windows that can't drop down](#windows-that-cant-drop-down).
- **The focus** stays where the user put it, and the window hides as with the shortcut, but without giving the front back.

### Minimized, or shown by someone else

- **Minimized:** when the shown drop-down window is minimized, from its Minimize button, Windows + Down, Windows + M or Show desktop (`EVENT_SYSTEM_MINIMIZESTART`, which Show desktop raised for it in the spike), it's hidden once minimized and counts as hidden, and its shortcut brings it back.
- **Shown by someone else:** when its app shows the hidden window, or another program does (`EVENT_OBJECT_SHOW`), the app wins. The window stays where it shows, its mark is removed, and it counts as shown. The report of a show of Drop-down Apps' own can arrive after a later hide, so the window is checked again 100 ms later, and only one still shown and hidden by Drop-down Apps counts.

## Virtual desktops

- **Shown on the current desktop:** a hidden window shows on the virtual desktop that's showing, as Windows places a window that's shown again. Windows doesn't switch desktops, as Minimize to Tray's spike and this spike showed.
- **Shown on another desktop:** when the shortcut is pressed while the window is shown on another desktop (cloaked), it's hidden, then shown here 100 ms later.
  - **Why the wait:** in the spike, a window shown again at once came back on its old desktop, and bringing it to the front then switched to that desktop. With 50 ms or more in between, it came here every time.
  - **Activated only once here:** the window is brought to the front only once it shows and isn't cloaked, checked every 15 ms for up to 300 ms. When it's still cloaked then, the warning sound plays and a recent result says so.
- **Told apart by cloaking:** a shown window that's cloaked counts as shown on another desktop; Windows' desktop interface (`IVirtualDesktopManager`) isn't needed.
- Moving another program's window to a desktop (`IVirtualDesktopManager::MoveWindowToDesktop`) is refused (Minimize to Tray's spike), so it isn't used.

## Never losing a window

- **The mark:**
  - Before a window hides, PC Companion sets a window property on it (`WindowMark`, named `PCCompanion.DropDownApps`), as Minimize to Tray does, and on its hidden owned windows.
  - The mark is removed when the window shows. It lives as long as the window, so nothing outlasts a restart of Windows, and there's no file to keep right.
  - **When the mark can't be set,** as for an app running as administrator, the window doesn't drop down.
  - A second property (`PCCompanion.DropDownApps.OnTop`) marks a window that PC Companion put on top.
- **When PC Companion exits** for any reason it's told about: **Exit**, turning the feature off, setup closing it for an update or a removal (`WM_CLOSE`), Restart Manager, and the end of the session (`WM_ENDSESSION`). Removing an app from the list does the same for that app's window.
  - **Every hidden drop-down window comes back on the taskbar,** minimized, without taking the front (`ShowWindowAsync` with `SW_SHOWMINNOACTIVE`), at the place and size it had before it first dropped down (`SetWindowPos` with `SWP_ASYNCWINDOWPOS`).
  - **Its owned windows** come back with it.
  - **A window that was maximized then** is set to come back maximized (`SetWindowPlacement` with `WPF_RESTORETOMAXIMIZED`, on the thread pool, within the host's wait).
  - **Shown drop-down windows** stay where they are, taken off the top when PC Companion put them there.
  - **Marks:** they're removed.
  - **The host's wait:** up to 2 seconds for the feature's thread, as for Minimize to Tray.
- **Signing out, restarting and shutting down:** the windows come back on the taskbar when Windows asks whether the session may end (`WM_QUERYENDSESSION`), before their apps are asked to close, so an app's question about unsaved changes shows with its window. The host asks Windows to ask it before other apps (`ShutdownOrder.AskFirst`), as for Minimize to Tray. When the user cancels the sign-out, the windows stay on the taskbar.
- **When PC Companion crashes:** a handler for unhandled exceptions brings the hidden windows back on the taskbar before the host ends, from a list the feature's thread keeps for it, as Minimize to Tray's does. A failure on the feature's own thread does the same. When the host is ended without a chance to run code, for example from Task Manager, the windows stay hidden until it starts again.
- **At the next start,** on or off, the feature brings back on the taskbar, minimized, every hidden window that carries the mark, takes off the top every window marked as put on top, and removes the marks.
- **Never off the screen:** a window is never left above the screen, only on its way in or out. So an app that saves its place when it closes saves a place on the screen.

## Apps running as administrator

- Windows keeps the windows of apps running as administrator away from programs that don't run as administrator. The spike, with a copy labeled to run without admin rights, confirmed that Windows refuses to move, hide, mark or put on top such a window (error 5).
- **So without admin rights:**
  - Such a window can't drop down. The warning sound plays, and a recent result says that the app runs as administrator and that Always run as administrator lets it drop down.
  - Its shortcut still reaches PC Companion while such an app is in front, so other apps' windows still drop down over it.
- An app the user starts with admin rights, or one that always asks for them, isn't started with them by Drop-down Apps. Windows refuses to start a program that needs admin rights without them (`ERROR_ELEVATION_REQUIRED`), and a recent result says so.
- With Always run as administrator, or UAC off, every window drops down. The apps it starts still run without admin rights.

## With Hide Apps and Minimize to Tray

- **Hide Apps** never minimizes a drop-down window:
  - A hidden one isn't shown, so Hide Apps leaves it.
  - **Hide others** leaves a shown one, as it leaves always-on-top windows: the user keeps it in sight.
  - **Hide app** with the drop-down window in front minimizes the app's other windows and leaves the drop-down window, which its own shortcut hides.
- **Minimize to Tray** leaves drop-down windows alone:
  - Minimizing a drop-down window hides it as a drop-down window, also with Shift + Minimize or when its app is on Minimize to Tray's list.
  - **Minimize to Tray's shortcut** plays the warning sound for a drop-down window, and its page lists it among the windows that can't go to the tray.
- **The shared set:** the module keeps the drop-down windows in a set that both features read, as Minimize to Tray reads Hide Apps' (`HideAppsMinimized`). Drop-down Apps' thread changes it, and the others read it on their threads.
- **A window in Minimize to Tray's tray** can drop down. Drop-down Apps shows it, and Minimize to Tray lets it go as when its app shows it: it removes its mark and icon.
- **Their specs** say so too: [Hide Apps](hide-apps.md#windows-that-stay) and [Minimize to Tray](minimize-to-tray.md#windows-that-cant-go-to-the-tray).
- **With either feature off,** nothing changes.

## With Shortcuts & Mouse Buttons

- **Show or hide** *app*, for each listed app, is an action that [Shortcuts & Mouse Buttons](../input/shortcuts-and-mouse-buttons.md) can run from the user's own shortcuts and mouse buttons while Drop-down Apps is on (`IFeatureActions`). It names the app by its app ID, or its program for an app without one, ignoring case, and does what the app's own shortcut does, also for an app whose shortcut is None. An app that's no longer listed has no action.

## With Title Bar Actions

- **Minimize,** an action for a middle-click on a title bar, hides a shown drop-down window, as minimizing one does.
- **Minimize to tray** and **Hide app,** which pass the window to those features, leave drop-down windows alone, as those features do. See [With the other features](title-bar-actions.md#with-the-other-features).

## Recent results

- The drop-down tools in [Compared with other systems](#compared-with-other-systems) don't say on screen why an app didn't drop down.
- **What's kept:** the host keeps the last 5 results in memory, each with its time, the app's place in the list, and the outcome:
  - shown, hidden, brought to the front, brought from another desktop, or started;
  - the app runs as administrator;
  - the app is a Store app in Windows' shared frame;
  - the window has a dialog open;
  - the program is missing or isn't an `.exe`, or the app ID isn't in Start's Apps folder;
  - Windows couldn't start the program, or it needs admin rights;
  - no window came within 30 seconds;
  - the window stayed on another desktop;
  - another program has the shortcut.

  The same result again right after itself is counted, with its latest time, instead of pushing the others out, as on the Hot Corners page.
- **Results name no app:** they hold only its place in the list, which the page shows with the name the list keeps. They aren't written to disk, and they're gone when the feature is turned off or the host exits.
- **On the page:** a **Recent results** card lists them, newest first, such as "Notes: runs as administrator, so it can't drop down while PC Companion doesn't", with the time in the user's format. It works as Hot Corners' card does: it updates while the page is shown, and says when results aren't available.
- **How they get there:** through a named pipe that only the user's own account and the system can open, named `PCCompanion.DropDownApps.Results.<session ID>`, which rejects remote clients and reads nothing (`SnapshotPipe`), as for Hot Corners.

## Threads

- **Its own thread:** Drop-down Apps runs on a thread of its own, derived from `MessageLoopThread<TSettings>`. [Threads and processes](../../architecture/features.md#threads-and-processes) keeps work on the host's UI thread to a few milliseconds, and a slide waits for each frame for about 150 ms. The shortcuts and hooks need a message loop, which the thread pool doesn't have.
- **The host's UI thread** runs only `Apply`, which reads the section and passes it to the feature's thread. The first `Apply` also looks for windows left marked: it reads every top-level window's properties, which waits for no program.
- **The feature's thread** has its own hidden window and runs everything else:
  - the shortcuts, and following the keyboard layout;
  - the WinEvent hooks;
  - finding windows and reading their apps' identity;
  - placing, sliding, showing, hiding, marking and putting windows on top;
  - giving the front back;
  - the 150 ms wait before hiding on focus loss;
  - `WM_QUERYENDSESSION`;
  - the warning sound;
  - the recent results, which it copies for the pipe.
- **During a slide** it waits for each frame (`DwmFlush`), at most one frame's time, and handles its messages between frames.
- **The thread pool** runs:
  - starting apps (`IHostCommands`, which waits for Windows up to 30 seconds);
  - looking up app IDs in the Apps folder;
  - `SetWindowPlacement`, for minimized and maximized windows and for putting maximized windows back;
  - the pipe.

  Each passes its result back to the feature's thread with `MessageWindow.BeginInvoke`.
- **Nothing on the feature's thread waits for another program's window:**
  - `ShowWindowAsync`, `SetWindowPos` with `SWP_ASYNCWINDOWPOS`, `GetWindowPlacement`, `GetProp`, `SetProp` and `DwmGetWindowAttribute` didn't wait for an app that stopped responding in the spike;
  - `SetWindowPlacement`, which waited, runs on the thread pool;
  - `SetWindowRgn`, which sends the window messages and waits for them, isn't used.
- **Stopping** brings every hidden window back on the taskbar, removes the shortcuts, hooks and window, and ends the thread.
- **Failures:** a failure on the feature's thread is logged, brings the hidden windows back on the taskbar, and keeps the feature off until the host restarts, like a failing `Apply`. The other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100) confirmed the following. It used test windows it created, Edge, File Explorer, the command prompt, Character Map and Settings, at 100% scale on one display. The Sandbox draws about 32 frames a second and has animation effects off.

- **Shortcuts:** every shortcut on the list registered and reached the spike in 1 to 4 ms, with nothing of Windows' opening.
- **Starting apps:**
  - `shell:AppsFolder\MSEdge` gave Edge's window in 1.6 seconds. The window set the app ID `MSEdge` itself.
  - Starting Edge again while it ran opened a second window.
  - An app ID that isn't in Start's Apps folder opened File Explorer at Documents.
  - Settings' frame first showed cloaked at 136 × 39 pixels and was ready about 150 ms later.
  - Character Map, File Explorer and the command prompt showed a window in 0.1 to 0.7 seconds. The command prompt's window reports `cmd.exe` as its program.
- **Placing and showing:**
  - A hidden window moved with `SWP_ASYNCWINDOWPOS` was in place 14 ms later, still hidden, and was shown at the new place.
  - Its visible frame landed exactly on the work area's top half. The invisible borders were 7 pixels left, right and below for every app.
- **Sliding:**
  - Moving the whole window once per frame worked for every app.
  - Every move was drawn, about 31 ms apart when timed by `DwmFlush` in the Sandbox. With a 60 Hz timer, half the moves were never drawn there.
  - The processor time of the spike and the apps stayed at the clock's resolution.
  - Not sending moves to a slow app until the last one arrived kept the slide on time: 167 ms instead of 255 ms for an app that took 30 ms per move, and about 290 ms instead of 485 ms for 80 ms per move.
  - A window whose app kept it 200 pixels down stayed there, and the slide ended on time.
  - A window whose app didn't respond for 1.5 seconds went to its place when the app responded, and the slide ended on time.
- **Focus:**
  - Bringing the window to the front as the slide started worked, and keys typed during the slide reached it, not the window behind.
  - After a hide, Windows gave the front to the window that had it before, and giving it back explicitly worked too.
- **Owned windows and dialogs:**
  - A palette stayed on screen when its owner was hidden. Hidden and shown again with it, it stayed owned and in front of it.
  - Hiding a Windows Forms dialog canceled it, as minimizing its window does.
  - A Win32 message box and the Open dialog stayed open while hidden and came back in front.
- **Minimized, maximized and snapped:**
  - A hidden maximized window stayed maximized after `SetWindowPos`, or after `SetWindowPlacement` that kept it hidden.
  - `SW_SHOWNOACTIVATE` followed by `SetWindowPos` showed it at its old place for about 9 ms.
  - `SetWindowPlacement` with `SW_SHOWNOACTIVATE` and a normal place above the screen showed it there at once.
  - Put back with `SetWindowPlacement`, minimized and set to come back maximized, it came back maximized, then at its old size. With asynchronous calls only, it came back without being maximized.
  - A snapped window dropped down like any other.
- **Minimum size:** Windows kept a window at its minimum size of 1000 × 700 when it was asked for less.
- **Keep on top** was set and taken off for a test window and Edge, also while hidden.
- **Virtual desktops:**
  - A window hidden on one desktop and shown while another was showing came on that one, without a switch.
  - A window shown on another desktop, hidden and shown again at once, came back on its old desktop, and bringing it to the front switched to that desktop.
  - With 50, 150, 300, 600 or 1000 ms in between, it came to the current desktop.
- **The front changing:**
  - A dialog of the window came to the front, owned by it.
  - A context menu raised no change.
  - The emoji panel and Clipboard history are no-activate windows, and the front stayed.
  - Start and the notification center are shell windows on top (`Windows.UI.Core.CoreWindow`) that pass the taskbar's test.
  - Quick Settings and the taskbar are tool windows.
  - A click on the desktop gave the front to `Progman`, a tool window.
  - Alt + Tab gave the front to two hidden helper windows before the chosen one, 170 ms later.
  - Show desktop minimized the drop-down window.
- **An app that stopped responding:**
  - `ShowWindowAsync`, `SetWindowPos` with `SWP_ASYNCWINDOWPOS`, `GetWindowPlacement`, `DwmGetWindowAttribute` and `SetForegroundWindow` returned in under 0.5 ms.
  - `SetWindowPlacement` waited 4.7 seconds, until the app responded.
  - What was asked meanwhile, such as keep on top, was done when it responded.
- **Window regions:** `SetWindowRgn` worked on another program's window, 1.7 ms per call while it responded. Windows documents that it sends the window messages.
- **Settings:**
  - Its frame moved with `SetWindowPos`.
  - Hidden without minimizing, it stayed hidden for 30 seconds but kept its taskbar button.
  - Minimized, it could be given a new normal place and restored there.
- **Apps running as administrator:**
  - A copy of the spike labeled to run without admin rights was refused moving, keep on top, hiding and the mark (error 5).
  - Asking the window to minimize and restore (`WM_SYSCOMMAND`) worked.
- **Turning off Windows' own transitions** for another program's window (`DWMWA_TRANSITIONS_FORCEDISABLED`) was accepted. Cloaking it (`DWMWA_CLOAK`) was refused.
- **Not checked yet**, all by hand:
  - smoothness on real displays at 60 to 144 Hz;
  - several displays at different scales, and a display above another;
  - the foreground lock, which the Sandbox applied once;
  - UAC on;
  - Windows 10;
  - Narrator;
  - dialogs of other UI frameworks;
  - Windows' own show and hide animations with animation effects on;
  - desktop apps in Store packages, such as Windows Terminal.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Apps that drop down**, the app list with **Running app**, **Program file** and, in each row, a shortcut list and **Remove**;
  - **Display**, with the pointer or the main display;
  - **Width** and **Height**;
  - **Slide in and out**;
  - **Hide when another app comes to the front**;
  - **Keep on top while shown**;
  - **Recent results**;
  - a note about the windows that can't drop down;
  - a **Permissions & data** card.
- **The app list** is the one Hot Corners, Minimize to Tray and Input Language per App use. Each row has a shortcut list named "Shortcut for <app>" for screen readers, before **Remove**. After **Add**, the focus goes to the new row's list.
- **Slide in and out** says that it follows Windows' animation effects. Width runs from 30% to 100% and Height from 20% to 100%, in steps of 5%.
- **When the feature is first turned on:** no app is listed, the display is the one with the pointer, the width is 100% and the height 50%, the slide is on, the window hides when another app comes to the front on any display, and Keep on top is off.
- **The switch on Home** changes only `enabled` and keeps the other settings.
- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log.
- **Listed apps** are read as Hot Corners reads its list: a blank app ID or program counts as none, and an app with neither or both, or a `null`, is left out, as is an app listed a second time.
- **Unknown or out-of-range values:**
  - An unknown shortcut is None, with a warning.
  - A shortcut that a listed app earlier in the list has is None for the later one, with a warning.
  - An unknown display or choice for hiding takes the default.
  - A width or height out of range is brought into range.

```json
"features": {
  "dropDownApps": {
    "enabled": true,
    "apps": [
      { "name": "Terminal", "appId": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "shortcut": "WindowsControlAboveTab" },
      { "name": "Obsidian", "program": "C:\\Users\\Me\\AppData\\Local\\Programs\\Obsidian\\Obsidian.exe", "shortcut": "WindowsControlAlt1" }
    ],
    "display": "WithPointer",
    "width": 100,
    "height": 50,
    "slide": true,
    "hideWhenInactive": "AnyDisplay",
    "keepOnTop": false
  }
}
```

- `shortcut`: `WindowsControlAboveTab`, `WindowsAltAboveTab`, `WindowsControlAlt1` to `WindowsControlAlt9`, or `None`.
- `display`: `WithPointer` or `Main`.
- `width`: 30 to 100. `height`: 20 to 100.
- `hideWhenInactive`: `AnyDisplay`, `SameDisplay` or `Never`.
- `name` is shown on the page and in recent results only.

## Not included

- **Store apps in Windows' shared app frame:** a hidden frame keeps its taskbar button. Dropping them down by minimizing them instead, which keeps the taskbar button, is possible: the spike gave a minimized frame a new normal place and restored it there. For later.
- **Size, display and other settings for each app,** and a fixed display other than the main one: for later.
- **A shortcut of the user's choosing,** with shortcuts as triggers ([#39](https://github.com/Lazyb0y/pc-companion/issues/39)).
- **Taking the shown window off the taskbar:** possible with `ITaskbarList::DeleteTab`, which keeps the window off the taskbar and out of Alt + Tab until Explorer restarts (Minimize to Tray's spike), so a crash would leave it there.
- **Hiding the title bar and borders:** changing another app's window styles breaks apps that draw their own title bar.
- **Sliding from another edge, or fading.**
- **Choosing one of the app's windows,** matching windows by title, and starting an app with arguments.
- **Hiding the other drop-down windows when one shows:** hiding when another app comes to the front already does it.
- **Sliding across a display above:** the window shows at once instead.
- **Turning off Windows' own show and hide transitions for the window** (`DWMWA_TRANSITIONS_FORCEDISABLED`): Windows accepted it in the spike, but whether those transitions get in the way of the slide is checked by hand first.
- **A Hot Corners action:** Hot Corners is in another module; see [#152](https://github.com/Lazyb0y/pc-companion/issues/152).

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - windows: `ShowWindowAsync`, `SetWindowPos`, `SetWindowPlacement`, `GetWindowPlacement`, `SetForegroundWindow`, `GetLastActivePopup`, `IsWindowEnabled`, `SetProp`, `GetProp` and `RemoveProp`;
  - events and keys: `SetWinEventHook`, `RegisterHotKey`, `MapVirtualKeyEx` and `GetAsyncKeyState`;
  - displays and composition: `DwmGetWindowAttribute`, `DwmFlush`, `MonitorFromPoint`, `MonitorFromWindow`, `GetMonitorInfo`, `GetCursorPos` and `SystemParametersInfo`;
  - the shell: `SHCreateItemFromParsingName`;
  - the session: `SetProcessShutdownParameters` and `MessageBeep`.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:**
  - **At a press:** the visible top-level windows' styles, owners, whether they're enabled, minimized, maximized and cloaked, their place, display and class name, and for those the taskbar shows, their app's identity; and the pointer's position, to choose the display.
  - **While a drop-down window is shown,** with hiding on focus loss on: which window comes to the front, with its styles, owner, process and display, and whether a mouse button is held.
  - **While windows have dropped down:** Windows reports when they close, show, hide or start to minimize.
  - It reads no window titles or contents and receives no keys or clicks: no keyboard or mouse hook.
- **Changes:** other apps' windows: moves, resizes, shows, hides, brings to the front and puts on top, and adds and removes its marks on them. It starts listed apps, never with admin rights. It runs no code in other apps, sends them no input and writes no file. It asks Windows to end the host early at sign-out.
- **Settings:** a changed settings file can only list apps to start or move when their shortcut is pressed, and set where they show. A program is started only when it exists and is an `.exe`, in its own folder and without arguments. An app ID is opened only through Start's Apps folder, after it's found there, so it can name no path or argument.
- **The mark:** as for Minimize to Tray, another program running as the user could mark its own hidden windows, or another app's, so that PC Companion shows them at its next start; it could show them itself just as well. Windows refuses a program without admin rights the mark on a window of an app running as administrator.
- **Logs:**
  - **Debug entries** for each press: the app's place in the list, what happened or why nothing did, and the slide's frames, skipped moves and time.
  - **Never** titles, app IDs, names or paths, which can name documents, profiles or users.
  - **Warnings** when Windows refuses a shortcut, and when a shortcut or other value is unknown or the settings section can't be read.
  - **An error** when a failure stops the feature.
- **Recent results:** kept in the host's memory and given only to programs of the user's own account on this PC, through a pipe the host only writes to. They hold an app's place in the list, an outcome and a time, nothing about what was on screen.
- **Network:** none. `PRIVACY.md` gets a line on Drop-down Apps.
- **Rights:**
  - It runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off.
  - Running as administrator, it also moves and hides the windows of apps running as administrator, which the user can move and minimize anyway.
  - Turning the feature off brings every hidden window back and removes the shortcuts and hooks.

## Performance

- **Between presses,** nothing runs while no drop-down window is shown, apart from following the keyboard layout for the key above Tab and the number keys, as App Windows does. Each process with a drop-down window has a hook whose events for its other windows are dropped at once.
- **While a drop-down window is shown,** the foreground hook reports each window that comes to the front, for hiding on focus loss.
- **A press** lists the windows once and reads app identity only for windows the taskbar shows.
- **A slide** sends one move per frame for about 150 ms. The spike measured no processor time beyond the clock's resolution, for PC Companion or the app.

## Accessibility and localization

- **Keyboard:** each app's shortcut. The page works with the keyboard alone, and every switch, list, slider and button has a label for screen readers.
- **Screen readers:** a window that drops down gets the focus, and the window that gets the front back as it hides gets it, so screen readers announce each. Drop-down Apps announces nothing of its own: the host has no UI Automation provider. The page says what each shortcut does.
- **Motion:** the slide follows Windows' animation effects, and **Slide in and out** turns it off on its own.
- **Colors and layout:**
  - The page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages.
  - The window slides from the top and is centered in every language.
- **Strings:** the shortcuts' names show the key above Tab's character in the user's layout. The strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake windows, a fake clock, fake displays and fake Windows calls:
  - **which window drops down:** the app's front-most taskbar window, a browser profile's app ID and a package's app ID, a minimized window, one on another virtual desktop and one in Minimize to Tray's tray; and those that can't: an app running as administrator (mark refused), the shared app frame, a window with a dialog open and the desktop, each with the warning sound and its recent result;
  - **a press, by the window's state:** hidden shows it, in front hides it, shown behind brings it to the front, shown on another desktop brings it here with the wait and the retry, and a press while the app starts does nothing;
  - **starting the app:** a program, a missing program and one that isn't an `.exe`, an app ID found and not found in the Apps folder, the first window by identity from another process, a window that shows cloaked first, and no window within 30 seconds;
  - **the place:** the work area, width and height, the invisible borders, a minimum size centered again, a size dragged by the user kept for the session, and no slide when a display touches the top edge;
  - **the slide:** easing, one move per frame, no new move while the last hasn't arrived for up to two frames, ending on time, a window whose app puts it elsewhere, and no slide with animation effects or the switch off;
  - **minimized and maximized windows:** shown with `SetWindowPlacement` on the thread pool, and put back maximized;
  - **hiding:** the front given back, or left when that window is gone, minimized or cloaked; owned windows hidden and shown again; keep on top set and taken off, and left on for a window on top by its own choice;
  - **hiding on focus loss:** each kind of window that doesn't count, the desktop that does, the 150 ms wait, a held mouse button, only on its display, never, and a window with a dialog open;
  - **minimized or shown by others:** the window minimized by the user or Show desktop, shown by its app, and closing;
  - **never losing a window:** marks set before hiding and removed on showing, every hidden window back on exit and at `WM_QUERYENDSESSION`, the crash list, and recovery at the next start, on and off;
  - **the shortcuts:** one per app, the first free one for an added app, and a refused shortcut's warning and recent result;
  - **the shared set** read by Hide Apps and Minimize to Tray;
  - **the settings:** defaults, the spec's example, an unknown shortcut, a shortcut listed twice, values out of range, and a section that can't be read;
  - **recent results:** the last 5 kept, the JSON the pipe gives, and the page's text for each outcome.
- **Sandbox checks** (`scripts/sandbox/groups/DropDownApps.ps1`), with test programs the checks build and start, and animation effects turned on for the slide, since the Sandbox has them off:
  - **the page:** the Home switch turns the feature on, and the page saves the list, with Running app adding an app with the first shortcut, the shortcut chosen in its row, Keep on top and Remove; with a test program holding Windows + Ctrl + the key above Tab, the note below the list names the app, until the program lets it go and a setting changes;
  - **dropping down:** the shortcut, sent by `SendInput` by scan code, slides the app's front-most window in at the top of the work area at the page's width and height, in front, and leaves its other window; the host logs the slide's frames;
  - **hiding:** pressing it again slides the window up and hides it, marked, and the window that had the front gets it back; with animation effects off, the window shows and hides at once;
  - **hiding on focus loss:** the window hides when another test program's window comes to the front;
  - **windows that stay:** a window with a dialog open stays, and the host logs why;
  - **keep on top:** the window is on top while shown, and not after it hides;
  - **an app that stopped responding:** a test window that stops responding doesn't hold the shortcut, as the host's log shows, and is hidden once it responds;
  - **virtual desktops:** a window shown on the first desktop comes to a second one, in front, without a switch;
  - **with the other features:** Minimize to Tray's shortcut and Hide others leave a shown drop-down window, and a window in Minimize to Tray's tray drops down and leaves the tray;
  - **never losing a window:** **Exit** brings a hidden window back on the taskbar, minimized; ending the host with `Stop-Process` leaves it hidden and marked, and the next start brings it back;
  - **maximized windows:** a maximized test window drops down unmaximized, and comes back maximized after **Exit**;
  - **starting:** with the test program not running, the shortcut starts it and drops down its window; Edge starts by its app ID and drops down; an app ID that isn't in the Apps folder opens nothing, and the host logs why;
  - **recent results** show on the page;
  - **apps running as administrator:** with the host's file labeled Medium integrity, as the spike did, a test window running as administrator doesn't drop down, and the host logs why; Settings, in the shared frame, doesn't either;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** several displays and scales, a display above another, the foreground lock, UAC on, Windows 10, Narrator, and smoothness on a real display (the Sandbox draws about 32 frames a second).
- **By hand:**
  - the slide on real displays at 60 Hz and 120 to 144 Hz, with animation effects on, including Windows' own transitions;
  - keys typed during the slide reaching the window;
  - a click on the desktop, the window's own context menu, the emoji panel and Clipboard history, with hiding on focus loss;
  - several displays at different scales: the display with the pointer, the main display, a display above another, and a window moving to a display with another scale;
  - that the window comes to the front, and the front goes back, on a PC, where the foreground lock applies;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - the shortcuts on a real keyboard with the US, German and French layouts, switching layouts;
  - desktop apps in Store packages, such as Windows Terminal and Notepad, a browser with two profiles and an installed web app, an Electron app, an app started through a launcher, and an app that closes to its notification icon;
  - dialogs of other UI frameworks, such as WPF, in a drop-down window;
  - signing out with a hidden window that has unsaved changes, and canceling the sign-out;
  - Narrator after each press, high contrast, and a right-to-left language;
  - Windows 10.
