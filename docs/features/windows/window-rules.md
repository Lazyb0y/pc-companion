# Window Rules

Rules that act on an app's windows as they open: which display they open on, their place and size, whether they open maximized or minimized, and keep them on top, translucent, off the taskbar or in a tab group.

- Module: Window Management (`PCCompanion.Modules.Windows`). Settings section: `features.windowRules`. Issue: [#26](https://github.com/Lazyb0y/pc-companion/issues/26).
- Inspired by KDE Plasma's window rules, and by the window rules of tiling window managers for Linux.
- Off until the user turns it on.

## Behavior

- **A rule** names an app, and optionally a title or a window class, and has actions. When one of the app's windows opens, the rule's actions apply to it. See [Rules](#rules) and [Matching](#matching).
- **Actions:** the display, the place and size, how the window opens (normal, maximized or minimized), always on top, opacity, hiding it from the taskbar and Alt + Tab, and opening it as a tab of another window. See [Actions](#actions).
- **Placing happens once,** as the window opens, and holds against apps that move their window back; the user can move it afterwards. **On top, opacity and taskbar hiding hold** while the window is open. See [When rules apply](#when-rules-apply).
- **Apply now** applies a rule to the windows already open. **Apply again when the displays change** places the rule's windows again after a display is connected or disconnected.
- **Where I last put it,** a choice for the place, opens the app's next window where the user last left one, for each set of displays.
- **Making a rule:** **Pick a window** fills in a new rule from an open window the user chooses, and the rule shows how many open windows it matches. See [Making rules](#making-rules).
- No window is ever lost or left translucent: whatever happens to PC Companion, windows come back on the taskbar, opaque and on screen; see [Never losing a window](#never-losing-a-window).

## Compared with other systems

- **KDE Plasma's window rules** are the model, and its users report their problems:
  - six ways to apply each setting (Do Not Affect, Apply Initially, Remember, Force, Apply Now and Force Temporarily) that users find confusing, and an editor with dozens of settings, which led to requests for an easy way to choose the screen an app opens on;
  - Apply Initially loses to apps that put their window back where they left it ([468919](https://bugs.kde.org/show_bug.cgi?id=468919)), and Remember keeps one place for all of an app's windows, so they open on top of each other ([479391](https://bugs.kde.org/show_bug.cgi?id=479391));
  - positions are in screen pixels, so changing the display scale means editing every rule ([460398](https://bugs.kde.org/show_bug.cgi?id=460398)).

  Window Rules has two kinds of action, those that place a window as it opens and those that hold while it's open, plus Apply now; it places again when an app moves its window back in the first two seconds; Where I last put it keeps a place for each set of displays, and moves a window that would open exactly over another; and places are fractions of the display's work area, so they survive a change of scale or resolution.
- **GNOME** has no window rules; its Auto Move Windows extension puts an app's windows on a workspace and does nothing else. **macOS** can assign an app to a desktop or a display from the Dock, and tools for it restore saved layouts when the displays change, some only when the very same displays at the same resolutions are connected. Window Rules recognizes a display by its model and serial number on any connection, as Display Profiles does, and places relative to its work area.
- **Tiling window managers** such as i3, Sway and Hyprland have rules in text files, and their guides warn against matching titles, since apps set them late. They distinguish rules applied once, as a window opens, from rules checked again as it changes. Window Rules matches the app first, uses the title only to narrow a rule, and checks a late title for the window's first 10 seconds.
- **Windows** has no rules for apps. Apps put their windows back where they left them, and Windows 11 moves open windows back when a display is connected again. Window Rules works with both: an app that remembers its place reopens where the rule put it, as the spike showed with Edge and Settings.
- **Tools for Windows:**
  - window rules and triggers are paid features in some, which wait a fixed time before checking a new window, 150 ms by default in one, whose help warns that a shorter wait makes them less reliable;
  - Store apps such as Settings can be matched only by the program that hosts every Store app's frame and the window's title;
  - free tools place windows in zones or launch saved sets of apps on request, but don't apply rules by app or title.

  Window Rules is free, places each window as soon as it shows and watches it for two seconds instead of waiting, matches Store apps and each browser profile by the app ID the taskbar uses, and shows which windows a rule matches before it's saved.

## Rules

- A rule has a **name**, up to 60 characters; **the app** and optional **title** and **window class** it matches; **actions**; a switch, so a rule can be turned off without deleting it; and **Apply again when the displays change**, off by default.
- A rule needs an app and at least one action.
- **Up to 50 rules.** In a list edited by hand, the rules after these are left out, with a warning in the log, and the page says so.
- **Order:** the rules are kept in a list, which the page orders with **Move up** and **Move down**; see [Several rules](#several-rules).
- A rule that can't be used, such as one with a title pattern that isn't a valid regular expression, is left out, with a warning in the log that gives its number. The page shows the problem on the rule.

## Matching

### The app

- **The app,** which every rule names, as the taskbar groups apps, with the same code as App Windows, Minimize to Tray and Input Language per App (`AppOfWindow`, kept as a `ListedApp`): the app ID the window sets for itself, as browsers do for each profile; otherwise its Store app package's app ID; otherwise its program file.
- **Store apps in Windows' shared app frame** (`ApplicationFrameWindow`), such as Settings, say which app they are only once their frame shows its content: the spike read no app ID while the frame was cloaked, and the right one once it was uncloaked.
- **Reading it** takes 0.1 to 0.7 ms for each window in the spike, after a first read of 11 to 68 ms in the process, which is why it runs on the feature's own thread.

### Title and window class

- **Title,** optional: **contains** a text, **is** a text, or **matches** a regular expression, case ignored. Read without a message (`InternalGetWindowText`).
- **Regular expressions** use .NET's syntax, culture-invariant and case-insensitive, and give up after 100 ms, as Clipboard Actions' patterns do; a rule whose pattern takes longer doesn't match that window, with a warning in the log.
- **Window class,** optional, under **More matching**: the class name the app registered, such as `CabinetWClass` for File Explorer, matched exactly, case ignored. For apps whose windows all have the same app.
- **A late title:** a title is checked when the window is ready and again each time it changes (`EVENT_OBJECT_NAMECHANGE`) during the window's first 10 seconds, until it matches. In the spike, File Explorer set its title 0.2 s after showing, and Edge 0.25 s after.
- **Once decided, it stays:** a window that matched keeps the rule's held actions while it's open, even when its title changes later, as a browser's does at each tab; a window that didn't match by then isn't matched later. So a rule never moves a window the user is working in.

### Which windows

- **Main windows only:** windows the taskbar shows (`TopLevelWindows.MayHaveTaskbarButton`), with a title bar. Not dialogs, palettes, tool windows, menus, tooltips or splash screens.
- **Never:** the desktop, the taskbar and the shell's other windows; the stand-in Windows shows for a window that stopped responding (class `Ghost`); a window that stopped responding (`IsHungAppWindow`); PC Companion's own windows; Drop-down Apps' windows, windows in Minimize to Tray's tray and Window Tabs' hidden tabs, as the module's shared sets say; and, while PC Companion runs without admin rights, windows of apps running as administrator; see [Apps running as administrator](#apps-running-as-administrator).
- **A window, not a show:** a rule applies to a window once in its life. A window that's hidden and shown again, by its app, by Minimize to Tray, by Hide Apps or as a tab, isn't placed again. Windows are told apart by their handle, and forgotten when they're destroyed (`EVENT_OBJECT_DESTROY`), as Windows reuses handles.

### Several rules

- **All matching rules apply,** combined action by action: for each action, the first rule in the list that sets it wins. So a rule for one window of an app, such as by its title, goes above the rule for the app.
- **The rule editor** says which other rules also match the windows it shows, and which of its actions they override.

## Actions

Each action has a switch in the rule; an action that's off leaves the window as its app made it.

### Display

- **Which display:** a display chosen from the connected ones, **the main display**, or **the display with the pointer** when the window opens.
- **A chosen display** is recognized by its model and serial number on any connection, as Display Profiles recognizes displays (`DisplayRecognition`, `KnownDisplay`), and named by its name from its EDID, such as `DELL U2720Q`. When it isn't connected, the window stays on the display its app chose, and the rule's place applies there.
- **Display without a place:** the window moves to that display at its size, at the same place relative to the work area, made smaller when it doesn't fit.

### Place and size

- **Presets:** **centered** at the window's size; the **left**, **right**, **top** or **bottom half** of the work area; one of its four **quarters**; the **whole work area**, without maximizing; or **custom**.
- **Custom:** left, top, width and height in percent of the work area, from 0 to 100. Percent of the work area, not pixels, so a rule fits any resolution and scale, and a display whose taskbar is elsewhere.
- **Where I last put it:** see [Remembering places](#remembering-places).
- **The frame lines up exactly:** places are for the window's visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`), with the invisible resize borders added (7 pixels on three sides at 100% in the spike), so windows of different apps line up.
- **Within the work area:** a place is always inside the display's work area (`DisplayDetails.WorkArea`), whatever the rule says, so no rule puts a window off screen or under the taskbar.
- **Minimum sizes:** an app that keeps a minimum size larger than the rule's keeps it.
- **A window that opens maximized,** with a place but no **Open** choice, stays maximized, on the rule's display, and gets the rule's place as its normal place, so restoring it goes there.

### Open

- **As the app opens it** (the default), **normal**, **maximized** or **minimized**.
- **Maximized:** on the rule's display, with the rule's place as its normal place, in one step (`SetWindowPlacement` with `SW_SHOWMAXIMIZED`), so restoring it goes there; in the spike, Edge and a test window were maximized in 21 to 64 ms, and restored exactly to their normal place.
- **Minimized:** with the rule's place as its normal place, minimized without taking the front (`SetWindowPlacement` with `SW_SHOWMINNOACTIVE`), so a window that opens as the user works doesn't take their keys. In the spike it was minimized 25 ms after it showed, and restoring it brought it back to its normal place. A window that takes the front as it shows, as one the user starts does, gives it to the window behind it: its program first finishes showing it (`WM_NULL`, sent with `SendMessageTimeout`), then minimizes it as its Minimize button does (`ShowWindowAsync` with `SW_MINIMIZE`), and one that still has the front is restored and minimized again; see [Spike results](#spike-results).
- **Normal:** a window that opens maximized or minimized is restored to the rule's place.

### Always on top

- **On top:** the window is put above every window that isn't on top (`SetWindowPos` with `HWND_TOPMOST` and `SWP_ASYNCWINDOWPOS`), with its owned windows, as Windows does. In the spike it worked on a window in front and on one behind another app's.
- **Held:** when the window's app takes it off the top, the rule puts it back the next time the window comes to the front (`EVENT_SYSTEM_FOREGROUND`), once each time, so the rule and the app never take turns.
- **Off:** the window is taken off the top only if the rule put it there.

### Opacity

- **A fixed opacity,** from 20% to 90% in steps of 10%, or **as last set with the wheel:** the opacity Title Bar Actions' wheel last gave one of the rule's windows, kept for the app's next windows; see [With the other features](#with-the-other-features).
- **How:** as Title Bar Actions changes opacity: the layered style (`WS_EX_LAYERED`, given on the thread pool, since it waits for the app) and `SetLayeredWindowAttributes` with `LWA_ALPHA`. A window that sets its own opacity or color key keeps its color key, and gets its own opacity back. The second run of the spike faded a test window and PC Companion's own WinUI 3 window, which stayed see-through after a resize, and both went back to opaque when the style was put back. Title Bar Actions' spike faded Edge, Settings, File Explorer and Character Map.
- **Windows that draw themselves with `UpdateLayeredWindow`** are left alone, as by Title Bar Actions, since setting their opacity breaks their drawing for good.

### Taskbar and Alt + Tab

- **Hide from the taskbar and Alt + Tab:** the window's taskbar button is removed (`ITaskbarList::DeleteTab`), which on Windows 11 also takes it out of Alt + Tab, as Minimize to Tray's spike showed. The window stays on screen. For windows of apps that live in their notification icon.
- **It stays away** when the app hides and shows the window, minimizes and restores it, or activates it (spike).
- **While minimized,** the window gets its button back (`ITaskbarList::AddTab` when it starts to minimize, `EVENT_SYSTEM_MINIMIZESTART`), and loses it again when it's restored (`EVENT_SYSTEM_MINIMIZEEND`), so a minimized window can always be reached. The second run of the spike confirmed both.
- **When Explorer restarts,** the buttons come back, so they're removed again when the taskbar is created (the `TaskbarCreated` message).
- **Store apps in the shared app frame** are tested by hand.

### Open as a tab

- **Open as a tab of** a window of an app, chosen as the rule's app is: the new window joins the group of that app's front-most window on the same virtual desktop, or starts a group with it, as the tab in front, at the group's place, as Window Tabs' Windows + Ctrl + T does. Choosing the rule's own app puts all its windows in one group.
- **The window it joins** is the app's front-most window that's neither minimized nor on another virtual desktop. When none is open, or Window Tabs is off, the window opens as a window of its own, with the rule's other actions.
- **Window Tabs decides** whether either window can be a tab, on its own thread, and refuses quietly, without its warning sound. A window it refuses opens where its app put it, without the rule's other actions, since the rule asked Window Tabs for it.
- **A window that joins a group** takes the group's place and state, so the rule's other actions don't apply to it: Window Tabs keeps every tab alike.
- **Needs Window Tabs on;** while it's off, the action does nothing, and the page says so at the action. It goes through `WindowRequests` (`GroupWith`), which Window Rules calls from its own thread and Window Tabs passes to its thread, as Title Bar Actions' **Add to the group behind** does.

## When rules apply

### As a window opens

- **When it's ready:** a window is ready when it shows (`EVENT_OBJECT_SHOW`) uncloaked, or, when it shows cloaked, when it's uncloaked (`EVENT_OBJECT_UNCLOAKED`). In the spike, Settings showed cloaked at 136 × 39 pixels and was uncloaked 0.1 to 0.25 s later, and PC Companion's WinUI 3 window 0.65 s later; every other app showed uncloaked.
- **Placing:** at once, with `SetWindowPos` and `SWP_ASYNCWINDOWPOS` for a normal window, or with `SetWindowPlacement` on the thread pool for a maximized or minimized one, since it waits for the app. In the spike, a window placed as it was ready stayed there for Edge, File Explorer, Settings, Character Map, the command prompt, and WinForms and WPF test windows.
- **The settle watch:** for 2 seconds after placing, a hook for `EVENT_OBJECT_LOCATIONCHANGE` in the window's process reports its moves. 150 ms after the last one, the window's frame is compared with the rule's place, and the window is placed again when its app moved it, up to 3 more times. The watch ends early when the user starts moving or sizing the window (`EVENT_SYSTEM_MOVESIZESTART`) or minimizes it. In the spike, test windows that moved themselves back 0.4 s, and 0.4 s and 1.2 s, after showing were placed again once and twice, and were in the rule's place within 1.6 s.
- **The held actions** (on top, opacity and taskbar) apply when the window is ready.
- **Windows open when the feature starts,** as after a restart of PC Companion or an update, get the held actions of their rules, but aren't placed: they're the user's to move.

### Apply now

- **Apply now,** in a rule's **More options**, applies all its actions to the matching windows that are open, placing included.
- **Snapped windows aren't moved:** Windows keeps a moved snapped window snapped (`IsWindowArranged` stayed true in the spike after it was moved), so restoring it would jump. A minimized window gets the rule's place as its normal place and stays minimized. Full-screen windows aren't moved.
- **Turning a rule on** or saving it doesn't place open windows: only Apply now does, so a rule never moves the window the user is working in without being asked. It gives the open windows it matches the actions it holds, on top, opacity and taskbar hiding, as when the feature starts.
- **How the page asks:** through a pipe of the user's session (`WindowRulesPipe`), which the feature's thread serves while Window Rules is on, with the rule as the page shows it, so a change not yet saved still applies. The host answers with how many windows it applied the rule to, and the page says so. While Window Rules is off, Apply now isn't offered.

### When the displays change

- **Apply again when the displays change,** a switch in a rule, off by default: 2 seconds after the last change of displays (`WM_DISPLAYCHANGE`), as when a display is connected, disconnected or a display profile switches, the rule's open windows are placed again, as by Apply now.
- **Off by default,** since Windows 11 already moves windows back to a display that's connected again; the switch is for windows that must always be on a display, such as a dashboard on a TV.

### Remembering places

- **Where I last put it:** the rule keeps the place, size and state (normal or maximized) of the last of its windows the user moved, sized, maximized or restored, and of a window as it closes, and opens the app's next window there.
- **For each set of displays,** recognized by their models and serial numbers: a laptop on its own and with its desk displays keeps a place for each. Up to 10 sets for each rule; the one used longest ago goes.
- **Relative to the display:** the display, and the place as fractions of its work area. When that display isn't connected, the main display, with the place kept inside its work area.
- **Several windows:** a window that would open exactly over another open window of the rule is moved 32 effective pixels down and to the right, as many times as needed while it fits the work area, so an app's windows don't hide each other, as KDE Plasma's do.
- **Until a window is left anywhere,** the rule's other choices apply, or the app's own place.

### Remembered places and opacities in a file

- **Kept** in memory and in `window-rules.json` in the data folder, which only the host writes, on the thread pool, 5 seconds after a change, when the feature stops and when the session may end, as Window Tabs keeps its groups.
- **What's in it:** for each rule that remembers, its place for each set of displays, by model and a hash of the serial number, as Display Profiles keeps them, and the opacity last set with the wheel. Never titles.
- **Forgetting:** an entry goes with its rule. Turning the feature off deletes the file.
- **A file that can't be read,** as after a hand edit, is ignored with a warning in the log and replaced at the next write. It's read only up to 1 MB.
- **Running as administrator:** while the host runs as administrator after a UAC prompt, it neither reads, writes nor deletes the file, since a program without admin rights could put a link in its place; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs). Places and opacities are then remembered for the session only.

## Making rules

- **The page** lists the rules in their order, each with its name, what it does or why it isn't used, its switch, **Edit**, and **More options** with **Apply now**, **Move up**, **Move down** and **Delete**. **Add rule** opens the rule editor with an empty rule.
- **The rule editor,** a dialog:
  - **Name;**
  - **App,** with **Running app**, which lists the apps with windows on the taskbar, **Program file**, which picks a program with the Open dialog, and **Pick a window**;
  - **Title,** and **More options** with **Window class**;
  - **Matches,** the open windows the rule matches now, with their apps and titles, worked out again a moment after each change, and the other rules that also match them;
  - the actions, each with its switch;
  - **Apply again when the displays change.**
- **Pick a window,** in the editor, lists the open main windows, front to back, by their titles and apps, as the editor read them when it opened (`OpenRuleWindows`); a list works with the keyboard and screen readers, and shows windows behind others. The rule then gets the window's app, its title as **contains**, its class under **More matching**, and its display, place as custom and state as the actions, each with its switch off, so the user turns on what they want. The settings app reads the windows itself, and only reads them.
- **One dialog at a time:** Windows shows one of the settings app's dialogs at a time, so **Running app** and **Pick a window** hide the editor, and it shows again, as it was, once they close.
- **Problems** show below their box, and **Save** is available only when the rule has none. **Delete** asks first.

## Never losing a window

- **On screen:** places are kept inside a connected display's work area, also when a display is gone; see [Place and size](#place-and-size).
- **Never invisible:** opacity is 20% at least.
- **Always reachable:** a window hidden from the taskbar gets its button back while it's minimized; see [Taskbar and Alt + Tab](#taskbar-and-alt--tab).
- **The mark:** before a rule puts a window on top or removes its button, PC Companion sets a window property on it (`WindowMark`, named `PCCompanion.WindowRules`), whose value says which it did, and, for opacity, whether it added the layered style or the opacity and color key the window had set for itself. Title Bar Actions keeps a mark of its own. Marks live as long as their windows.
- **When PC Companion exits** for any reason it's told about (**Exit**, turning the feature off, setup closing it, Restart Manager and the end of the session), every window the rules changed gets back what it had: off the top, its own opacity and its taskbar button. Places aren't undone. Turning a rule off or deleting it does the same for its windows.
- **When PC Companion crashes:** a handler for unhandled exceptions does the same from a list the feature keeps, as Minimize to Tray's does. A failure on the feature's thread does the same.
- **At the next start,** on or off, the feature puts back every window that carries a mark, and then, when on, applies its rules' held actions to the open windows again.

## Apps running as administrator

- **Windows keeps the windows of apps running as administrator away from programs that don't run as administrator.** The spike, with a copy labeled Medium integrity, confirmed that Windows refuses such a program moving such a window and putting it on top (error 5), and that removing its taskbar button reports success and leaves the button. It still reads the window's app.
- **So without admin rights,** rules skip such windows, decided by comparing the window's process with the host's (`Elevation.CompareIntegrityLevel`), and log it at Debug level. The page says that Always run as administrator lets rules act on them.
- **With Always run as administrator, or UAC off,** rules act on every window.

## With the other features

- **Window Tabs:** **Open as a tab of** groups windows through it; see [Open as a tab](#open-as-a-tab). Window Tabs' hidden tabs are never matched, and a tab it shows isn't a new window. A window the rules placed and that Window Tabs groups again after a restart takes its group's place.
- **Title Bar Actions:** each feature marks the windows whose opacity it changed, with what they had before it changed them.
  - **The wheel** treats a rule's opacity as the window's own, as for a window that sets its own opacity: it changes the window between 20% and the rule's opacity, and back to the rule's.
  - **As last set with the wheel:** when the wheel changes the opacity of a window whose rule has that choice, Title Bar Actions tells Window Rules (`WindowRequests`), which keeps that opacity for the rule's next windows.
  - **Whichever stops first,** the window ends with its own opacity once both have put it back, also after a crash: Window Rules removes the layered style it added, and Title Bar Actions' opacity needs that style.
- **Minimize to Tray, Hide Apps and Drop-down Apps:** windows in the tray and drop-down windows aren't matched, and a window that comes back from the tray or from Hide Apps isn't a new window.
- **Display Profiles:** switching a profile changes the displays, which rules with **Apply again when the displays change** follow.
- **The module's shared sets** (`HideAppsMinimized`, `DropDownWindows`, `WindowTabsWindows`) are read as Window Tabs reads them.
- **With either feature off,** nothing changes.

## Threads

- **Its own thread:** Window Rules runs on a thread of its own, derived from `MessageLoopThread<TSettings>`, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: it hears every window that shows, reads each one's app, which took up to 68 ms the first time in the spike, and needs a message loop for its hooks.
- **The host's UI thread** runs only `Apply`, which reads the section and passes it to the feature's thread, and, on the first `Apply`, looking for marked windows, which reads every top-level window's properties and waits for no program.
- **The feature's thread** has its own hidden window and runs:
  - the global out-of-context WinEvent hooks for `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_UNCLOAKED`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_OBJECT_DESTROY`, the foreground and minimizing, and, during a settle watch, the location hook for one process;
  - reading each window's app, class, title, styles and place, and matching the rules;
  - placing with `SetWindowPos`, showing with `ShowWindowAsync`, putting on top, setting opacity, and the marks;
  - `WM_DISPLAYCHANGE`, `TaskbarCreated` and `WM_QUERYENDSESSION`.
- **The thread pool** runs `SetWindowPlacement` and giving a window the layered style (`SetWindowLongPtr`), which wait for the app, the taskbar calls (`ITaskbarList`), which wait for Explorer, and reading and writing `window-rules.json`. Each passes its result back with `MessageWindow.BeginInvoke`, one at a time for each window.
- **Nothing on the feature's thread waits for another program's window:** `SetWindowPos` with `SWP_ASYNCWINDOWPOS`, `ShowWindowAsync`, `InternalGetWindowText`, `GetProp`, `SetProp`, `SetLayeredWindowAttributes` and `DwmGetWindowAttribute` don't.
- **Stopping** puts every changed window back, as [Never losing a window](#never-losing-a-window) says, removes the hooks and window, and ends the thread.
- **Failures:** a failure on the feature's thread is logged, puts the windows back, and keeps the feature off until the host restarts, like a failing `Apply`. The other features keep running.

## Spike results

Two runs of a spike in Windows Sandbox, on Windows 11 24H2 (build 26100) at 100% scale on one display of 1520 × 823 pixels, with test windows it created (WinForms, including ones that moved themselves back 0.4 s, or 0.4 s and 1.2 s, after showing, and WPF), Character Map, the command prompt, File Explorer, Settings, Edge and PC Companion's settings app, each opened four times: twice to watch it and twice with a place applied as it was ready.

- **Ready:** every window showed uncloaked except Settings, which showed cloaked at 136 × 39 pixels, was placed by its frame 50 to 160 ms later and uncloaked 0.1 to 0.25 s after showing, and PC Companion's WinUI 3 window, uncloaked 0.65 s after showing. Settings' frame had no app ID until it was uncloaked; another frame of Explorer's showed cloaked and never uncloaked.
- **Placing as the window was ready** held at once for every app; the test windows that moved themselves back were placed again once or twice, and were in place within 1.6 s. File Explorer reported a move to the same place 0.4 to 1 s after showing, and WPF 70 ms after.
- **Apps that remember their place,** Edge and Settings, opened at the place the rule had given them the time before.
- **Titles:** File Explorer's changed four times in its first 0.3 s, Edge's once 0.25 s after showing, and the command prompt's twice.
- **Maximized with a normal place** (`SetWindowPlacement` with `SW_SHOWMAXIMIZED`) worked in one step for a test window and Edge, and restoring went exactly to the normal place. **Minimized** (`SW_SHOWMINNOACTIVE`) as the window showed: minimized 25 ms later, without taking the front.
- **Snapped** with Windows + Left: `IsWindowArranged` was true, `GetWindowPlacement` still gave the place before snapping, and after a move with `SetWindowPos` it was still true.
- **On top** worked on the window in front, on a window behind while the spike was in front, and on a window behind while a third app was in front. The Sandbox applies no foreground lock, so this is tested by hand on a PC.
- **Taskbar:** removing the button kept it away through hiding and showing by the app, minimizing, restoring and activating; Explorer's restart brought it back, and removing it again worked. Adding it back while the window was minimized showed it, restoring kept it, and removing it again worked.
- **Opacity:** a test window and PC Companion's WinUI 3 window became see-through at 140 of 255, stayed so after a resize, and were opaque again once the style was put back. Giving the layered style took under 1 ms.
- **Apps running as administrator:** from the copy labeled Medium integrity, putting a window of an app with admin rights on top failed (error 5), removing its taskbar button reported success and left it, and reading its app and program worked.
- **Reading a window's app:** 0.05 to 0.7 ms, after 11 to 68 ms for the first read in the process; its program, about 0.1 ms after 2 to 11 ms.
- **Events:** about one a second while nothing opened. Opening an app made 30 to 340 events in 3 to 6 seconds, of which 1 to 3 were windows showing at the top level; Character Map also made 200 top-level location changes in 3 seconds, which is why no location hook covers every process.
- **Opening minimized,** in three later Sandbox spikes with a test window that a script started and that took the front as it showed: `SW_SHOWMINNOACTIVE` left it in front while minimized, so the keys went to it; `SW_MINIMIZE` gave the front to the window behind it, at once from the window's own thread (`ShowWindowAsync`) and after about 300 ms from another process (`SetWindowPlacement`). A minimized window in front kept the front through `SW_MINIMIZE` and `SetWindowPlacement` and gave it up only when it was restored and minimized again. The Sandbox checks of the feature, whose thread is quicker than the spikes', saw the window take the front just after it was minimized, which is why its program finishes showing it first.
- **A window that closes,** read as its hide reached another process (`EVENT_OBJECT_HIDE`, out of context), in a later check on a PC with 40 WinForms test windows that showed off screen without taking the front and closed: every placement could still be read, and every visible frame but one, the first, whose window was destroyed as it was read. So a closing window's frame comes from its placement when it can't be read.
- **Virtual desktops:** which desktop a window of another app is on could be read (`IVirtualDesktopManager::GetWindowDesktopId`), but moving one is refused, which is why rules don't move windows between desktops.
- **Not checked,** by hand: several displays and scales, the foreground lock, UAC on, Windows 10, Store apps in the shared frame hidden from the taskbar, and Electron apps.

## Settings

- **Feature page:** the on/off switch; **Rules**, the list with **Add rule**; a note about apps running as administrator; and a **Permissions & data** card.
- When the feature is first turned on, there are no rules.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Window Rules off with a warning in the log. A `null` in a list is left out. A rule with a choice this version doesn't know, such as one from a newer version, keeps it when the settings are saved, and isn't used.

```json
"features": {
  "windowRules": {
    "enabled": true,
    "rules": [
      {
        "name": "Notes on the right",
        "enabled": true,
        "app": { "name": "Notepad", "appId": "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" },
        "title": { "match": "Contains", "text": "Notes" },
        "display": { "which": "Chosen", "name": "DELL U2720Q", "model": "DEL41A3", "serial": "4c1f9e0b2a7d3e85", "builtIn": false },
        "place": { "which": "RightHalf" },
        "open": "Normal",
        "onTop": true,
        "opacity": { "which": "Fixed", "percent": 90 },
        "applyOnDisplayChange": false
      },
      {
        "name": "Explorer in one group",
        "enabled": true,
        "app": { "name": "File Explorer", "program": "C:\\Windows\\explorer.exe" },
        "windowClass": "CabinetWClass",
        "tabOf": { "name": "File Explorer", "program": "C:\\Windows\\explorer.exe" }
      },
      {
        "name": "Music where I left it",
        "enabled": true,
        "app": { "name": "Music player", "program": "C:\\Program Files\\Music\\Music.exe" },
        "place": { "which": "Remembered" },
        "hideFromTaskbar": true,
        "opacity": { "which": "Wheel" }
      }
    ]
  }
}
```

- `app` and `tabOf`: an app as other features' lists keep one, with an `appId` or a `program`, not both.
- `title.match`: `Contains`, `Is` or `Pattern`.
- `display.which`: `Main`, `UnderPointer` or `Chosen`, which also has the display's `name`, `model`, `serial`, `connection` and `builtIn`, as Display Profiles keeps a display.
- `place.which`: `Centered`, `LeftHalf`, `RightHalf`, `TopHalf`, `BottomHalf`, `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `WorkArea`, `Remembered`, or `Custom` with `left`, `top`, `width` and `height` in percent.
- `open`: `Normal`, `Maximized` or `Minimized`; left out, the app chooses.
- `opacity.which`: `Fixed` with `percent`, from 20 to 90 in steps of 10, or `Wheel`.
- An action that's left out, or `false`, is off.
- `id`: a key the settings app gives each rule as it's added, which what the rule remembers is kept by, so renaming or moving it keeps that; a hand-written rule without one is known by its name.

## Not included

- **No title bar or frame:** apps that draw their own title bar, such as browsers, Store apps and WinUI 3 apps, can lose their buttons and sizing, and an app can put its frame back.
- **Moving windows to another virtual desktop:** Windows refuses it for other programs' windows except through undocumented interfaces; see [#30](https://github.com/Lazyb0y/pc-companion/issues/30).
- **Holding a place,** so the user can't move the window: it fights the user and the app.
- **Windows' snap layouts and snap groups:** Windows has no way for a program to snap another program's window.
- **Focus rules,** keeping a window below others, minimum and maximum sizes, a process's priority, closing windows, and a shortcut for each window.
- **Starting apps,** or sets of apps with their places: free tools for Windows do it.
- **Zones and layouts** that windows are dragged into: free tools for Windows do it.
- **Title-bar actions for each app:** [#361](https://github.com/Lazyb0y/pc-companion/issues/361).
- **Dialogs and tool windows.**
- **Rules for apps running as administrator while PC Companion runs without admin rights:** Windows refuses it.
- **Actions for Shortcuts & Mouse Buttons and Launcher Actions,** such as applying the rules to the window in front.
- **Rules that depend on the network, the power source or the time:** with profiles and context triggers ([#56](https://github.com/Lazyb0y/pc-companion/issues/56)).

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - windows: `SetWindowPos`, `SetWindowPlacement`, `GetWindowPlacement`, `ShowWindowAsync`, `InternalGetWindowText`, `GetClassName`, `IsHungAppWindow`, `SetProp`, `GetProp`, `RemoveProp`, `SetWindowLongPtr`, `SetLayeredWindowAttributes` and `GetLayeredWindowAttributes`;
  - `IsWindowArranged`, from Windows 10 version 1903, read with `GetProcAddress` since the SDK has no header for it;
  - events and displays: `SetWinEventHook`, `DwmGetWindowAttribute`, `MonitorFromWindow` and `GetMonitorInfo`, and Display Profiles' display recognition;
  - the taskbar: `ITaskbarList`.
- No undocumented API is used.
- **Windows 10:** whether removing a taskbar button also takes the window out of Alt + Tab is tested by hand; the spike ran on Windows 11 only.

## Security and privacy

- **Reads:**
  - **For each window that shows,** if it could be a main window: its app identity, class, title, styles, state and place, to match the rules, while its rules are decided, and Windows reports when it shows, is uncloaked, changes its title, moves during a settle watch, comes to the front, minimizes and closes.
  - **The displays,** by model and serial number, as Display Profiles reads them, when the displays change.
  - **Pick a window,** in the settings app: the open main windows' apps, titles, classes, displays and places, and those of the window chosen.
  - It receives no keys or clicks: no keyboard or mouse hook.
- **Titles** are compared in memory as the window opens and never written or logged. The settings app shows the titles of the windows a rule matches in its editor; any program of the user's can read a window's title anyway. A rule's title text and pattern, which the user writes, are in `settings.json`, and can name a document.
- **Changes:** other apps' windows: places, sizes, maximizes, minimizes, puts on top, changes their opacity and layered style, removes and adds back their taskbar buttons, and adds and removes its marks. It runs no code in other apps and sends them no input.
- **Regular expressions** are the user's own, and are matched with a 100 ms limit, so a pattern can't hold the feature's thread.
- **`window-rules.json`:** places for each rule that remembers, by display model and a hash of the serial number, and opacities. Kept on this PC in the data folder; turning the feature off deletes it, and uninstalling removes it with the rest of the data folder. Not read or written while the host runs as administrator after a UAC prompt.
- **Settings and the file** can only place, show and style windows of the apps they name, so a changed file can't make PC Companion do more.
- **The marks:** another program running as the user could mark a window so that PC Companion puts it back at its next start; it could do that itself just as well. Windows refuses a program without admin rights the mark on a window of an app running as administrator.
- **Logs:**
  - **Debug entries** when a rule applies, with its number and which actions, when a window is skipped and why, when an app moves a window back during a settle watch, and when places are remembered, read or saved.
  - **Never** titles, app IDs, names, paths or displays' serial numbers.
  - **Warnings** when the settings section or the file can't be read, a rule can't be used or a pattern takes too long, with the rule's number, and when the file can't be written or deleted.
  - **An error** when a failure stops the feature.
- **Network:** none. `PRIVACY.md` gets a line on Window Rules.
- **Rights:** runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Running as administrator, it also acts on the windows of apps running as administrator, which the user can move and style anyway.

## Performance

- **With no rules,** the feature sets no hooks.
- **With rules,** the global hooks report each window that shows, is uncloaked, changes its title or closes, and the foreground and minimizing: about one event a second while nothing opened in the spike, and 30 to 340 in 3 to 6 seconds while an app opened, of which 1 to 3 were windows showing at the top level. Events for windows that aren't main windows, and title changes past a window's first 10 seconds, are dropped at once.
- **Each new main window** costs reading its app, 0.05 to 0.7 ms in the spike, and matching the rules.
- **The settle watch** hooks one process's moves for 2 seconds after a window is placed.
- **No timer runs** while no window is opening, except the 2-second wait after a change of displays.

## Accessibility and localization

- **Keyboard:** the page and the rule editor work with the keyboard alone, and every box, switch and button has a label for screen readers. **Pick a window** is a list, which works with the keyboard and screen readers.
- **Screen readers:** **Matches** announces how many windows a rule matches when it changes.
- **Right to left:** the page and the editor are laid out right to left in right-to-left languages. Halves and quarters are on the screen's left and right, whatever the language, and say so.
- **Strings:** the page, the editor and the presets are translated like the other pages', marked "Needs native review"; the count of matching windows uses plural forms.

## Testing

- **Unit tests,** with fake windows, a fake clock, fake displays and fake Windows calls:
  - **matching:** app ID before program, Store frames matched once uncloaked, titles that contain, are or match a pattern, case ignored, a pattern that times out, a late title within 10 seconds and not after, window classes, and each kind of window that's never matched;
  - **several rules:** actions combined, the first rule that sets each winning;
  - **placing:** presets and custom places in the work area, invisible borders, a missing display, a display without a place, places kept on screen, and a window that opens maximized keeping the rule's normal place;
  - **the settle watch:** placed again after an app's move, at most 3 times, not after the user moves the window, and over after 2 seconds;
  - **open:** maximized with its normal place, minimized without the front, and normal;
  - **held actions:** on top put back once when the window comes to the front, opacity within 20% to 90%, and the taskbar button back while minimized and after Explorer restarts;
  - **Apply now and display changes:** snapped, minimized and full-screen windows, and the 2-second wait;
  - **remembering:** a place for each set of displays, the limit of 10, a missing display, the opacity from the wheel, the file's contents, a file that can't be read, and no file while running as administrator after a UAC prompt;
  - **with the other features:** grouping through Window Tabs and its refusals, opacity shared with Title Bar Actions as either stops, and windows of the shared sets left alone;
  - **never losing a window:** marks, putting windows back on exit, turning a rule off and at the next start, and the crash list;
  - **apps running as administrator** skipped without admin rights;
  - **the settings:** the spec's example, unknown choices kept, limits, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/WindowRules.ps1`), with test windows the checks start:
  - **the page:** the Home switch turns the feature on with no rules; a rule saved from the editor with Running app, and one from Pick a window, are in the settings; Matches shows the open test windows;
  - **placing:** a test window opens at a rule's place and display, and one that moves itself back is placed again; Edge, File Explorer and Settings open at a rule's place; maximized and minimized work, and restoring goes to the rule's place;
  - **held actions:** on top, opacity and taskbar hiding apply, the taskbar button comes back while minimized, and turning the rule off puts them back;
  - **Apply now** places an open test window, and leaves a snapped one;
  - **Open as a tab of** groups two test windows, with Window Tabs on;
  - **Where I last put it:** a window moved by the checks and closed opens there next time, and the file holds no title;
  - **never losing a window:** **Exit** puts the windows back; ending the host with `Stop-Process` leaves them marked, and the next start puts them back;
  - **apps running as administrator:** with the host's file labeled Medium integrity, a window running as administrator is left alone;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** several displays and scales, display changes, the foreground lock, UAC on, Windows 10, Narrator and Electron apps.
- **By hand:**
  - several displays at different scales: a chosen display, the pointer's display, a display that's disconnected, Apply again when the displays change, and Where I last put it with a laptop on its own and at a desk;
  - always on top on a PC, where the foreground lock applies, for a window that opens behind another;
  - taskbar hiding on Windows 10 and for Store apps in the shared frame;
  - real apps: Office, Electron apps such as Visual Studio Code, Teams and Discord, a browser with two profiles, and an app that restores its own place late;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - Narrator on the page and in the editor, high contrast, a right-to-left language, and Windows' Text size;
  - Windows 10.
