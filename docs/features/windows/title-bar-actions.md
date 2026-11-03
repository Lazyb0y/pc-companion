# Title Bar Actions

Middle-click a window's title bar to minimize it or send it to the back, or turn the wheel there to change its opacity, in any app.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.titleBarActions`. Issue: [#28](https://github.com/Lazyb0y/pc-companion/issues/28).
- Inspired by the title-bar actions of KDE Plasma, GNOME, Xfce and Cinnamon.
- Off until the user turns it on.

## Behavior

- **A middle-click on a title bar** runs the action chosen for it: **Minimize** when the feature is first turned on. **Shift + middle-click** and **Ctrl + middle-click** have actions of their own.
- **The wheel on a title bar,** and **Ctrl + wheel**, can change the window's opacity.
- **A title bar is where the window tells Windows that dragging moves it.** Tabs, buttons and search boxes that apps put in their title bars keep their own clicks, so a middle-click on a browser tab still closes the tab.
- **The click that runs an action is kept from the app:** the window isn't activated, and the wheel doesn't scroll it. Clicks and wheel moves without an action reach the app as usual.
- **Double-click and right-click** stay as Windows has them.
- **Listed apps keep their title-bar clicks;** see [Apps that keep their title-bar clicks](#apps-that-keep-their-title-bar-clicks).
- **Some windows get nothing,** such as those of apps running as administrator while PC Companion doesn't; see [Which windows](#which-windows).

## Compared with other systems

- **Windows** maximizes a window on a double-click on its title bar, opens its window menu on a right-click, and minimizes the other windows when it's shaken, where that's turned on. A middle-click or the wheel on a title bar does nothing.
- **KDE Plasma** offers the most:
  - left, middle and right clicks, each set separately for the window in front and the windows behind it;
  - wheel actions in pairs for up and down, such as raise and lower, roll up, and opacity.

  Its users report:
  - apps that draw their own title bars, such as Chrome and Firefox, ignoring all of it ([425123](https://bugs.kde.org/show_bug.cgi?id=425123));
  - rolling up dropped on Wayland as unreliable for the same reason ([377162](https://bugs.kde.org/show_bug.cgi?id=377162));
  - the wheel changing opacity and scrolling the app at the same time ([440284](https://bugs.kde.org/show_bug.cgi?id=440284), [392799](https://bugs.kde.org/show_bug.cgi?id=392799)).

  Title Bar Actions asks each window where its title bar is, so it works for apps that draw their own and tell Windows where it is, and it keeps the wheel from the app.
- **GNOME** has double-, middle- and right-click actions, and no wheel action.
  - Middle-click lowered the window by default until 3.14.1, which changed it to nothing after reports of "disappearing windows" ([737384](https://bugzilla.gnome.org/show_bug.cgi?id=737384)).
  - Firefox ignores the setting and opens a new tab instead.

  Title Bar Actions minimizes by default, so the window stays on the taskbar, and Firefox can be listed to keep its middle-click.
- **Xfce and Cinnamon:**
  - Xfce rolls a window up on the wheel by default, which brushing a touchpad sets off ([8373](https://bugzilla.xfce.org/show_bug.cgi?id=8373)), and changes opacity with Alt + wheel.
  - Cinnamon offers opacity on the wheel, never below 30%, and lowers windows on a middle-click by default, which users report sending Chrome to the back by accident.

  Title Bar Actions gives the wheel no action until the user chooses one, offers Ctrl + wheel for it, and keeps opacity at 20% or more.
- **macOS** offers only the double-click (Fill, Zoom, Minimize or nothing), and apps that draw their own title bars must read the setting themselves; Firefox, Office and Teams didn't. It has no middle-click or wheel actions, and no public way to change another app's opacity.
- **Tools for Windows** that add title-bar clicks or buttons:
  - load code into every app, and users report anti-cheat software, crashes and separate 32-bit and 64-bit helpers;
  - guess the title bar's height, so a middle-click meant for a browser tab closes the whole window, and scripts exclude browsers;
  - draw their buttons over the tabs of File Explorer, Notepad and Terminal;
  - turn windows black when they make them translucent;
  - stop working after sleep, when Windows removes their mouse hook.

  Title Bar Actions runs no code in other apps, asks each window where its title bar is, sets its mouse hook only while the pointer is on a title bar and notices when Windows removes it, skips windows that would break, and makes every window opaque again when it stops.

## Which windows

- **The window whose title bar is under the pointer:** the top-level window of the window there, when it's one that the taskbar shows (`TopLevelWindows.MayHaveTaskbarButton`). The title bars of dialogs, palettes and other owned windows get nothing.
- **These get nothing,** and their clicks reach them as usual:
  - **windows of apps that stopped responding** (`IsHungAppWindow`), and the stand-in Windows shows for them after 5 seconds (class `Ghost`, in Desktop Window Manager's process), for which `IsHungAppWindow` is true too: asking them would wait;
  - **windows with a dialog open,** recognized by the window being disabled (`IsWindowEnabled`): minimizing a Windows Forms window with a dialog open cancels the dialog, as Minimize to Tray's spike showed;
  - **the desktop and the taskbar:** the shell's desktop window (`GetShellWindow`) and the taskbars' windows (classes `Shell_TrayWnd` and `Shell_SecondaryTrayWnd`). File Explorer windows, which often share the shell's process, count like any other;
  - **windows of listed apps;** see [Apps that keep their title-bar clicks](#apps-that-keep-their-title-bar-clicks);
  - **apps running as administrator** while PC Companion doesn't; see [Apps running as administrator](#apps-running-as-administrator).
- **Full-screen windows** have no title bar, so they get nothing anyway.
- **Store apps** in Windows' shared app frame (`ApplicationFrameWindow`), such as Settings, get every action but Minimize to tray, which Minimize to Tray refuses for them. Desktop apps in Store packages, such as Windows Terminal and Notepad, have ordinary windows.
- **When an action can't run,** such as Minimize on a window without a Minimize button or Minimize to tray while Minimize to Tray is off, Windows' warning sound plays (`MessageBeep` with `MB_ICONEXCLAMATION`), and the click is still kept from the app.

## Recognizing a title bar

- **Asking the window:**
  - The host finds the window under the pointer (`WindowFromPoint`, which sends no message to other programs' windows).
  - It sends that window `WM_NCHITTEST` with the pointer's position, as Windows does at every pointer move, with `SendMessageCallback`, which returns at once and passes the answer back to the host's UI thread.
  - Only `HTCAPTION` counts: the area where dragging moves the window. The icon, the buttons, the borders and anything an app answers `HTCLIENT` for, such as its tabs, don't.
- **The window under the pointer answers, not its top-level window:**
  - File Explorer, Settings and Windows Terminal answer for their title bars through child windows, and asking their top-level windows gave the wrong answer over most of the title bar (spike).
  - A child that answers `HTTRANSPARENT` passes the question to its parent.
- **Only the top band:** the point must be within the top 64 effective pixels of the window's visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`), at the window's DPI.
  - The band holds the tallest title bars, those of browsers and WinUI apps.
  - It keeps an app that answers `HTCAPTION` for its whole window, as Firefox's picture-in-picture window does, from turning all of it into a title bar.
- **When:**
  - The window is asked when the pointer comes into a window's top band, and after each move there, with at most one question waiting.
  - Raw input reports a move before the pointer's position follows it, so when an answer is about where the pointer was, the window is asked again.
  - A question with no answer within 150 ms is dropped, and the next move asks again.
- **Windows that aren't DPI aware:** for a window that isn't per-monitor DPI aware, the point is converted to its own coordinates (`PhysicalToLogicalPointForPerMonitorDPI`) before it's asked.
- **Not used:**
  - `GetTitleBarInfo` gives the standard title bar's strip even for Edge and File Explorer, covering their tabs.
  - `DWMWA_CAPTION_BUTTON_BOUNDS` gives the standard buttons for every window.
  - UI Automation can wait seconds for an app, makes Chromium-based apps build their accessibility tree, and answered "Pane" for Edge's empty title-bar area (spike).
- **Apps that draw a title bar but move their window themselves,** without telling Windows where the title bar is, get nothing. Windows' own double-click and snapping don't work in them either.

## Keeping the click from the app

- **Why:** the pointer is watched with raw input, as for Hot Corners, which sees a click but can't stop it. Letting it through would cause trouble:
  - a middle-click activates a window behind before it minimizes or goes to the back;
  - the wheel and Ctrl + wheel also scroll or zoom the app;
  - Firefox opens a tab on a middle-click on its tab bar (spike, and Firefox's source).
- **How:** a low-level mouse hook (`WH_MOUSE_LL`), set only while the pointer rests on a title bar.
  - **When it's set:** once the window answers `HTCAPTION` for where the pointer rests. Title Bar Actions then claims that point, give or take 2 pixels, for the clicks that have an action.
  - **When it's removed:** as soon as the pointer leaves the point.
  - **What it keeps:** a middle-click or wheel notch at the claimed point, with the keys held then (`GetAsyncKeyState`), when that click has an action. It keeps the middle button's release with its press, and lets everything else through: moves, the left and right buttons, the horizontal wheel, and clicks without an action.
  - **What it costs:** setting or removing it takes about 2 µs.
- **The shared pointer hook:** `PointerHook` in `PCCompanion.Platform.Windows`, one per process, as `RawPointerMonitor` is.
  - **Its thread:** its own, since Windows waits for a low-level hook at every pointer event in every program; see [Threads and processes](../../architecture/features.md#threads-and-processes).
  - **Claims:** features add claims, each saying which events to keep and where. It's set while any feature has a claim, and removed when none has.
  - **What its thread does:** it only compares each event with the claims, and posts a kept one to its feature's thread. It calls no other program and waits for nothing.
  - **For #37 and #39:** screen edges and mouse buttons as triggers would add claims of their own ([#37](https://github.com/Lazyb0y/pc-companion/issues/37), [#39](https://github.com/Lazyb0y/pc-companion/issues/39)). Their specs decide when.
- **A click before the window answers,** as right after the pointer reaches a title bar, reaches the app. The action still runs once the window answers `HTCAPTION` for that point, so the window may come to the front first.
- **Checking what the hook did:**
  - Raw input reports kept clicks too, and often before the hook has seen them (spike). So 60 ms after raw input reports a middle-click or a wheel notch, the host looks up, by the input's time, what the hook did with it.
  - **Kept:** nothing more to do.
  - **Let through:** the window is asked, and the action runs, as for a click before the window answers.
  - **Never seen while the hook was set:** Windows removed it. A warning is logged, the hook is set again, and the action runs as for a click let through.
- **When Windows removes the hook:** Microsoft documents that Windows removes a low-level hook, without telling it, after a call takes longer than the timeout. The default timeout is undocumented; it was 300 ms in the spike, and Windows caps it at 1 second.
  - The spike couldn't make Windows remove the hook with injected input: each slow call made all input wait instead.
  - The hook is also set again each time the pointer comes onto a title bar, so a removed hook doesn't last.
- **`RawPointerMonitor` grows** to report which button changed, the wheel's delta and the input's time (`GetRawInputData`, `GetMessageTime`). Its other monitors are unaffected.

## Actions

### Clicks

| Action | What happens | How |
|---|---|---|
| None | The click reaches the app | The hook doesn't claim it |
| Minimize | Minimizes the window, as its Minimize button does, so an app that minimizes to its own notification icon still does | Posts `WM_SYSCOMMAND` with `SC_MINIMIZE` |
| Send to back | Puts the window behind every other window; when it was in front, the next window comes to the front, as with Alt + Esc | `TopLevelWindows.SendToBack` (`HWND_BOTTOM`), then `SetForegroundWindow` on the next window that the taskbar shows |
| Minimize to tray | Sends the window to the notification area | Through [Minimize to Tray](minimize-to-tray.md); see [With the other features](#with-the-other-features) |
| Hide app | Minimizes every window of the window's app, as the taskbar groups them | Through [Hide Apps](hide-apps.md)' Hide app, for the clicked window's app rather than the app in front |

- **The same action** runs whether the window is in front or behind; Send to back gives the front away only when the window had it.
- **An always-on-top window** goes behind every other window and loses always-on-top, as with `HWND_BOTTOM`. So Send to back skips such windows, with the warning sound.
- **A window without a Minimize button** (`TopLevelWindows.HasMinimizeButton`) isn't minimized, sent to the tray or hidden, with the warning sound.

### The wheel

| Action | What happens |
|---|---|
| None | The wheel reaches the app |
| Change opacity | Each notch up makes the window more opaque and each notch down more transparent, by 10%, between 20% and 100% |

- **Smaller steps,** as from mice that scroll smoothly, add up until they make a notch (120).
- **How:** the window gets the layered style (`WS_EX_LAYERED` with `SetWindowLongPtr`), and its opacity is set with `SetLayeredWindowAttributes` and `LWA_ALPHA`.
- **Back to 100%:** the style is removed again, unless the window had it before.
- **The spike** faded Edge (drawn with DirectComposition), Settings with its content, File Explorer's XAML parts, Character Map and test windows, and kept their rounded corners.
- **Windows that already have the layered style:**
  - **A window that sets its own opacity,** as the command prompt's console does, reports it (`GetLayeredWindowAttributes`). The wheel changes its opacity, and the window gets its own back; its style is never removed.
  - **A window that draws itself with `UpdateLayeredWindow`** reports nothing (`GetLayeredWindowAttributes` fails), and setting its opacity broke its drawing for good in the spike. The wheel leaves such windows alone, and the first notch plays the warning sound.
- **How long it lasts:** until the window closes, the wheel brings it back to 100%, or PC Companion stops; see [Never leaving a window translucent](#never-leaving-a-window-translucent). Opacity isn't remembered for an app.
- **Windows' own animations:** while Windows animates a translucent window minimizing or maximizing, it shows it opaque.

### Clicks and keys

- **Middle-click, Shift + middle-click and Ctrl + middle-click** each choose among the click actions.
- **The wheel and Ctrl + wheel** each choose among the wheel actions.
- **Middle-click and the wheel** mean without Shift or Ctrl. Shift + wheel, and Ctrl + Shift with either, have no action: they reach the app.
- **Alt isn't offered:** releasing it would open the menu bar of the window in front.
- **The Windows key isn't offered:** releasing it opens Start.
- **With Alt or the Windows key held,** nothing runs, and the click reaches the app.
- **The horizontal wheel and wheel tilt** aren't used.

## Apps that keep their title-bar clicks

- **Apps that keep their title-bar clicks,** a list on the page: a listed app's title bars get no actions, and their clicks reach the app as usual. It's for apps that use a middle-click or the wheel on their title bars themselves, such as Firefox, which opens a tab on a middle-click on its tab bar.
- **Adding apps:** as to Hot Corners' list. **Running app** lists the apps with windows on the taskbar, **Program file** picks a program with the Open dialog, and **Remove** takes one out. No app is listed when the feature is first turned on.
- **What counts as one app,** as the taskbar groups them, with the same code as App Windows, Minimize to Tray, Hide Apps and Hot Corners (`AppOfWindow`): the app ID the window sets for itself, as browsers do for each profile; otherwise its Store app package's app ID; otherwise its program file.
- **When the app is read:** only while the list has apps, once for each top-level window the pointer comes to, and kept until that window closes. Reading it waits for no program.

## Apps running as administrator

- **Windows keeps the windows of apps running as administrator away from programs that don't run as administrator.** A copy of the spike labeled Medium integrity confirmed it:
  - Windows refuses such a program the question `WM_NCHITTEST` (error 5), so their title bars aren't recognized and their clicks reach them as usual.
  - Windows also refuses it `SetWindowPos` and the opacity calls.
  - While such an app is in front, Windows gives the program no pointer input, by raw input or by a hook, so no title bar gets actions until another window comes to the front.
- **With Always run as administrator, or with UAC off,** they work like any other window.
- **The page's note** about apps running as administrator follows the host's rights, read from its process when the page opens, as App Windows' note does.

## With the other features

- **Minimize to tray** passes the window to [Minimize to Tray](minimize-to-tray.md), which sends it as its shortcut does, with its own rules. It refuses Store apps in the shared frame, windows with a dialog open, apps running as administrator while it can't hide them, and Drop-down Apps' windows; for each, the warning sound plays.
- **Hide app** passes the window to [Hide Apps](hide-apps.md), which minimizes every window of that window's app as Hide app does, with its own rules.
- **While Minimize to Tray or Hide Apps is off,** the action plays the warning sound, and the click is still kept. The page offers both actions all the time, with a note that they need their feature turned on.
- **How:** within the module, a request carries the window to the other feature's thread (`WindowRequests`), as the features already share `HideAppsMinimized` and `DropDownWindows`. Each feature sets its part as it starts and clears it as it stops.
- **Shift + Minimize:** when Shift + middle-click is set to **Minimize** and Minimize to Tray's **Shift + Minimize** is on, the window minimizes with Shift held, so Minimize to Tray takes it to the tray, as it does a click on the Minimize button with Shift held.
- **Drop-down Apps' windows:** Minimize hides a shown drop-down window, as Drop-down Apps does whenever one is minimized. Minimize to tray and Hide app leave drop-down windows alone, as those features do.

## Never leaving a window translucent

- **The mark:** before it changes a window's opacity, PC Companion sets a window property on it (`WindowMark`, named `PCCompanion.TitleBarActions.Opacity`).
  - **Its value** says whether PC Companion added the layered style, or what opacity the window had set for itself. `WindowMark` grows to keep such a small value, where today it only marks a window.
  - **It's removed** when the window is back at its opacity, and it lives as long as the window.
- **When PC Companion exits** for any reason it's told about: **Exit**, turning the feature off, setup closing it for an update or a removal (`WM_CLOSE`), Restart Manager and the end of the session (`WM_ENDSESSION`). Every window it made translucent goes back to its opacity: the style comes off, or the window gets its own opacity back. Its mark is removed.
- **When PC Companion crashes:** a handler for unhandled exceptions does the same from a list the feature keeps, as Minimize to Tray's does. A failure in the feature does the same.
- **When the host is ended without a chance to run code,** for example from Task Manager, the windows stay translucent until it starts again.
- **At the next start,** on or off, the feature puts back every window that carries the mark.

## Threads

- **No thread of its own:** Title Bar Actions' work is short. The shared pointer hook has its own thread.
- **The host's UI thread** runs:
  - `Apply`, which reads the section, starts or stops raw input, and adds or removes the hook's claims;
  - the pointer moves from `RawPointerMonitor`: finding the window under the pointer, the checks in [Which windows](#which-windows), asking `WM_NCHITTEST` and getting its answer, and claiming the point;
  - the check 60 ms after a click;
  - Minimize and Send to back;
  - passing requests to Minimize to Tray's and Hide Apps' threads;
  - the warning sound.
- **`PointerHook`'s thread** runs the hook and nothing else.
- **The thread pool** changes opacity: `SetWindowLongPtr` sends the window `WM_STYLECHANGING` and waits for it, 1.2 to 2.2 seconds for a window that stopped responding in the spike. One change runs at a time for each window, and notches that come meanwhile add up.
- **Nothing on the UI thread waits for another program's window:**
  - `WindowFromPoint`, reading styles and frames, and `SendMessageCallback` send no message that waits.
  - Posting `SC_MINIMIZE`, `SetWindowPos` with `SWP_ASYNCWINDOWPOS` and `SetLayeredWindowAttributes` took under 2 ms, also for a window that stopped responding (spike).
- **Failures:** a failure is logged, puts back every window's opacity, and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), at 100% scale, with input sent by `SendInput`, confirmed the following. It used test windows it created, Edge, File Explorer with tabs, Settings, Character Map and the command prompt. A read-only probe on a PC asked Chrome's and an Electron app's windows `WM_NCHITTEST` too.

- **Title bars:**
  - Standard title bars answer `HTCAPTION` over the title.
  - Edge, Chrome and the Electron app answer it only between the tabs and on the empty strip, and `HTCLIENT` on tabs.
  - File Explorer answers through a child window, `TITLE_BAR_SCAFFOLDING_WINDOW_CLASS`, and Settings through `ApplicationFrameTitleBarWindow`. Asking their top-level windows gave other answers at 946 and 1,592 points of the title bar.
  - In a narrower Settings window, as the Sandbox checks use, its own content, with its name and a search box, fills most of the title bar and answers `HTCLIENT`; only the strip beside its buttons, `ApplicationFrameTitleBarWindow`, answers `HTCAPTION`.
  - A drag-bar child, as Windows Terminal uses, answered for itself, and an `HTTRANSPARENT` child passed the question to its parent.
- **Speed:**
  - `WindowFromPoint` took 3 µs.
  - A `WM_NCHITTEST` round trip took a median of 36 to 90 µs, and 0.3 to 0.9 ms through `SendMessageCallback`, counting the host's message loop.
- **A window that stopped responding:** `SendMessageCallback` returned in under 1 ms, and the answer came when the window responded, 7.8 seconds later.
- **Letting clicks through:**
  - A middle-click on a background window's title bar activated it first.
  - The wheel and Ctrl + wheel reached the window under the pointer, in the background too.
  - Edge and File Explorer did nothing on a middle-click on their empty title-bar area.
- **The hook:**
  - On all nine windows, it was set 0 to 62 ms after the pointer came to rest, and kept the middle-click: no window was activated or got anything but the action.
  - A tab's click reached the app.
  - The wheel was kept, and five notches gave 50%.
  - Send to back gave the front to the next window.
- **What a hook costs:** while one was set, each move took 1.7 ms instead of 0.8 ms to get through, as Windows waits for the hook.
- **Windows didn't remove the hook** after one call of 1.2 seconds, ten calls of 400 ms in a row, or its thread blocked for 3 seconds. Each slow call made input wait about 300 ms.
- **The check after a click:**
  - With the hook removed without telling the feature, raw input reported the click, the action ran, and the hook was set again for the next click.
  - Raw input reported clicks the hook kept, and reported many before the hook had seen them.
- **Opacity:**
  - The test windows, Edge, Settings, File Explorer and Character Map faded and came back.
  - The console reported its own opacity.
  - A window drawn with `UpdateLayeredWindow` failed `GetLayeredWindowAttributes`, and its drawing failed from then on (error 87).
- **Medium integrity:**
  - A copy labeled Medium integrity was refused `WM_NCHITTEST`, `SetWindowPos`, `SetWindowLongPtr` and `SetLayeredWindowAttributes` on windows of apps running as administrator.
  - It got no pointer input while one was in front.
- **Not checked yet:** a real mouse, touchpad and tilt wheel; the foreground lock; Windows removing the hook after sleep or locking; a real GPU; several displays and scales, and apps that aren't DPI aware; Firefox, Windows Terminal, VS Code, Office, Teams and WinUI 3 apps; games; UAC on; and Windows 10.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Middle-click**, **Shift + middle-click** and **Ctrl + middle-click**, each a list of the click actions;
  - **Wheel** and **Ctrl + wheel**, each a list of the wheel actions;
  - **Apps that keep their title-bar clicks**, with **Running app**, **Program file** and **Remove**;
  - a note that Minimize to tray and Hide app need Minimize to Tray and Hide Apps turned on, shown while an action needs a feature that's off;
  - a note about the windows that get nothing, including apps running as administrator;
  - a **Permissions & data** card.
- **When the feature is first turned on:** middle-click is Minimize, the other clicks and the wheel are None, and no app is listed.
- **The switch on Home** changes only `enabled` and keeps the other settings.
- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log.
- **Unknown values:** an unknown action is None, with a warning.
- **Listed apps** are read as Hot Corners reads its list: a blank app ID or program counts as none, and an app with neither or both, or a `null`, is left out, as is an app listed a second time.

```json
"features": {
  "titleBarActions": {
    "enabled": true,
    "middleClick": "Minimize",
    "shiftMiddleClick": "MinimizeToTray",
    "controlMiddleClick": "SendToBack",
    "wheel": "None",
    "controlWheel": "Opacity",
    "excludedApps": [
      { "name": "Firefox", "program": "C:\\Program Files\\Mozilla Firefox\\firefox.exe" }
    ]
  }
}
```

- `middleClick`, `shiftMiddleClick`, `controlMiddleClick`: `None`, `Minimize`, `SendToBack`, `MinimizeToTray` or `HideApp`.
- `wheel`, `controlWheel`: `None` or `Opacity`.
- `name` is shown on the page only.

## Not included

- **Double-click and right-click,** which Windows uses.
- **Left-click with a key, and other buttons:**
  - Windows starts a drag or opens the window menu on the left and right buttons.
  - Extra buttons belong to shortcuts and mouse buttons as triggers ([#39](https://github.com/Lazyb0y/pc-companion/issues/39)).
- **Close:** a click a few pixels off a tab would close the whole window.
- **Always on top:** free tools for Windows already offer it with a shortcut.
- **Move to another display:** dragging the title bar and Windows + Shift + an arrow key do it.
- **Maximize vertically or horizontally:** double-clicking the top border and Windows + Shift + Up do it.
- **Minimize the other windows:** Windows' title-bar shake and Hide Apps' Hide others do it.
- **Roll up:** window shading ([#27](https://github.com/Lazyb0y/pc-companion/issues/27)) needs its own spike, and becomes a wheel action once it exists.
- **Move to another virtual desktop:** [#30](https://github.com/Lazyb0y/pc-companion/issues/30), which needs undocumented APIs.
- **Different actions for the window in front and the windows behind it,** as KDE Plasma offers: one action per click keeps the page short, and Send to back already handles the window in front.
- **Actions for each app, and remembering an app's opacity:** Window Rules ([#26](https://github.com/Lazyb0y/pc-companion/issues/26)).
- **Recent results on the page:** most clicks that do nothing are on apps that don't tell Windows where their title bar is, which results couldn't show. Debug logs say why a click on a title bar did nothing.
- **Actions anywhere in a window,** such as a key with a click: this feature covers title bars only.
- **The title bars of windows inside an app's own window** (MDI child windows).
- **A switch to let clicks through without the hook:** the app would get each click too, as in [Keeping the click from the app](#keeping-the-click-from-the-app).

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - windows: `WindowFromPoint`, `SendMessageCallback`, `PostMessage`, `SetWindowPos`, `SetForegroundWindow`, `SetWindowLongPtr`, `SetLayeredWindowAttributes`, `GetLayeredWindowAttributes`, `SetProp`, `GetProp`, `RemoveProp`, `IsHungAppWindow`, `IsWindowEnabled`, `DwmGetWindowAttribute`, `GetDpiForWindow` and `PhysicalToLogicalPointForPerMonitorDPI`;
  - input: `SetWindowsHookEx` with `WH_MOUSE_LL`, `RegisterRawInputDevices`, `GetRawInputData`, `GetMessageTime` and `GetAsyncKeyState`;
  - the session: `MessageBeep`.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:**
  - the pointer's position and its buttons and wheel, by raw input, as Hot Corners does, only while the feature is on;
  - for the window under the pointer: its styles, owner, state, frame, DPI and process, and, while the pointer is in its top band, its answer to `WM_NCHITTEST`, which Windows asks it at every pointer move anyway;
  - at a click on a title bar, whether Shift or Ctrl is held;
  - the app of the windows the pointer comes to, only while apps are listed.

  It reads no window titles or contents, and no keys: no keyboard hook.
- **The mouse hook:**
  - It's set only while the pointer rests on a title bar, and sees pointer events only then.
  - It keeps only the middle-click and wheel notches it claimed, and lets everything else through unchanged.
  - Positions and clicks are compared, never stored or logged.
  - It runs in PC Companion's process and loads no code into other apps.
- **Changes:** other apps' windows. It minimizes them, sends them to the back, gives the front to the next window, changes their opacity and style, and adds and removes its mark on them. It passes windows to Minimize to Tray and Hide Apps. It sends no input to other apps and writes no file.
- **Logs:**
  - **Debug entries** for each click or notch on a title bar: which click, which action, whether the hook kept it or it was let through, and why nothing ran, such as a listed app, a window that doesn't respond or a feature that's off.
  - **Never** titles, app IDs, names, paths or pointer positions, which can name documents, profiles or users.
  - **Warnings** when Windows removed the hook, when the settings section can't be read and when a value is unknown.
  - **An error** when a failure stops the feature.
- **Settings:** a changed settings file can only choose among these actions for title-bar clicks and list apps that keep their clicks.
- **The mark:** another program running as the user could mark a window, so that PC Companion gives it full opacity at its next start. It could change that window's opacity itself just as well. Windows refuses a program without admin rights the mark on a window of an app running as administrator.
- **Network:** none. `PRIVACY.md` gets a line on Title Bar Actions.
- **Rights:**
  - It runs with the host's rights: without admin rights, unless Always run as administrator is on or UAC is off.
  - Running as administrator, it also acts on the windows of apps running as administrator, which the user can minimize anyway.
  - Turning the feature off removes the hook's claims, stops reading the pointer, and puts back every window's opacity.

## Performance

- **Away from title bars,** each pointer event costs `WindowFromPoint` and reading the window's frame, a few microseconds on the UI thread. Raw input costs nothing while the pointer is still.
- **In a window's top band,** the window is asked once per move, with at most one question waiting, without waiting for the answer.
- **The hook** is set only while the pointer rests on a title bar. While it's set, every pointer event passes through its thread, which only compares the event with the claims. Setting and removing it takes about 2 µs.
- **Opacity:** `SetLayeredWindowAttributes` took under 2 ms per notch in the spike.

## Accessibility and localization

- **Keyboard:**
  - The page works with the keyboard alone, and every list and button has a label for screen readers.
  - The actions are for the mouse; the page names the keyboard ways to do the same: Windows + Down minimizes, Alt + Esc sends the window in front to the back, and Minimize to Tray and Hide Apps have their own shortcuts.
  - Opacity has no shortcut; see #39.
- **Screen readers:**
  - Minimize, Minimize to tray, Hide app and Send to back of the window in front move the focus to another window, which screen readers announce.
  - Opacity changes nothing a screen reader reads.
  - The feature announces nothing of its own: the host has no UI Automation provider.
- **Colors and layout:**
  - The page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages.
  - The feature draws nothing of its own.
  - Opacity works in high contrast too.
- **Right-to-left windows** answer for their own mirrored title bars, since the question uses screen coordinates.
- **Strings:** the strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake windows, fake answers to `WM_NCHITTEST`, a fake hook, a fake clock and fake raw input:
  - **recognizing a title bar:**
    - `HTCAPTION` counts, and `HTCLIENT`, the icon, the buttons and the borders don't;
    - a child's answer, and `HTTRANSPARENT` passed to the parent;
    - the top band at several DPIs, and the point converted for a window that isn't DPI aware;
    - an answer about where the pointer was, asked again;
    - a question unanswered for 150 ms, dropped;
  - **which windows get nothing:** non-taskbar windows, disabled windows, windows of apps that stopped responding, the shell's windows, listed apps by app ID and by program, and apps whose question Windows refuses;
  - **claims:**
    - a claim at the answered point, give or take 2 pixels, only for the clicks that have an action;
    - Ctrl + Shift let through;
    - the claim removed when the pointer leaves;
  - **the check after a click:** kept, let through and acted on after the answer, and never seen, which sets the hook again and logs a warning;
  - **actions:**
    - Minimize posted, and refused without a Minimize button;
    - Send to back, giving the front away only from the window in front, and skipping always-on-top windows;
    - Minimize to tray and Hide app passed on, and the warning sound while their feature is off;
  - **opacity:**
    - notches added up from smaller steps, 10% per notch, 20% to 100%;
    - the style removed at 100% only when PC Companion added it;
    - a window's own opacity given back, and a window drawn with `UpdateLayeredWindow` left alone;
  - **never leaving a window translucent:** the mark set and removed, every window put back on exit and after a failure, and marked windows put back at the next start, on and off;
  - **the settings:** defaults, the spec's example, unknown values, the app list, and a section that can't be read.
- **Unit tests for `PointerHook`:** claims from two features, the hook set only while a claim exists, a kept press keeping its release, and events posted to the right feature.
- **Unit tests for `WindowMark`'s value:** set, read and removed.
- **Sandbox checks** (`scripts/sandbox/groups/TitleBarActions.ps1`), with test windows the checks start:
  - **the page:** the Home switch turns the feature on with its defaults, and the page saves each choice and the app list, with Running app and Remove;
  - **minimizing:**
    - a middle-click, sent by `SendInput`, on a test window's title bar minimizes it without activating it, and the window gets no click, as its log shows. The test window draws its own title bar, as a browser does;
    - one on its tab doesn't, and the window gets the click;
    - one on the window in front minimizes it too;
  - **Edge:** a middle-click on the empty tab strip minimizes the window, and Edge isn't activated;
  - **File Explorer and Settings,** which answer through child windows, are minimized the same way, Settings from the strip beside its buttons;
  - **send to back:** Ctrl + middle-click sends the window in front to the back, and the next window comes to the front;
  - **the hook** keeps each of these clicks, as the host's log shows;
  - **opacity:**
    - the wheel changes a test window's opacity in 10% steps, the window gets no wheel message, and 100% removes the style;
    - Exit and turning the feature off make it opaque again;
    - ending the host with `Stop-Process` leaves it translucent, and the next start puts it back;
  - **windows that get nothing:**
    - a test window that stops responding gets no action and doesn't hold the host;
    - a listed app's title bar gets no action, and the host logs why;
    - with the host labeled Medium integrity, a test window running as administrator gets no action, and the host logs why;
  - **the hook:** it's set only while the pointer is on a title bar, as the host's log shows;
  - **the other features:** with Minimize to Tray and Hide Apps on, their actions send the window to the tray and hide its app; with them off, the warning sound plays and the host logs why;
  - **the page** in English and Arabic, in light, dark and high contrast, with screenshots;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** a real mouse and touchpad, the foreground lock, several displays and scales, UAC on, Windows 10, Narrator, a real GPU, and apps it doesn't have. Nor can it remove the host's hook without telling the feature: Windows doesn't remove a slow hook with injected input, as the spike showed, so the unit tests check that a click the hook never saw sets it again.
- **By hand:**
  - a real mouse with middle button and wheel, a smooth-scrolling mouse, a touchpad's scrolling and a tilt wheel;
  - Send to back of the window in front on a PC, where the foreground lock applies;
  - the hook after sleep and after locking the PC, and whether Windows removes it on a busy PC;
  - opacity on a PC with a GPU, in Edge, Chrome, Firefox, Windows Terminal, VS Code, Office, a WinUI 3 app and a game in a window;
  - title bars in Firefox, including its middle-click on the tab bar and its picture-in-picture window, Windows Terminal, VS Code, Office, Teams, Notepad and WinUI 3 apps;
  - several displays at different scales, and an app that isn't DPI aware on a scaled display;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - Narrator after each action, high contrast and a right-to-left language;
  - Windows 10.
