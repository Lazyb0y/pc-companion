# Window Tabs

Group windows of different apps into one window with tabs, and switch between them with a click or a shortcut.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.windowTabs`. Issue: [#36](https://github.com/Lazyb0y/pc-companion/issues/36).
- Inspired by window tabs in Fluxbox, Stack & Tile in Haiku, and tabs in macOS.
- Off until the user turns it on.

## Behavior

- **Making a group:** drag a window by its title bar and drop it on another window's tabs, or hold Ctrl while dropping it on another window's title bar. **Windows + Ctrl + T** puts the window in front into the group of the window behind it. See [Making a group](#making-a-group).
- **The tab strip:** a row of tabs, one per window, with each app's icon and the window's title, sits on top of the group's window, above its title bar. See [The tab strip](#the-tab-strip).
- **Switching:** clicking a tab, or **Windows + Ctrl + Page Down** and **Windows + Ctrl + Page Up**, shows that tab's window in the group's place, at its size, maximized when the group is, and in front. The other windows of the group are hidden.
- **One window to Windows:** the taskbar, Alt + Tab and Task View show a group once, as its tab in front.
- **Leaving a group:** drag a tab off the strip, choose **Move out of group** from the tab's menu, or press **Windows + Ctrl + Shift + T**. A group with one window left ends.
- **Groups come back:** after a restart or a sign-out, windows of the same apps are grouped again; see [Keeping groups after a restart](#keeping-groups-after-a-restart).
- No window is ever lost: whatever happens to PC Companion, hidden tabs come back on the taskbar; see [Never losing a window](#never-losing-a-window).
- Some windows can't be tabs, such as those of apps running as administrator while PC Companion doesn't. Windows' warning sound plays, and the page says which and why; see [Windows that can't be tabs](#windows-that-cant-be-tabs).

## Compared with other systems

- **Windows** has tabs only inside apps that offer them, such as File Explorer, Notepad, Terminal and browsers. Sets, tabs for any app, was tried in preview builds in 2017 and 2018 and withdrawn: testers didn't understand it, and Office needed work of its own ([OSNews](https://www.osnews.com/story/129881/windows-10s-sets-feature-is-gone-and-not-expected-to-return/)). Window Tabs needs nothing from apps, and the page explains how groups are made.
- **macOS** puts windows in tabs only within an app that supports them, and Merge All Windows merges one app's windows ([Apple](https://support.apple.com/guide/mac-help/use-tabs-in-windows-mchla4695cce/mac)). Window Tabs mixes apps.
- **KDE Plasma** had window tabs from KDE 4.4, lost them in Plasma 5.2 when its new window decorations didn't offer them, and removed the code in 2019. Its developers won't bring them back, since apps that draw their own title bars make them unreliable ([343690](https://bugs.kde.org/show_bug.cgi?id=343690), [458873](https://bugs.kde.org/show_bug.cgi?id=458873)). Its tabs also listed every tab in Alt + Tab and on the taskbar ([296092](https://bugs.kde.org/show_bug.cgi?id=296092)). Window Tabs draws its strip above the window, never inside an app's title bar, so apps that draw their own title bars work, and Windows lists a group once.
- **Fluxbox** groups a window dropped on another with the middle button or Ctrl held, and its tabs share one size, which made a fixed-size editor shrink ([Gentoo 183982](https://bugs.gentoo.org/183982)). Window Tabs uses Ctrl too, and leaves out windows that can't be resized.
- **Haiku** stacks windows of different apps with a key held while dragging, and can't restore its groups after a restart, since its windows have no lasting identity. Window Tabs keeps each group's apps and groups their windows again.
- **i3, Sway, Hyprland and niri** have tabbed containers only within their tiling layouts. Users report apps that misbehave in tabs that aren't shown, such as screen-sharing pickers that stay blank and titles that stop updating. Window Tabs groups ordinary windows, without a tiling layout, and activates a tab's window as it shows it, as Alt + Tab does; how apps behave while their tab is hidden is tested by hand.
- **Tab tools for Windows:**
  - some load code into every app: users report a game dropping from 200 to 20–30 frames a second, and antivirus warnings;
  - some keep the tabs that aren't shown behind the one that is, so every tab stays on the taskbar and in Alt + Tab, and the tabs behind trail the window when it's dragged;
  - tools that hid windows lost them when they crashed, until they kept a list in a file;
  - users report strips in the wrong place at 175% scale.

  Window Tabs runs no code in other apps and uses no keyboard or mouse hook. Windows lists each group once, and only the strip moves during a drag. It marks the windows it hides, so a restarted PC Companion finds exactly those, and it draws the strip for each display's scale.

## Making a group

### Dropping a window

- **On a strip:** dragging a window by its title bar and releasing the mouse button with the pointer on a group's strip adds the window to that group. No key is needed: the strip is a clear target.
- **On a title bar, with Ctrl:** releasing it with Ctrl held and the pointer on the top of another window, within 48 effective pixels of the top of its visible frame, starts a group of the two, or adds it to that window's group. Without Ctrl, nothing changes: dropping a window over another is common when arranging windows, so grouping needs the key, as in Fluxbox.
- **Group by dropping with Ctrl,** a switch on the page, on by default, turns the second way off.
- **The dropped window** becomes the group's tab in front, at the group's place; the window that was in front is hidden. It goes after the last tab, or between the tabs where it was dropped on a strip.
- **While dragging:** a strip under the pointer comes above the dragged window, so it can be seen, and shows where the tab would go. With Ctrl held over another window's top, a highlight in the accent color covers that window's top. Nothing is highlighted for a window that can't join, or over a window that can't be a tab.
- **How it's noticed, without a hook:** an out-of-context WinEvent hook for `EVENT_SYSTEM_MOVESIZESTART` and `EVENT_SYSTEM_MOVESIZEEND`, which Windows raises as any window's move starts and ends. While a window that could join moves, a hook for `EVENT_OBJECT_LOCATIONCHANGE` in its process reports each move, and at each one the pointer's position (`GetCursorPos`) and Ctrl (`GetAsyncKeyState`) are read. In the spike, a window dropped on a strip joined 75 ms after the mouse button was released.
- **The window under the pointer** is the front-most shown window there, other than the dragged one, found in the order of windows from the front (`EnumWindows`), by its visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`).
- **A window already in a group** moves its whole group when dragged by its title bar, and can't be dropped into another group: its tab can.
- **Windows' own snapping** still works: when Windows maximizes or snaps the dropped window, as at the top of a display, the window still joins and takes the group's place.

### From the keyboard

- **Windows + Ctrl + T** adds the window in front to the group of the window behind it, or starts a group of the two. The window behind is the front-most shown window behind it in the order of windows that can be a tab, or that is a group's tab in front, on the same display, not minimized and not on another virtual desktop. The window in front becomes the tab in front, at that window's place, and goes after the group's last tab.
- **A tab in front** moves to the group behind it, as its tab does when it's dragged onto that group's strip, and its old group shows its next tab.
- **When it can't group,** as when the window in front can't be a tab or no window behind it can, Windows' warning sound plays.
- **Windows + Ctrl + Shift + T** takes the tab in front out of its group. It stays shown, moved 32 effective pixels down and to the right (to the left in right-to-left languages), so the group's next tab shows behind it.
- **Group from the keyboard** on the page offers Windows + Ctrl + T or None.

### Leaving a group

- **Dragging a tab off the strip,** by more than the strip's height, and releasing it there: the window leaves the group and shows where it was dropped, with the pointer on its title bar, at the group's size.
- **Dragging a tab onto another group's strip** moves it to that group. Dragging it along the strip moves it among the tabs.
- **The tab's menu:** right-clicking a tab opens **Move out of group** and **Close window**, then **Window Tabs settings**, which opens the feature's page. The menu follows the Windows mode and high contrast, and is laid out right to left in right-to-left languages, as the PC Companion icon's menu is (`PopupMenu`, `MenuTheme`).
- **Close window** shows that tab first, so a question about unsaved changes shows with its window, then asks the window to close as its Close button does (`WM_SYSCOMMAND` with `SC_CLOSE`, posted).
- **The shortcut,** Windows + Ctrl + Shift + T; see [From the keyboard](#from-the-keyboard).
- **A group with one window left ends:** its strip goes, and the window stays an ordinary window.

### Windows that can't be tabs

- **Apps running as administrator,** while PC Companion runs without admin rights: Windows refuses to hide, move or mark their windows, as the spike showed. With Always run as administrator, or with UAC off, they can be tabs.
- **Store apps in Windows' shared app frame** (`ApplicationFrameWindow`, in `ApplicationFrameHost.exe`), such as Settings: hidden, the frame keeps its taskbar button and a blank entry in Alt + Tab (spike). Store apps that run as desktop apps, such as Windows Terminal and most new apps, have ordinary windows and can be tabs.
- **Windows the taskbar doesn't show** (`TopLevelWindows.MayHaveTaskbarButton`), such as dialogs, palettes and tool windows, and the desktop, the taskbar and the shell's other windows.
- **Windows that can't be resized,** without a sizing border (`WS_THICKFRAME`): every tab takes the group's size.
- **A window with a dialog open,** recognized by the window being disabled (`IsWindowEnabled`): hiding it cancels a Windows Forms dialog, as the spike showed, and an app can't be told apart in advance.
- **A window that stopped responding** (`IsHungAppWindow`), and the stand-in Windows shows for one (class `Ghost`, in Desktop Window Manager's process).
- **Minimized and full-screen windows,** and windows on another virtual desktop.
- **Drop-down Apps' windows and windows in Minimize to Tray's tray;** see [With the other features](#with-the-other-features).
- **How the user is told:** the drop shows no highlight; when the mouse button is released with Ctrl over a window that can't take tabs, or a shortcut can't group, Windows' warning sound plays (`MessageBeep` with `MB_ICONEXCLAMATION`). The page lists the windows that can't be tabs and why. Its note about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.
- Each case is logged at Debug level with its reason, never with the app.

## The tab strip

### Where it is

- **Above the window:** the strip stands on the top edge of the window in front's visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`), from its left edge, or its right edge in right-to-left languages, like the tab of a folder. It covers nothing of the window: the spike showed a strip inside the title bar covering File Explorer's tabs and the window's buttons.
- **No room above,** as for a maximized window or one that touches the top of the work area: the strip goes over the top of the window, and shows only while the pointer rests within 2 effective pixels of the window's top edge for 400 ms, while the pointer is on the strip, for 1.5 seconds after a tab switch or a grouping from the keyboard, and during a drag that could drop on it. A quick click at the top, as on a browser's tabs, isn't delayed. The pointer's position is read every 50 ms only while such a strip's group is in front or its strip shows.
- **Its height** is 32 effective pixels at the display's scale. **Its width:** each tab is up to 200 effective pixels wide; tabs narrow to fit the window's width, down to 40, which shows only the icon. Past that, the strip is wider than the window.
- **It follows the window in front,** on every move and size change (`EVENT_OBJECT_LOCATIONCHANGE` for that window's process). In the spike, following a drag took 0.3 to 0.4 ms per move on the feature's thread, and the strip was in place in every frame but one.
- **Its place in the order of windows:** right above the window in front, never always on top, so other windows cover it as they cover the window. When the window comes to the front (`EVENT_SYSTEM_FOREGROUND`), the strip is put back right above it.
- **Its own window,** never owned by the window in front: an owned window would tie the strip's input to that app's, and in the spike a click on such a strip waited 5.6 seconds for an app that had stopped responding.
- **Hidden** while the group is minimized, full screen or on another virtual desktop; see [While windows are tabs](#while-windows-are-tabs).

### What it shows

- **Each tab:** the app's icon and the window's title, cut with an ellipsis at its end, and the full title in a tooltip.
- **The icon** is the window's own, as the taskbar shows it, read without waiting for the app, as Minimize to Tray reads it (`WindowIcon`), drawn at the display's scale.
- **The title** is read without a message (`GetWindowText`), and again when it changes (`EVENT_OBJECT_NAMECHANGE`).
- **The tab in front** stands out: its background, and a line in the accent color under it.

### Look

- **Drawn by PC Companion,** pixel by pixel (`LayeredWindow`, which grows a way to be placed in the order of windows rather than always on top), with the strip drawing System Meters uses moved to `PCCompanion.Platform.Windows` so both features share it.
- **Light and dark:** it follows the app mode (Choose your app mode), as apps' title bars do, read with `WindowsTheme`, and is drawn again when the mode, the accent color or high contrast changes.
- **High contrast:** the strip uses the high contrast colors: the window color behind the tabs, window text for their titles, the highlight color behind the tab in front with its title in the window color, and window text for the line under it and a border around the strip. The drop highlight uses the highlight color.
- **Corners:** rounded at the top on Windows 11, square on Windows 10, as the windows there are.
- **Text:** Segoe UI Variable on Windows 11 and Segoe UI on Windows 10, at title bar size, following Windows' Text size (`WindowsTheme.TextScale`). Icons keep their size, as on PC Companion's pages.
- **Right to left:** in right-to-left languages, the strip stands at the window's right edge, the tabs run right to left, and each icon is right of its title. Titles show as the apps write them, read in the language's direction.
- **Scale:** sizes follow the scale of the display the window in front is on (`GetDpiForMonitor`), also for windows that aren't DPI aware, and the strip is drawn again when that window moves to a display with another scale.

### Using it

- **Clicking a tab** shows its window. The strip never takes the focus (`WS_EX_NOACTIVATE`), so the click goes to the tab, and Windows lets PC Companion bring the window to the front after the user's click.
- **Dragging a tab** moves it among the tabs, to another group, or out of its group; see [Leaving a group](#leaving-a-group).
- **Right-clicking a tab** opens its menu.
- **The strip doesn't move the group:** the window's title bar does, which keeps Windows' snapping. In the spike, a move request posted from the strip (`SC_MOVE`) didn't move the window.

## Switching tabs

### How

1. **The new tab is placed while hidden:** when the group isn't maximized and the window's frame isn't already the group's, the window is moved to the visible frame of the tab in front, with its own invisible borders added, read when it was last shown (`SetWindowPos` with `SWP_ASYNCWINDOWPOS`). The spike saw a hidden window shown afterwards at its new place, never at its old one.
2. **Shown and activated:** with `ShowWindowAsync`: maximized when the group is (`SW_SHOWMAXIMIZED`), restored when it was hidden maximized and the group isn't (`SW_SHOWNORMAL`), and otherwise as it was (`SW_SHOW`), so a tab that was snapped comes back snapped. Its owned windows that were hidden with it are shown again. Then it's brought to the front with its open dialog, if any (`SetForegroundWindow` on `GetLastActivePopup`).
3. **The old tab is hidden only once the new one shows:** after the new window is shown (`IsWindowVisible`) and two more frames have been drawn (`DwmFlush`, waited for on the thread pool), the old one is marked and hidden (`ShowWindowAsync` with `SW_HIDE`), with its shown owned windows, such as palettes, which Windows doesn't hide with it. In the spike, hiding the old window at once, or as soon as the new one was visible, left a frame with neither; with the two frames, none of the switches did.
4. **The strip** shows the new tab in front.

- **An app that paints slowly** shows its window's background for a moment: about 150 ms of white for a test window that took 150 ms to paint. Edge, File Explorer, the command prompt and Task Manager showed at once.
- **An app that stopped responding:** its tab shows when the app responds again. Until then the old tab stays, however long it takes; the strip shows the chosen tab. A tab whose app responds after the user chose another tab is hidden again as soon as it shows.
- **A tab with a dialog open** stays in front: switching plays the warning sound, since hiding it would cancel a Windows Forms dialog.
- **Windows + Ctrl + Page Down** shows the next tab and **Windows + Ctrl + Page Up** the previous one, going round from the last to the first. With Shift, they move the tab in front one place along the strip. They act on the group of the window in front; with no group in front, the warning sound plays.

### Place, size and state

- **One place for all tabs:** the visible frame of the tab in front. Hidden tabs are given it when it changes, not during a drag: after a move or size ends (`EVENT_SYSTEM_MOVESIZEEND`), and 120 ms after the last other change, such as a maximize, a snap or a move by the app.
- **How:** `SetWindowPlacement` on each hidden window, with its normal place set to the group's and `SW_HIDE`, on the thread pool, since it waits for the app. In the spike it took 1 to 16 ms per window.
- **Maximized:** a group whose tab in front is maximized shows every tab maximized on that display, with the group's normal place, so restoring it brings the group back to the same place.
- **Snapped:** a tab shown at a snapped place takes that place, but isn't snapped: Windows has no way for a program to snap another program's window. So Windows + Down minimizes it instead of restoring it, and Windows' snap groups don't include it. A snapped tab that's hidden and shown again, with the group in the same place, stays snapped (spike); whether it stays snapped after the group moved is tested by hand.
- **Another display scale:** a tab shown on a display with another scale than it had may resize itself as it moves (`WM_DPICHANGED`), so its frame is checked 100 ms after it shows and set again if its app changed it, as Drop-down Apps does.
- **Minimum sizes:** an app that keeps a minimum size larger than the group's keeps it; the strip follows the window in front.
- **Coordinates:** places are in physical pixels, as `DWMWA_EXTENDED_FRAME_BOUNDS` gives them; a normal place is converted to the work area's coordinates that `SetWindowPlacement` uses.

## While windows are tabs

- **Minimized:** minimizing the tab in front, from its button, Windows + Down, Show desktop or Hide Apps, minimizes the group: the strip hides, and the hidden tabs stay hidden (`EVENT_SYSTEM_MINIMIZESTART`). Restoring it brings the strip back.
- **Full screen:** while the tab in front covers its display without a frame, as Hot Corners decides (its check is in `PCCompanion.Platform.Windows`, which both modules use), such as a browser after F11, the strip hides and the tab shortcuts play the warning sound. In the spike, Edge's full screen was recognized and the strip came back after it.
- **The app shows a hidden tab itself,** as for a reminder or a second launch (`EVENT_OBJECT_SHOW`): the app wins. That tab becomes the tab in front, at the group's place, and the old one is hidden, as the spike did. When the group's tab in front is on another virtual desktop then, the window leaves the group instead, since it showed on the desktop that's showing.
- **The app hides its tab in front itself,** as when it closes to its notification icon: the window leaves the group, and the next tab shows in its place. Window Tabs never shows a window its app hid.
- **A tab closes, or its app ends** (`EVENT_OBJECT_DESTROY`): its tab goes. When it was in front, the next tab shows, or the one before the last, and takes the front if the closed window had it.
- **Owned windows** that an app shows while its tab is hidden, such as a notification, show as the app chooses.
- **Virtual desktops:** a group is on the virtual desktop of its tab in front; hidden tabs are on none. A hidden tab shows on the desktop that's showing, as Windows places a window that's shown again, so tabs are switched only from the strip or a shortcut, on that desktop. When the tab in front is on another desktop (`EVENT_OBJECT_CLOAKED`, as Windows hides the windows of other desktops), the strip hides, and it shows on the desktop of the tab once that desktop shows (`EVENT_OBJECT_UNCLOAKED`). Moving the tab in front to another desktop with Task View moves the group.
- **Explorer restarts:** File Explorer windows in Explorer's process close with it and leave their groups.
- **How it's watched:** for each process with tabs, one out-of-context WinEvent hook for `EVENT_OBJECT_DESTROY` to `EVENT_OBJECT_HIDE` and one for `EVENT_OBJECT_NAMECHANGE`; for the processes of the tabs in front, one for `EVENT_OBJECT_LOCATIONCHANGE`; and hooks for every process for the foreground, minimize, move and cloaking events. Events for windows that aren't tabs are dropped at once.

## Taskbar, Alt + Tab and Task View

- **A group shows once:** hidden tabs aren't on the taskbar, in Alt + Tab, in Task View or in Snap Assist, so Windows shows the tab in front, with its preview. Selecting it brings the group to the front.
- **The taskbar button is the tab in front's app's.** When another tab comes to the front, its app's button shows the group: an app whose only shown window was hidden loses its button, unless it's pinned, and the other app's button may show at another place on the taskbar.
- **Alt + Tab** goes to groups, not tabs: the tab shortcuts and the strip switch within a group, as tabs within a browser window.

## The shortcuts

- **Switch tabs** on the page offers:
  - **Windows + Ctrl + Page Down / Page Up,** the default, as browsers use Ctrl + Page Down and Page Up for their tabs;
  - **Windows + Alt + Page Down / Page Up;**
  - **None.**

  Each comes with its Shift version to move a tab.
- **Group from the keyboard** offers Windows + Ctrl + T, with Windows + Ctrl + Shift + T to take a tab out, or None.
- **Registration:** with `RegisterHotKey` and `MOD_NOREPEAT`, so Windows tells PC Companion only when a shortcut is pressed. No keyboard hook.
- **Why these:** in the spike, on Windows 11 build 26100, a program could register Windows + Ctrl or Windows + Alt with Page Down and Page Up, and Windows + Ctrl with T, with or without Shift, while Windows itself has Windows + Ctrl + Left and Right, Windows + Alt with the arrows, Windows + Alt + T and Windows + Tab. Ctrl + Page Down and Page Up alone, and Ctrl + Tab, belong to apps. On keyboards without Page Up and Page Down keys, they're Fn with the arrows; users can set their own shortcuts with Shortcuts & Mouse Buttons.
- When another program already has a shortcut, Windows refuses it and a warning is logged. PC Companion tries again after any change to the settings. The page shows a warning note at that shortcut, which goes away once Windows takes it; see [Running in the host](../../architecture/features.md#running-in-the-host).

## Keeping groups after a restart

- **Keep groups after a restart,** a switch on the page, on by default, keeps each group's apps and groups their windows again when they show after the host starts, as after a restart, a sign-out or an update.
- **What's kept:** for each group, its tabs' apps, as the taskbar groups them (`AppOfWindow`): the app ID or the program path, in the strip's order, and which tab was in front. Never titles. In memory, and in `window-tabs.json` in the data folder, which only the host writes, on the thread pool, 5 seconds after a change and when the feature stops, as Input Language per App keeps its file. At most 50 groups; the one changed longest ago goes.
- **Grouping again:** during the first 60 seconds after the host starts, a remembered group is made again as soon as each of its apps has a shown or minimized window in no group, and with the windows present then once the 60 seconds are up, when there are at least two. Each app's front-most such window is taken. The group takes the place and state of the window of its remembered tab in front: it's minimized when that window is. Each remembered group is made once.
- **Forgetting:** a group that ends is forgotten. Turning the switch or the feature off forgets every group and deletes the file.
- **A file that can't be read,** as after a hand edit, is ignored with a warning in the log and replaced at the next write. An entry with neither or both an app ID and a program is left out.
- **Running as administrator:** while the host runs as administrator after a UAC prompt, it neither reads, writes nor deletes the file, since a program without admin rights could put a link in its place; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs). Groups are then kept for the session only.

## Never losing a window

- **The mark:** before a tab is hidden, PC Companion sets a window property on it (`WindowMark`, named `PCCompanion.WindowTabs`), as Minimize to Tray does, and on its hidden owned windows. The mark is removed when the window shows. It lives as long as the window, so nothing outlasts a restart of Windows. When the mark can't be set, as for an app running as administrator, the window isn't hidden.
- **When PC Companion exits** for any reason it's told about: **Exit**, turning the feature off, setup closing it for an update or a removal (`WM_CLOSE`), Restart Manager and the end of the session (`WM_ENDSESSION`).
  - Every hidden tab comes back on the taskbar, minimized, without taking the front (`ShowWindowAsync` with `SW_SHOWMINNOACTIVE`), with its owned windows.
  - The tabs in front stay where they are, the strips go, and the marks are removed.
  - The host waits up to 2 seconds for the feature's thread, as for Minimize to Tray.
  - With Keep groups after a restart on, the groups are kept first, so the next start groups the windows again.
- **Signing out, restarting and shutting down:** the hidden tabs come back on the taskbar when Windows asks whether the session may end (`WM_QUERYENDSESSION`), before their apps are asked to close, so an app's question about unsaved changes shows with its window. The host asks Windows to ask it before other apps (`ShutdownOrder.AskFirst`). When the user cancels the sign-out, the windows stay on the taskbar and their groups have ended.
- **When PC Companion crashes:** a handler for unhandled exceptions brings the hidden tabs back on the taskbar before the host ends, from a list the feature's thread keeps for it, as Minimize to Tray's does. A failure on the feature's own thread does the same. When the host is ended without a chance to run code, for example from Task Manager, the hidden tabs stay hidden until it starts again. The spike confirmed both: the marked windows stayed hidden after their process was ended, and were found and shown again in under a millisecond.
- **At the next start,** on or off, the feature brings back on the taskbar, minimized, every hidden window that carries the mark, and removes the mark. Opening PC Companion from Start starts the host, so the user has a way back at any time.

## Apps running as administrator

- Windows keeps the windows of apps running as administrator away from programs that don't run as administrator. The spike, with a copy labeled to run without admin rights, confirmed that Windows refuses to move, hide, mark or place such a window (error 5), and still reports its moves.
- **So without admin rights,** such a window can't be a tab: dropping it on a strip or the shortcut plays the warning sound, and the page says that Always run as administrator lets it be one.
- **Ctrl while such a window is in front:** Windows doesn't tell a program without admin rights which keys are held while a window of an app running as administrator is in front (`GetAsyncKeyState` returns nothing then), so dropping such a window with Ctrl on a title bar does nothing, without the sound, as the Sandbox checks showed.
- **With Always run as administrator, or UAC off,** every window can be a tab.

## With the other features

- **App Windows** switches between the windows of the app in front that Alt + Tab shows, so it skips hidden tabs: a group counts as its tab in front.
- **Hide Apps:** Hide app and Hide others minimize a group's tab in front like any window, which minimizes the group; hidden tabs stay hidden, and Show all brings back the tab in front with its group.
- **Minimize to Tray** refuses a window that's a tab, with its warning sound, as it refuses Drop-down Apps' windows, and a window in the tray can't be a tab: it's hidden.
- **Drop-down Apps** doesn't take a tab as an app's drop-down window, and a drop-down window can't be a tab.
- **The shared sets:** the module keeps the windows that are tabs in a set that Minimize to Tray and Drop-down Apps read, as they share `HideAppsMinimized` and `DropDownWindows`; Window Tabs reads theirs. Their specs get a line on Window Tabs with the code.
- **Title Bar Actions** acts on a group's tab in front as on any window: Minimize minimizes the group, and Send to back sends the group back, with its strip. Minimize to tray plays the warning sound, as Minimize to Tray refuses the tab. Its click actions also offer **Add to the group behind** and **Move out of group**, which do what Windows + Ctrl + T and Windows + Ctrl + Shift + T do, for the clicked window, through `WindowRequests`; see [With the other features](title-bar-actions.md#with-the-other-features).
- **Shortcuts & Mouse Buttons** can run **Next tab**, **Previous tab**, **Move tab forward**, **Move tab back**, **Add to the group behind** and **Move out of group** from the user's own shortcuts and mouse buttons while Window Tabs is on (`IFeatureActions`). They act on the window in front, so launchers can't run them. See [How features offer actions](../input/shortcuts-and-mouse-buttons.md#how-features-offer-actions).
- **End App** ends a tab's app like any other; its tabs go.
- **Window Rules** ([#26](https://github.com/Lazyb0y/pc-companion/issues/26)) can put a window in a group by rule, and **Window shading** ([#27](https://github.com/Lazyb0y/pc-companion/issues/27)) shades a group's tab in front, with its strip staying, once those features exist; their specs say how.
- **With either feature off,** nothing changes.

## Threads

- **Its own thread:** Window Tabs runs on a thread of its own, derived from `MessageLoopThread<TSettings>`, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: the strips must answer clicks and screen readers at once, a drag reports a move at every frame, and the shortcuts and hooks need a message loop. Its thread is a single-threaded apartment, as UI Automation needs.
- **The host's UI thread** runs only:
  - `Apply`, which reads the section and passes it to the feature's thread;
  - on the first `Apply`, looking for windows left marked: it reads every top-level window's properties, which waits for no program.
- **The feature's thread** has its own hidden window and runs everything else:
  - the strips' windows: drawing, clicks, drags, tooltips, menus and their UI Automation providers;
  - the drop highlight;
  - the shortcuts;
  - the WinEvent hooks, following drags, and placing the strips;
  - reading the pointer's position every 50 ms, only while a strip shown at its window's top edge is in front or shown, to tell when the pointer rests there;
  - showing, hiding, moving, marking and bringing windows to the front;
  - reading titles, icons and app identity;
  - `WM_QUERYENDSESSION` and the warning sound.
- **The thread pool** runs `SetWindowPlacement`, which waits for the app, the two frames' wait after a tab shows (`DwmFlush`), reading program icons when a window has none, and reading and writing `window-tabs.json`. Each passes its result back to the feature's thread with `MessageWindow.BeginInvoke`.
- **Nothing on the feature's thread waits for another program's window:** `ShowWindowAsync`, `SetWindowPos` with `SWP_ASYNCWINDOWPOS`, `PostMessage`, `GetWindowText`, `GetProp`, `SetProp`, `SendMessageCallback` for icons and `DwmGetWindowAttribute` don't; `SetWindowPlacement`, which does, runs on the thread pool.
- **Its handlers stay within a few milliseconds:** in the spike, following a drag took 0.3 to 0.4 ms per move on average and under 2 ms at most, a switch 1 to 13 ms of work on the thread, and a resize, which draws the strip again at each size, up to 6 ms. Tabs aren't moved during a drag, only the strip.
- **Stopping** brings every hidden tab back on the taskbar, closes an open menu (`EndMenu`), removes the strips, highlight, shortcuts, hooks and window, and ends the thread.
- **Failures:** a failure on the feature's thread is logged, brings the hidden tabs back on the taskbar, and keeps the feature off until the host restarts, like a failing `Apply`. The other features keep running.

## Spike results

Three runs of a spike in Windows Sandbox, on Windows 11 24H2 (build 26100) at 100% scale on one display, with test windows it created, Edge, File Explorer, the command prompt, Character Map, Task Manager and Settings, compared five ways of keeping the tabs that aren't shown out of sight. The Sandbox draws about 32 frames a second and has animation effects off.

- **Hiding** (`SW_HIDE`): Windows listed one window per group on the taskbar and in Alt + Tab. With the old window hidden two frames after the new one showed, no switch left a frame with neither window; slow painting showed as above. Following a drag cost 0.3 to 0.4 ms per move, with the strip in place in every frame but one. A snapped window hidden and shown again was still snapped. Hiding canceled a Windows Forms dialog, and Settings kept its taskbar button and a blank Alt + Tab entry while hidden. A hidden window shown while another virtual desktop showed came back on that desktop.
- **Keeping the tabs behind the window in front:** switching was instant, and dialogs were kept, but every tab stayed on the taskbar and in Alt + Tab. The tabs behind trailed the window by up to 15 pixels while it was dragged and 32 pixels while it was resized, and minimizing the window in front had to minimize every tab too.
- **Keeping them behind and fully transparent** (`LWA_ALPHA` 0): switching was instant, even to an app that had stopped responding, but every tab stayed on the taskbar and in Alt + Tab, where Windows showed their pictures as if opaque. After the spike's process was ended, the tabs stayed invisible but listed. Following a drag moved every tab at each move, about 4 ms per move with six tabs.
- **Minimizing:** every tab stayed on the taskbar, minimized, and a tab shown again was black until its app painted it.
- **Moving the windows into a frame of the spike's own** (`SetParent`): ending the frame's process destroyed every window in it, and Edge's process ended. Keys typed into the frame didn't reach the window in it, and Settings was refused.
- **A strip owned by the window in front:** a click on it waited 5.6 seconds for an app that had stopped responding, against 110 ms for a strip of its own.
- **Dropping** a window on a strip, noticed from the move events and the pointer's position, added it in 75 ms. A move request posted from the strip (`SC_MOVE`) didn't move the window.
- **Apps running as administrator:** the copy labeled Medium integrity was refused moving, hiding, placing, marking and adopting a window of an app with admin rights (error 5); it got the window's move events, and a posted minimize and restore worked.
- **Full screen:** Edge after F11 covered its display without a frame, and Windows reported a full-screen app (`SHQueryUserNotificationState`).
- **Shortcuts:** see [Why these](#the-shortcuts).
- **Not checked yet**, by hand: several displays and scales, UAC on, Windows 10, the foreground lock, Windows Terminal and Electron apps, which the Sandbox doesn't have, Windows' snap groups, and a real mouse.

## Settings

- **Feature page:** the on/off switch; a card that says how to make a group; **Group by dropping with Ctrl**; **Switch tabs**; **Group from the keyboard**; **Keep groups after a restart**; warning notes at shortcuts that another program has; a note about the windows that can't be tabs and apps running as administrator; and a **Permissions & data** card.
- When the feature is first turned on: Group by dropping with Ctrl on, Switch tabs Windows + Ctrl + Page Down / Page Up, Group from the keyboard Windows + Ctrl + T, and Keep groups after a restart on.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Window Tabs off with a warning in the log. An unknown shortcut choice registers none, with a warning.

```json
"features": {
  "windowTabs": {
    "enabled": true,
    "groupByDropping": true,
    "switchShortcuts": "WindowsControlPage",
    "groupShortcuts": "WindowsControlT",
    "keepGroups": true
  }
}
```

- `switchShortcuts`: `WindowsControlPage`, `WindowsAltPage` or `None`.
- `groupShortcuts`: `WindowsControlT` or `None`.

## Not included

- **Tabs drawn inside apps' title bars:** they cover the apps' own tabs and buttons, and KDE Plasma's tabs failed on apps that draw their own title bars.
- **Moving windows into a frame of PC Companion's:** the spike lost every window in the frame when its process ended.
- **Keeping the tabs behind the one in front,** visible or transparent: Windows then lists every tab on the taskbar and in Alt + Tab, and every tab moves at each move of a drag. Showing every tab on the taskbar could use the transparent way later, as a choice.
- **Hiding by cloaking:** Windows refuses it for other programs' windows, and allows it only through undocumented interfaces.
- **Store apps in Windows' shared frame:** they could be minimized instead of hidden; later.
- **Grouping windows by rule,** such as every window of an app: with Window Rules ([#26](https://github.com/Lazyb0y/pc-companion/issues/26)).
- **Merging two groups,** vertical tabs, tab previews, a group's own name or color, and closing a tab with the middle button.
- **Moving the group by its strip:** the title bar moves it, with Windows' snapping.
- **A program that brings the tabs back when the host is ended from Task Manager:** the next start does.
- **A group across virtual desktops or displays.**
- **Recent results** that say on the page why a window couldn't be a tab.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - windows: `ShowWindowAsync`, `SetWindowPos`, `SetWindowPlacement`, `GetWindowPlacement`, `SetForegroundWindow`, `GetLastActivePopup`, `IsWindowEnabled`, `IsHungAppWindow`, `EnumWindows`, `GetWindowText`, `SetProp`, `GetProp`, `RemoveProp` and `PostMessage`;
  - events, keys and the pointer: `SetWinEventHook`, `RegisterHotKey`, `GetAsyncKeyState` and `GetCursorPos`;
  - displays and composition: `DwmGetWindowAttribute`, `DwmFlush`, `UpdateLayeredWindow`, `MonitorFromWindow`, `GetMonitorInfo` and `GetDpiForMonitor`;
  - UI Automation: `UiaReturnRawElementProvider` and `UiaRaiseNotificationEvent`;
  - the session: `SetProcessShutdownParameters` and `MessageBeep`.
- No undocumented API is used.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:**
  - **For each tab:** its title and icon, to draw the tab, while it's a tab; its styles, owner, state, place, display, class name and app identity.
  - **While a window is dragged,** if it could be a tab: the pointer's position and whether Ctrl is held at each move, and the shown windows' places under the pointer.
  - **Windows reports** when tabs close, show, hide, move, change titles or start to minimize, and which window comes to the front.
  - It receives no keys or clicks besides its shortcuts and clicks on its own windows: no keyboard or mouse hook.
- **Titles** stay in memory only, while the window is a tab. They're never written or logged. The strip shows them on screen, and gives them to UI Automation, where screen readers read them; any program of the user's can read a window's title directly anyway.
- **Changes:** other apps' windows: hides, shows, moves, resizes, maximizes and brings them to the front, adds and removes its mark, and asks a window to close when the user chooses Close window. It runs no code in other apps, sends them no input, and changes none of their styles.
- **`window-tabs.json`:** each group's apps, by app ID or program path, which can name the user, in the strip's order, without titles or times. Kept on this PC in the data folder; turning the switch or the feature off deletes it, and uninstalling removes it with the rest of the data folder. Not read or written while the host runs as administrator after a UAC prompt.
- **Settings and the file** can only choose shortcuts and switches, and group windows of the listed apps that are already open, so a changed file can't make PC Companion do more.
- **The mark:** as for Minimize to Tray, another program running as the user could mark its own hidden windows, or another app's, so that PC Companion shows them at its next start; it could show them itself just as well. Windows refuses a program without admin rights the mark on a window of an app running as administrator.
- **Logs:**
  - **Debug entries** when a group is made, joined, switched or left, or ends, with the number of tabs and why, when a window can't be a tab and why, and when no window behind can be grouped with.
  - **Never** titles, app IDs, names or paths, which can name documents, profiles or users.
  - **Warnings** when Windows refuses a shortcut, and when the settings section or the file can't be read.
  - **An error** when a failure stops the feature.
- **Network:** none. `PRIVACY.md` gets a line on Window Tabs.
- **Rights:** runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Running as administrator, it also groups the windows of apps running as administrator, which the user can move and minimize anyway. Turning the feature off brings every hidden tab back and removes the shortcuts and hooks.

## Performance

- **With no groups,** the global hooks for moves, the foreground, minimizing and cloaking report events, which are dropped at once unless a window that could be a tab is dragged. No timer runs.
- **With groups,** each process with tabs has hooks whose events for its other windows are dropped at once. The pointer's position is read every 50 ms only while a strip shown at its window's top edge is in front or shown.
- **A drag** moves only the strip, 0.3 to 0.4 ms per move in the spike. Hidden tabs get their place once, when it ends.
- **A switch** shows one window and hides one, with the work on the feature's thread taking 1 to 13 ms in the spike, most of it drawing the spike's strip, then waits two frames on the thread pool.
- **The strip** is drawn again only when its tabs, their titles or icons, its size, the colors or the scale change.

## Accessibility and localization

- **Keyboard:** the shortcuts switch, move and group tabs. The page works with the keyboard alone, and every switch, list and button has a label for screen readers.
- **Screen readers:** each strip has a UI Automation provider, the host's first: a tab list named after the window in front, with a tab item per window, named by its title, with its position in the list and whether it's the tab in front. A tab can be chosen through UI Automation, as with a click. When a tab switches, the window that comes to the front gets the focus, which screen readers announce as after Alt + Tab, and the strip raises a notification with its position, such as "Tab 2 of 3".
- **Colors:** the strip follows the app mode, dark or light, the accent color and high contrast; see [Look](#look).
- **Motion:** the strip has no animation.
- **Right to left:** the strip, its menu and its tooltips are laid out right to left in right-to-left languages; see [Look](#look).
- **Strings:** the menu's items, the notification and the page are translated like the other pages', marked "Needs native review." "Tab 2 of 3" names a position, not an amount, so its words don't change with the number and it needs no plural forms.

## Testing

- **Unit tests,** with fake windows, a fake clock, fake displays and fake Windows calls:
  - **groups:** made by dropping on a strip and with Ctrl on a title bar, from the keyboard, a window joining between two tabs, leaving by dragging off, by the menu and by the shortcut, in front of the tab that shows in its place, moving between groups and along a strip, and a group of one ending;
  - **which windows can be tabs:** each kind in [Windows that can't be tabs](#windows-that-cant-be-tabs), with the warning sound, and no highlight while dragging;
  - **switching:** the order show, visible, two frames, hide; a hung new tab keeping the old one, and one that shows after another tab was chosen; a tab with a dialog open; next and previous going round; owned windows hidden and shown;
  - **place and state:** invisible borders, maximized groups and their normal place, a snapped tab kept snapped, another scale checked again, a minimum size, and work area coordinates;
  - **the strip:** above the window, at the top edge with the 400 ms rest, tab widths down to icons and past the window, right to left, and placing it right above the window in front;
  - **while windows are tabs:** minimized, full screen, the app showing or hiding a tab, also one that Window Tabs showed as its last group ended, a tab closing, the tab in front on another virtual desktop, and Explorer restarting;
  - **keeping groups:** the file's contents, grouping again within 60 seconds and after, minimized windows, the 50-group limit, forgetting, a file that can't be read, and no file while running as administrator after a UAC prompt;
  - **never losing a window:** marks set before hiding and removed on showing, every hidden tab back on exit and at `WM_QUERYENDSESSION`, the crash list, and recovery at the next start, on and off;
  - **the shared sets** read by Minimize to Tray and Drop-down Apps;
  - **UI Automation:** the tab list's names, positions, the tab in front, and choosing a tab;
  - **the settings:** defaults, the spec's example, unknown shortcut choices and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/WindowTabs.ps1`), with test windows the checks start, painted in solid colors:
  - **the page:** the Home switch turns the feature on with its defaults, and the page saves each setting; with a test program holding Windows + Ctrl + Page Down, and then one holding Windows + Ctrl + T, the page shows its note until the program lets it go and a setting changes;
  - **making a group:** a test window dragged with `SendInput` and dropped with Ctrl on another's title bar makes a group: the strip shows, the other window is hidden and marked, and the taskbar lists one window; one dropped on the strip joins it; one dropped without Ctrl doesn't; Windows + Ctrl + T groups the window in front with the window behind it, at its place, and Windows + Ctrl + Shift + T takes it out again, beside the group and in front of it;
  - **switching:** a click on a tab and the shortcuts show each tab in the group's place, in front, and screen samples at the tab's center never show what's behind the group; with Shift, a tab moves;
  - **dragging and resizing:** the strip follows, and the hidden tabs have the new place when the drag ends; the host logs the time per move;
  - **maximized and snapped:** a maximized group shows each tab maximized and restores them to the same place; a snapped tab hidden and shown again is snapped;
  - **minimized and full screen:** minimizing the tab in front hides the strip and restoring brings it back; a test window that goes full screen hides the strip;
  - **apps:** the app showing a hidden tab makes it the tab in front; closing the tab in front shows the next; a test window that stops responding doesn't hold the host, and its tab shows once it responds, with the old tab in front until then;
  - **windows that can't be tabs:** a window with a dialog open, Settings and a fixed-size window can't join, and the host logs why; with the host's file labeled Medium integrity, as the spike did, neither can a window running as administrator;
  - **File Explorer and Edge** as tabs, switching both ways;
  - **virtual desktops:** the strip hides on another desktop and shows again on the group's;
  - **never losing a window:** **Exit** brings every hidden tab back on the taskbar, minimized; ending the host with `Stop-Process` leaves them hidden and marked, and the next start brings them back;
  - **keeping groups:** after the host is ended and started again, the test windows are grouped again in their order;
  - **with the other features:** Minimize to Tray's shortcut and Drop-down Apps refuse a tab;
  - **the strip** in light and dark, and in Arabic, with screenshots, and its UI Automation tab list and names;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** several displays and scales, UAC on, the foreground lock, Windows 10, Narrator, high contrast, Windows Terminal and Electron apps, Windows' snap groups, and smoothness on a real display.
- **By hand:**
  - dropping with a real mouse, with and without Ctrl, including over a maximized window at the top of the display where Windows offers its snap layouts;
  - the strip at the top edge of a maximized browser: quick clicks on its tabs aren't delayed, and resting there shows the strip;
  - Windows + Ctrl + T and Windows + Ctrl + Shift + T with real apps and a maximized window, on a PC, where the foreground lock applies, and the tab actions on the user's own shortcuts, mouse buttons and title-bar clicks;
  - several displays at different scales, moving a group between them, and windows that aren't DPI aware;
  - the taskbar buttons as tabs switch, pinned and not, and Windows' snap groups;
  - that a tab comes to the front on a PC, where the foreground lock applies;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - Windows Terminal, Electron apps such as Visual Studio Code, Teams and Discord, Office, a browser with two profiles, and an app that closes to its notification icon, including that each repaints and takes input after being shown again;
  - signing out with a hidden tab that has unsaved changes, canceling the sign-out, and groups coming back after a restart and an update;
  - Narrator reading the strip and the notifications, high contrast, a right-to-left language, and Windows' Text size;
  - Windows 10.
