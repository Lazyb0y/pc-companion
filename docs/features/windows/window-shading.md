# Window Shading

Roll a window up to its title bar, to see and use what's behind it without moving it away, and roll it down again.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.windowShading`. Issue: [#27](https://github.com/Lazyb0y/pc-companion/issues/27).
- Inspired by KDE Plasma's window shading and classic Mac OS's collapse box.
- Off until the user turns it on.

## Behavior

- **Roll up:** the window is reduced to its title bar, which stays where it was, and what was below it shows. See [Rolling up](#rolling-up).
- **Roll down:** the window comes back as it was, in front. A click on the rolled-up title bar, the wheel turned down on it, the taskbar and Alt + Tab all roll it down. See [Rolling down](#rolling-down).
- **Ways to roll up:** **Roll up** in Title Bar Actions, for a middle-click on a title bar or the wheel turned up on it, and in Shortcuts & Mouse Buttons, for a shortcut or mouse button. **Roll down all** is in the PC Companion icon's menu while a window is rolled up. See [Actions](#actions).
- **Keep on top while rolled up,** on by default, keeps rolled-up title bars above other windows.
- **Hover to roll down,** off by default, rolls a window down while the pointer rests on its title bar, and up again when the pointer leaves.
- **No window is ever lost:** a rolled-up window is a minimized window, on the taskbar and in Alt + Tab, so it can always be brought back, also if PC Companion stops; see [Never losing a window](#never-losing-a-window).

## Compared with other systems

- **KDE Plasma** shades windows from the title bar, the wheel, a shortcut or the window menu, and can unshade on hover. It works only on X11: it was never made to work on Wayland, since apps that draw their own title bars can't be shaded reliably and many windows have a minimum height ([377162](https://bugs.kde.org/show_bug.cgi?id=377162)). A shaded window can be active without taking keys, which users find confusing, a window picked on the taskbar can stay shaded, and hover unshading breaks with context menus ([479956](https://bugs.kde.org/show_bug.cgi?id=479956)).
- **Other Linux desktops:** GNOME removed shading in 2023; Xfce rolls windows up with the wheel by default, which surprised enough users that a distribution turned it off; Openbox rolls up only with the wheel turned up and down only with it turned down, and unrolls a window picked with Alt + Tab.
- **macOS:** classic Mac OS collapsed a window to its title bar, which stayed active and movable. Today's macOS doesn't let one app resize another's windows, so a tool for it minimizes the window and shows a picture of its title bar instead.
- **Windows** has no shading. Tools for Windows shrink the window, and their users report windows that won't shrink below their minimum height, windows that grow back when dragged, apps with their own title bars that don't collapse or are cut off, and windows left flat after the tool restarts; others load code into every app.
- **Window Shading** works like the macOS tool: the window is minimized, and a strip of PC Companion's own shows a picture of its title bar that Windows draws. So it works for every main window with a title bar, including the custom title bars of browsers, File Explorer, Settings and WinUI apps, whatever its minimum size, loads nothing into other apps, never leaves a window flat, and gives the keys to the window behind. The wheel only rolls up when turned up and down when turned down, any way of bringing the window back rolls it down, and its title bar can stay on top.

## Rolling up

- **Two steps:**
  1. The window is minimized as its Minimize button does (`ShowWindowAsync` with `SW_MINIMIZE`), which gives the front to the window behind it, so keys never go to a rolled-up window. The window keeps its size, place and state: a maximized or snapped window comes back maximized or snapped.
  2. A strip, a window of PC Companion's own, takes the title bar's place, at its size, showing a picture of the title bar that Windows draws (`DwmRegisterThumbnail`, with the title bar as the source). The spike showed Edge's tabs, File Explorer's tab, Settings' search box and PC Companion's title in it.
- **The picture is the title bar as it was:** while the window is minimized, Windows keeps the picture it had, so a change made meanwhile, such as a new title, doesn't show until the window is rolled down (spike).
- **The title bar's height** is where the window stops answering `HTCAPTION`, asked downwards from the top of its visible frame at 40% and 60% of its width, as Title Bar Actions asks a window, through the child window that answers ([Recognizing a title bar](title-bar-actions.md#recognizing-a-title-bar)):
  - the spike found 31 pixels at 100% for standard title bars, 35 for File Explorer and 41 for Edge;
  - at most the top 64 effective pixels, Title Bar Actions' band, so an app that answers `HTCAPTION` for its whole window doesn't become one long strip;
  - when the window answers nothing, or within 150 ms, the height of its caption buttons (`DWMWA_CAPTION_BUTTON_BOUNDS`), or else Windows' caption height for its DPI.
- **The strip's edges:** the picture leaves out the frame's outermost pixel, which is transparent, so no gap shows at the strip's edges (spike).
- **Which windows:** main windows with a title bar that the taskbar shows, as Title Bar Actions recognizes them. Not:
  - full-screen windows, minimized windows and hidden windows;
  - windows with a dialog open (`IsWindowEnabled`), since minimizing a Windows Forms window with a dialog open cancels the dialog;
  - windows that stopped responding;
  - Window Tabs' tabs, Drop-down Apps' windows and windows in Minimize to Tray's tray, as the module's shared sets say;
  - PC Companion's own windows;
  - without admin rights, windows of apps running as administrator; see [Apps running as administrator](#apps-running-as-administrator).

  A window that can't be rolled up plays Windows' warning sound, as Title Bar Actions' other refusals do, and the log says why.
- **An app that hides its window as it's minimized,** as apps that live in their notification icon do: the window is restored at once, without its strip, with the warning sound, since it can't be rolled up.

## Rolling down

- **On the strip:**
  - **a click** rolls the window down, restored and in front (`ShowWindowAsync` with `SW_RESTORE`, then `SetForegroundWindow`, which the click on PC Companion's window allows);
  - **a click on the picture of the Close button** closes the window instead, where Windows says where the title bar's buttons are (`DWMWA_CAPTION_BUTTON_BOUNDS`): Close is the last of them, at the right, or at the left in a window laid out right to left. The window is rolled down first and then asked to close (`WM_SYSCOMMAND` with `SC_CLOSE`), so a question about unsaved changes shows with it;
  - **the wheel turned down** rolls it down; turned up, it does nothing;
  - **a right-click** opens a menu with **Roll down** and **Close**;
  - **a drag** moves the strip; the window rolls down where the strip is, at its size, on that display (`SetWindowPlacement`, keeping it minimized until it's restored).
- **Elsewhere:** the window is rolled down, and its strip goes, whenever it's restored by anything else: its taskbar button, Alt + Tab, Task View, its app or another feature (`EVENT_SYSTEM_MINIMIZEEND`). When the window closes (`EVENT_OBJECT_DESTROY`) or is hidden (`EVENT_OBJECT_HIDE`), the strip goes.
- **Roll down all,** in the PC Companion icon's menu while a window is rolled up, and as an action for Shortcuts & Mouse Buttons, rolls every rolled-up window down without giving any the front (`SW_SHOWNOACTIVATE`).

## Hover to roll down

- **Off by default.** When on, a window rolls down while the pointer rests on its strip for **Roll down after**, 0.5 seconds by default, without taking the front (`SW_SHOWNOACTIVATE`).
- **It rolls up again** when the pointer has been off the window for **Roll up again after**, 1 second by default, unless:
  - the window was clicked meanwhile and is in front, which keeps it rolled down;
  - a menu or another window of its app is open, such as a dialog or a list, which keeps it rolled down until they close;
  - the user is moving or sizing it.
- **Why the delays:** hovering over a title bar on the way somewhere else shouldn't open a window, and a window shouldn't vanish while the pointer crosses its edge.

## Actions

- **Title Bar Actions** gets **Roll up** for each middle-click and for the wheel, none of which has it until the user chooses it:
  - **as a click action,** it rolls up the window whose title bar was clicked;
  - **as the wheel's action,** turning the wheel up on a title bar rolls the window up, and turning it down on its rolled-up title bar rolls it down. Each direction does one thing, so more notches, or a wheel that bounces, never roll a window up and down again.
  - It goes through `WindowRequests` (`RollUp`), as Title Bar Actions' other actions for other features do. While Window Shading is off, the action plays the warning sound and the page says so at the action.
- **Shortcuts & Mouse Buttons** gets **Roll up the window in front**, **Roll down the last rolled-up window** and **Roll down all**.
- **The PC Companion icon's menu** gets **Roll down all** while a window is rolled up.
- **No shortcut of its own:** Window Shading's page links to Title Bar Actions and Shortcuts & Mouse Buttons, and says that one of them is needed to roll windows up.
- **Not double-click:** Windows maximizes on a double-click of the title bar.

## Never losing a window

- **A rolled-up window is a minimized window:** it's on the taskbar and in Alt + Tab, and restoring it brings it back as it was, whatever happens to PC Companion. Its strip is PC Companion's own window, so it goes when PC Companion stops.
- **When PC Companion exits** for any reason it's told about (**Exit**, turning the feature off, setup closing it, Restart Manager and the end of the session), every rolled-up window is rolled down without taking the front.
- **When PC Companion crashes,** a handler for unhandled exceptions does the same, from the list the feature keeps.
- **The mark:** a rolled-up window carries a window property (`WindowMark`, named `PCCompanion.WindowShading`) while it's rolled up. At the next start, on or off, the feature rolls down every minimized window that carries it, so a window that was rolled up when PC Companion was ended doesn't stay minimized.

## Apps running as administrator

- **Without admin rights,** Windows refuses minimizing and moving the windows of apps running as administrator, as Title Bar Actions' and Window Rules' spikes showed, so Window Shading leaves them alone, with the warning sound.
- **With Always run as administrator, or UAC off,** it rolls up every window.

## With the other features

- **Title Bar Actions:** **Roll up** for clicks and the wheel, as [Actions](#actions) says. A strip isn't an app's title bar, so Title Bar Actions' clicks and wheel don't act on it; the strip handles them itself.
- **Window Tabs:** tabs aren't rolled up, since the group's strip and Window Tabs' hidden tabs would be left behind. A rolled-up window is minimized, so it isn't grouped.
- **Minimize to Tray:** a window it sends to the tray on minimizing, as the apps in its list, isn't rolled up: Window Shading adds the window to the module's shared set of rolled-up windows (`ShadedWindows`) before minimizing it, and Minimize to Tray leaves the windows in it alone, as it leaves Hide Apps' windows.
- **Hide Apps:** a rolled-up window is already minimized; **Show all** doesn't restore it, since Hide Apps didn't minimize it.
- **Window Rules:** a rolled-up window isn't a new window when it's rolled down, and its held actions stay. **Open rolled up** as a rule action comes later.
- **With either feature off,** nothing changes.

## Threads

- **Its own thread:** Window Shading runs on a thread of its own, derived from `MessageLoopThread<TSettings>`, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: the strips are windows with their own input, and the hooks need a message loop.
- **The host's UI thread** runs `Apply`, which reads the section and passes it to the feature's thread, and Title Bar Actions' and Shortcuts & Mouse Buttons' requests, which `WindowRequests` passes to the feature's thread. On the first `Apply`, it looks for marked windows, which reads every top-level window's properties and waits for no program.
- **The feature's thread** runs:
  - the strips: their windows, thumbnails, clicks, wheel, drags, menus and hover;
  - asking the title bar's height, with `SendMessageCallback`, whose answers come back to the thread;
  - minimizing and restoring with `ShowWindowAsync`, which doesn't wait for the app;
  - the WinEvent hooks for `EVENT_SYSTEM_MINIMIZEEND`, `EVENT_OBJECT_DESTROY` and `EVENT_OBJECT_HIDE`, set only while a window is rolled up;
  - the marks.
- **The thread pool** runs `SetWindowPlacement`, for a strip that was moved, which waits for the app, and passes the result back with `MessageWindow.BeginInvoke`.
- **Stopping** rolls every window down, removes the strips and hooks, and ends the thread. A failure on the feature's thread is logged, rolls every window down, and keeps the feature off until the host restarts, like a failing `Apply`.

## Spike results

Two runs of a spike in Windows Sandbox, on Windows 11 24H2 (build 26100) at 100% scale on one display of 1520 × 823 pixels, with test windows it created (Windows Forms, including one with a minimum size of 500 × 400, and WPF), Character Map, the command prompt, File Explorer, Settings, Edge and PC Companion's settings app. It tried three ways to roll a window up.

- **Shrinking the window** (`SetWindowPos`):
  - Minimum sizes stopped it: File Explorer stayed 236 pixels high, Settings 322, Edge 83 and the test window with a minimum size 393. The command prompt, the plain test windows, WPF, Character Map and PC Companion's window went down to their title bars.
  - Leaving out the app's size check (`SWP_NOSENDCHANGING`) got every window to its title bar, but Edge, Settings and the test window with a minimum size grew back to their minimum as soon as they were dragged by the title bar.
  - Rejected: it depends on each app's minimum size, makes apps lay their content out again, and an app that saves its size would save the rolled-up one.
- **Clipping the window** to its title bar with a window region (`SetWindowRgn`):
  - Windows allowed it on other programs' windows, clicks below the title bar went to the window behind, and the frame kept being drawn by Windows.
  - But Windows still showed the window's whole rectangle, blank below the title bar, for File Explorer, the command prompt, Edge and PC Companion's window.
  - Rejected.
- **A strip with a picture of the title bar** (`DwmRegisterThumbnail`):
  - It showed the title bar of every window tried, with the window shown, minimized, made invisible with the layered style, or moved off the screen; File Explorer moved off the screen showed nothing.
  - Minimized, the window gave the front to the window behind it, and the picture kept the title bar as it was before: a title changed meanwhile didn't show.
  - The picture's outermost pixel was transparent, which showed a gap at the strip's edges.
  - Chosen, with the window minimized, which keeps it on the taskbar and in Alt + Tab, and leaves it minimized if PC Companion stops.
- **The title bar's height,** where `HTCAPTION` ends from the top: 31 pixels for the command prompt, the test windows, WPF, Character Map and PC Companion's window, 35 for File Explorer and 41 for Edge. `GetTitleBarInfo` gave 23 pixels 8 below the top for all of them, and the caption buttons (`DWMWA_CAPTION_BUTTON_BOUNDS`) were 30 pixels high, except Settings', which reported none. The spike's simpler question got `HTCLIENT` everywhere from Settings, which Title Bar Actions' way of asking, through the child window that answers, handles.
- **Not checked:** several displays and scales, Windows 10, an app that hides its window as it's minimized, the minimize animation on a PC, and a program without admin rights rolling up an app running as administrator; earlier spikes showed that Windows refuses minimizing such windows.

## Settings

- **Feature page:** the on/off switch; **How to roll windows up**, a note with links to Title Bar Actions and Shortcuts & Mouse Buttons; **Keep on top while rolled up**; **Hover to roll down**, with **Roll down after** and **Roll up again after**; a note about apps running as administrator; and a **Permissions & data** card.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Window Shading off with a warning in the log. A choice this version doesn't know is kept when the settings are saved, and the default is used.

```json
"features": {
  "windowShading": {
    "enabled": true,
    "keepOnTop": true,
    "hover": { "enabled": false, "rollDownAfterMs": 500, "rollUpAfterMs": 1000 }
  }
}
```

- `hover.rollDownAfterMs`: 250, 500, 1000 or 2000. `hover.rollUpAfterMs`: 500, 1000, 2000 or 5000.
- **Title Bar Actions** keeps its choices in its own section: `RollUp` for a click action and for `wheel`.
- **Shortcuts & Mouse Buttons** keeps its triggers in its own section, with the actions `windowShading.rollUp`, `windowShading.rollDownLast` and `windowShading.rollDownAll`.

## Not included

- **Shrinking the window or clipping it,** which the spike rejected, and **code loaded into other apps,** which PC Companion never does.
- **A live title bar:** while rolled up, the picture is the title bar as it was; Windows doesn't draw a minimized window's changes.
- **A rolling animation:** Windows' minimize and restore animations play, as for any window, when they're on.
- **Double-click:** Windows maximizes on it.
- **Rolling up all windows,** or all of an app's windows: Hide Apps minimizes an app's windows or all others.
- **Rolling up dialogs, tool windows and windows without a title bar.**
- **Open rolled up,** as a Window Rules action: a later improvement of Window Rules.
- **A shortcut of its own:** Shortcuts & Mouse Buttons offers the actions for any shortcut or mouse button.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version: `DwmRegisterThumbnail`, `DwmUpdateThumbnailProperties`, `DwmGetWindowAttribute` (`DWMWA_EXTENDED_FRAME_BOUNDS`, `DWMWA_CAPTION_BUTTON_BOUNDS`), `ShowWindowAsync`, `SetWindowPlacement`, `SetForegroundWindow`, `SendMessageCallback`, `SetWinEventHook`, `SetProp` and `GetProp`.
- No undocumented API is used.
- **Windows 10:** pictures of minimized windows and the strip are tested by hand; the spike ran on Windows 11 only.

## Security and privacy

- **Reads:** for the window being rolled up, its place, styles and state, where its title bar ends (`WM_NCHITTEST`) and where its buttons are; whether a rolled-up window is restored, hidden or closed. It never reads window titles, except to name the strip for screen readers, in memory only, and never the picture's pixels, which Windows draws into the strip.
- **Changes:** minimizes, restores, moves and closes, when asked, the windows it rolled up, and adds and removes its mark. It runs no code in other apps and sends them no input.
- **Stores nothing** but its settings: rolled-up windows are known by their marks, not a file.
- **Logs:** Debug entries when a window is rolled up or down and why one isn't, never titles, app names or paths; a warning when the section can't be read; an error when a failure stops the feature.
- **Network:** none. `PRIVACY.md` gets a line on Window Shading.
- **Rights:** runs with the host's rights; see [Apps running as administrator](#apps-running-as-administrator).

## Performance

- **While no window is rolled up,** the feature sets no hooks and runs no timer.
- **Each rolled-up window** has one strip, whose picture Windows composes, so drawing it costs PC Companion nothing, and the hooks report only minimizing, closing and hiding.
- **Hover** uses the strip's own pointer messages and one timer while the pointer is on or has just left a strip.

## Accessibility and localization

- **Screen readers:** a strip is named after its window and says it's rolled up, such as "Mail, rolled up", and offers **Roll down** as its default action.
- **Keyboard:** a rolled-up window is in Alt + Tab and on the taskbar, and choosing it there rolls it down; Shortcuts & Mouse Buttons' actions roll windows up and down from the keyboard. The page works with the keyboard alone.
- **High contrast and themes:** the picture is the app's own title bar, as Windows draws it.
- **Right to left:** the page and the strip's menu are laid out right to left in right-to-left languages, and Close is found at the left of a window laid out right to left.
- **Strings:** the page, the menu items and the actions are translated like the other pages', marked "Needs native review".

## Testing

- **Unit tests,** with fake windows and a fake clock and fake Windows calls:
  - **rolling up:** which windows, the title bar's height and its fallbacks, the band, and refusals with the warning sound;
  - **rolling down:** a click, Close, the wheel, the menu, a drag, and restoring by anything else; a window that closes or hides; Roll down all;
  - **hover:** the delays, a click keeping the window rolled down, open menus and dialogs, and moving;
  - **never losing a window:** exit, turning the feature off, a crash, and marked windows at the next start;
  - **the other features:** Title Bar Actions' and Shortcuts & Mouse Buttons' actions, and Minimize to Tray leaving rolled-up windows alone;
  - **the settings:** the example, unknown choices kept, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/WindowShading.ps1`), with test windows the checks start, File Explorer, Settings and Edge:
  - **the page:** the Home switch, the settings, and the links;
  - **rolling up** with Title Bar Actions' middle-click and wheel: the window minimized, the strip at its title bar's place showing its picture, and the front given to the window behind;
  - **rolling down** by a click on the strip, the wheel, the taskbar and Alt + Tab, and closing by the picture's Close button;
  - **a drag** of the strip, and the window rolling down there;
  - **hover**, with its delays;
  - **never losing a window:** **Exit** rolls windows down, ending the host with `Stop-Process` leaves them minimized and marked, and the next start rolls them down;
  - **apps running as administrator:** with the host's file labeled Medium integrity, a window running as administrator isn't rolled up.
- **The App group** checks the page's layout and header, as for every feature page.
- **What the Sandbox can't check:** several displays and scales, Windows 10, the minimize animation, the foreground lock, UAC on and Narrator.
- **By hand:**
  - several displays at different scales, a strip dragged to another display, and a maximized or snapped window rolled up and down;
  - real apps with their own title bars: Office, Electron apps such as Visual Studio Code and Teams, and a browser with several tabs;
  - an app that lives in its notification icon, and Minimize to Tray's apps;
  - hover with a real mouse, and the wheel with a smooth-scrolling mouse and a touchpad;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - Narrator on the strip and the page, high contrast, a right-to-left language, and Windows 10.
