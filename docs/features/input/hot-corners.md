# Hot Corners & Edges

Move the pointer into a corner or push it against an edge of the screen to run an action, such as showing all windows or locking the PC, or turn the wheel there to change the volume or switch desktops.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.hotCorners`, kept from when the feature had only corners. Issue for the edges and the wheel: [#37](https://github.com/Lazyb0y/pc-companion/issues/37).
- Inspired by macOS Hot Corners, the Activities hot corner in GNOME, and screen edges in KDE Plasma.
- Off until the user turns it on.

## Behavior

- **Eight places:** the four corners and the four edges of the screen. Each can run one action when the pointer reaches it, and another when the wheel turns on it, or nothing. See [Corners and edges](#corners-and-edges).
- **Corners** trigger when the pointer reaches one and stays for the **activation delay**: 150 ms by default, adjustable from 0 to 1,000 ms. With **Corners trigger** set to **When pushed**, they trigger as edges do instead.
- **Edges** trigger only when the pointer keeps pushing against them, never while it just rests there, since the pointer rests on edges all the time: on a maximized window's scroll bar, on browser tabs and on the taskbar. See [Pushing](#pushing).
- **The wheel,** turned with the pointer on a corner or edge that has a wheel action, changes the volume, switches desktops or switches tracks, and the app under the pointer doesn't get it. See [The wheel](#the-wheel).
- After a corner or edge triggers, it doesn't trigger again until the pointer has left it, and not within 350 ms, or the activation delay and 50 ms if that's longer, even if the pointer leaves and comes back: most actions toggle, such as Task View, Show desktop and Start, so a hand that slips out and back would undo the action at once.
- A corner or edge can require a key: Ctrl, Alt, Shift or the Windows key must be held when the delay ends or the push is enough, whether it was pressed before or after the pointer got there. This avoids accidental triggers, as in macOS. The wheel has a key of its own; see [The wheel](#the-wheel).
- A corner or edge doesn't trigger, and its wheel turns reach the apps:
  - when a mouse button is held as the pointer reaches it or when it would trigger, because dragging a window to a corner or edge snaps it there;
  - while an app has captured the pointer: confined it to part of the screen, as games and virtual machines do, or hidden it, as browser games do;
  - while an app is full screen on its display, such as a game, a video or a presentation, unless the user turns this off. A full-screen app on another display doesn't stop it;
  - while an app the user listed is in front, on any display;
  - on the lock screen or a UAC prompt, which Windows shows on a separate desktop.
- **An edge where a taskbar hides itself** has no push action: pushing there shows the taskbar. Its wheel action works; see [Taskbars that hide themselves](#taskbars-that-hide-themselves).
- See [Full screen and a captured pointer](#full-screen-and-a-captured-pointer) and [Apps that turn corners and edges off](#apps-that-turn-corners-and-edges-off) for how these are checked.
- A corner or edge the pointer is already on when the feature starts, or when its settings or the displays change, triggers only after the pointer leaves it and comes back; saving other settings, such as another feature's, doesn't.
- The feature page shows the last few results, such as which corner ran which action, or why an edge didn't trigger; see [Recent results](#recent-results).

## Compared with other systems

- **macOS** has hot corners only, with an optional key, and nothing for edges or the wheel. Tools for it change the volume with the wheel over the menu bar, for a price.
- **KDE Plasma** gives the four corners and four edges an action each, triggered after a delay, with a short wait before one triggers again. It refuses wheel actions, and users report desktops switching by accident, edges firing in games, and pointers that stick between displays.
- **GNOME** triggers its one corner when the pointer pushes into it, rather than after a delay. Its most used corners extension stretches corners into edges and adds wheel actions, such as the volume, with an optional Ctrl.
- **Windows** has no actions for corners or edges. Turning the wheel over the speaker icon in the notification area changes the volume, and only there.
- **Tools for Windows** change the volume with the wheel at an edge or over the taskbar, or switch desktops with the wheel at an edge, mostly without checks for games or full-screen apps; corner tools check the pointer many times a second, and stop working while an app running as administrator is in front.
- **Hot Corners & Edges** gives corners and edges both push and wheel actions, needs a real push at edges so a resting pointer never triggers one, leaves the wheel to the apps everywhere but on the edge's outermost pixels, keeps out of full-screen apps and listed apps on their own display, says why a corner or edge didn't trigger, and costs nothing while the pointer is away from them.

## Corners and edges

- **A corner** is the corner pixel of a display, as before.
- **An edge** is the outermost line of pixels of a display's side, where the pointer stops. A part of a side next to another display isn't an edge: the pointer passes into the other display there.
- **Near the corners:** the 40 effective pixels at each end of an edge don't trigger its push action, as in KDE Plasma, so a pointer sliding along an edge into a corner triggers the corner. The wheel there is the edge's.
- **Windows' "Ease cursor movement between displays"** can move the pointer from part of a side to the next display instead of stopping it there. The pointer has then left the edge, so nothing triggers.
- **The same for every display:** each corner and edge has one set of actions, used on every display, as before ([#121](https://github.com/Lazyb0y/pc-companion/issues/121) would give each display its own).
- **Names are physical** ("Top left", "Right edge") in every language.

## Pushing

- **Inspired by** GNOME's hot corner, which triggers on pressure against the edge, and by the edge resistance of KDE Plasma and XFCE.
- **How:** while the pointer is on an edge, the raw input the host already receives keeps reporting the mouse's movement outward, though the pointer can't move further (spike). The movement outward within the last second adds up, and the edge triggers when it reaches the **push strength**:
  - **Light:** 50 counts;
  - **Medium,** the default: 100 counts, close to GNOME's 100 pixels within a second;
  - **Firm:** 200 counts.
- **Counts** are the mouse's own units, which depend on its resolution and not on the pointer speed. The values are checked by hand with several mice and a touchpad, and may change before release.
- **Moving along the edge:** when the pointer has moved more than 16 effective pixels along the edge from where the push began, the push starts again from there, as in XFCE. So a pointer sliding along an edge doesn't trigger it.
- **Getting there isn't a push:** the move that brings the pointer to an edge doesn't count, since a quick flick to a scroll bar can move the mouse as far in one event.
- **Corners, when pushed** ([#120](https://github.com/Lazyb0y/pc-companion/issues/120)): movement outward on either side adds up, with the same strength. The delay stays the default, since some people can't push steadily.
- **Pen, touch, remote sessions and many virtual machines** report positions, not movement (spike), so nothing adds up. For them, a corner waits for the activation delay, and an edge for 500 ms, or the activation delay if that's longer.
- **The checks** in [Behavior](#behavior), such as the key and full screen, run once, when the push is enough or the delay ends, as before.

## The wheel

| Action | Wheel up | Wheel down | How |
|---|---|---|---|
| None | Reaches the app | Reaches the app | The hook doesn't claim it |
| Volume | Louder | Quieter | The Volume Up and Volume Down keys, which change the volume by 2% and show Windows' volume display (spike) |
| Desktops | Previous desktop | Next desktop | Ctrl + Windows + Left and Right; nothing happens past the first or last desktop (spike) |
| Tracks | Previous track | Next track | The Previous Track and Next Track keys, which Windows passes to the app playing media |

- **Where:** on an edge's outermost line of pixels, its ends included, or on a corner's pixel.
- **Steps:** each notch (120) is one step. Smaller steps, as from mice that scroll smoothly, add up until they make a notch, as in Title Bar Actions.
- **Desktops and tracks** take at most one step every 300 ms, so a wheel that spins freely doesn't race through them. Notches in between are kept and do nothing. Two switches 50 ms apart both worked in the spike, so the limit is for the user, not for Windows.
- **Keys:** each wheel action can require a key, held as the wheel turns, separately from the push action's key. The keys must match exactly, as for the pointer hook's other claims. With the Windows key, each kept notch sends the unassigned key, as Shortcuts & Mouse Buttons does, so releasing the Windows key doesn't open Start.
- **Why keys and not an API:** Windows' audio API changes the volume without showing the volume display (spike), and Windows has no documented API to switch desktops; keys are sent as the actions' shortcuts are.
- **What the apps don't get:** the shared pointer hook (`PointerHook`) keeps the wheel only on the corner's pixel or the edge's outermost line of pixels; one pixel inside, the app gets it (spike).
  - **When it claims:** only while the pointer is on a corner or edge with a wheel action, and only when the checks in [Behavior](#behavior) pass as the pointer gets there: no full-screen app on that display, no listed app in front. The claim goes when the pointer leaves. So the hook isn't set while the pointer is away from them.
  - **A captured pointer:** while an app confines or hides the pointer, the hook lets the wheel through (`NotWhileCaptured`).
  - **Claims that overlap:** on a maximized window's title bar along the top edge, Title Bar Actions' wheel claim can overlap. The edge's claim wins on its line, since the user chose an action for the screen's edge: `PointerClaim` gets `First`, which Hot Corners & Edges sets.
- **What's under the edges:** a maximized window reaches the right and left edges, with its scroll bar at or next to the right one; its title bar or a browser's tabs are along the top edge; the taskbar is usually along the bottom edge. The page says that a wheel action on the right edge takes the wheel from a maximized window's scroll bar there, and that on the top edge it takes it from Title Bar Actions there.
- **Results:** wheel turns aren't recent results: each shows its effect at once, as the volume display or the switch.

## Actions

| Action | What happens | How |
|---|---|---|
| Task View | Shows all open windows and virtual desktops | Windows + Tab |
| Show desktop | Shows the desktop; again brings the windows back | Windows + D |
| Start | Opens Start | Windows key |
| Search | Opens Windows search | Windows + S |
| Notifications | Opens the notification center (Windows 11) or the Action center (Windows 10) | Windows + N, or Windows + A on Windows 10 |
| Previous desktop | Switches to the virtual desktop on the left | Ctrl + Windows + Left |
| Next desktop | Switches to the virtual desktop on the right | Ctrl + Windows + Right |
| Lock | Locks the PC | `LockWorkStation` |
| Turn off displays | Turns the displays off until the pointer moves | `SC_MONITORPOWER` |
| Screen saver | Starts the screen saver | `SC_SCREENSAVE` |
| Keep displays on | While the pointer stays in the corner or on the edge, the screen saver doesn't start and the displays don't turn off | A power request; see [Keep displays on](#keep-displays-on) |
| Run a program | Starts the program chosen for the corner or edge | Started directly, without a shell; see [Run a program](#run-a-program) |

- Every action is offered for every corner and edge. Previous desktop and Next desktop suit the left and right edges, and give corners the desktop actions of [#152](https://github.com/Lazyb0y/pc-companion/issues/152).
- Shortcuts are sent as keyboard input only where Windows has no API for the action.
- A modifier the user is holding, such as the key a corner requires, is released before the shortcut is sent: Ctrl + Windows + D would add a virtual desktop. An unassigned key is pressed first, so releasing the Windows key or Alt doesn't open Start or the menu bar.
- Screen saver does nothing when no screen saver is chosen in Windows settings.

## Keep displays on

- Inspired by Disable Screen Saver in macOS Hot Corners. Unlike the other actions, which run once, it lasts while the pointer stays in the corner or on the edge.
- It triggers like the other actions, with the same delay or push, key and exceptions.
- While it lasts, the screen saver doesn't start, the displays don't turn off and the PC doesn't go to sleep on its own.
- It ends when the pointer leaves the corner or edge, the corner or edge gets another action, the feature is turned off or stops, or the PC is locked or switched to another user. Locking ends it so the displays turn off on the lock screen as usual; after unlocking, the pointer must leave and come back.
- Other changes to the settings or the displays don't end it while the pointer is still on a corner or edge with this action.
- It doesn't stop the user: Sleep in Start, the power button and closing the lid work as usual, and Windows ends the request then.
- **How:** a power request (`PowerCreateRequest`, then `PowerSetRequest` for the display and the system), released with `PowerClearRequest`. Its reason, "Hot Corners & Edges: the pointer is on a corner or edge that keeps the displays on.", is shown by `powercfg /requests` in the user's language. Session locks come from `WTSRegisterSessionNotification`.
- It changes no screen saver or power setting, and Windows ends the request when the host exits, so nothing is left behind if PC Companion stops unexpectedly.
- Policies that limit what apps can keep awake, such as `powercfg /requestsoverride` or Group Policy, apply to it as to any app.

## Run a program

- Close to the custom command in KDE Plasma, Cinnamon and XFCE. Each corner or edge with this action has its own program and arguments.
- **Program:** the full path of an `.exe` file, typed or chosen with **Browse**. A name without a folder, a relative path or another type of file doesn't run: Windows would otherwise search folders for a program of that name, and it runs `.bat` and `.cmd` files through cmd, which reads arguments differently. The path is used as written; environment variables aren't expanded.
- **Arguments:** optional, in one box. They're split by the Windows command-line rules and passed to the program one by one (`ProcessStartInfo.ArgumentList`), never through a shell, so pipes and redirection don't apply. To run a script, choose `powershell.exe` or `cmd.exe` as the program.
- The program starts in its own folder, with the user's rights, and runs on its own: PC Companion doesn't wait for it, watch it or stop it.
- It's checked and started on the thread pool, not the host's UI thread: Windows can take many seconds to start a program, for example from a network share that's offline, and the other corners, edges and features don't wait meanwhile. A failure there is logged and leaves the feature running.
- A program that needs admin rights doesn't start, because Windows refuses it without asking; a warning is logged.
- **The program never gets admin rights, also while PC Companion runs as administrator.** Any program running as the user can change the settings file, so that file must never make PC Companion run code as administrator. The host starts the program through `IHostCommands.StartProgram`: elevated through UAC, it has a copy of itself without admin rights start it; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs).
- A corner or edge without a program, or whose program is missing or isn't an `.exe`, does nothing and logs a warning when it triggers. The feature page shows the problem below the box.

## Full screen and a captured pointer

- Inspired by GNOME, which checks full screen for each display. KDE Plasma checks only the active window, and Windows' own full-screen state (`SHQueryUserNotificationState`) sees exclusive full screen but not a borderless game, and doesn't say which display a full-screen app is on.
- Both checks run only when a corner or edge would trigger, and as the pointer reaches one with a wheel action, never on each pointer move.
- **Captured pointer:** the corner or edge is skipped when the pointer is confined to less than the area of all displays (`GetClipCursor`), or hidden (`GetCursorInfo`). A pointer that Windows hides during touch or pen input doesn't count. This applies whether or not **Don't trigger when an app is full screen** is on: a corner that an app pins the pointer to isn't one the user aimed for. For the wheel, the hook checks it at each notch.
- **Full screen, for the corner's or edge's display:** PC Companion looks at the visible top-level windows from front to back (`EnumWindows`) and skips:
  - windows that are cloaked, as on another virtual desktop, or minimized, or that don't overlap the display;
  - overlays: click-through windows (`WS_EX_TRANSPARENT`), windows that can't be activated (`WS_EX_NOACTIVATE`) and tool windows, such as the overlays of graphics drivers, game launchers and chat apps;
  - always-on-top windows that don't cover the whole display, such as the taskbar or a picture-in-picture video.
- The first window left decides. The display is full screen when that window covers the whole display, has no title bar or resizable frame (`WS_CAPTION`, `WS_THICKFRAME`), and doesn't belong to the Windows shell's process, which shows the desktop and Task View. So a maximized window, which keeps its frame, isn't full screen, even with a taskbar that hides itself.
- Windows' own state adds two cases: exclusive full screen (`QUNS_RUNNING_D3D_FULL_SCREEN`) counts on the display of the window in front, which such an app always is, and presentation mode (`QUNS_PRESENTATION_MODE`) counts on every display. Its busy state (`QUNS_BUSY`) isn't used, since it doesn't say which display: a video on one display stopped the corners of every display.
- A skipped corner or edge logs why at Debug level: the pointer was confined or hidden, or an app was full screen.

## Apps that turn corners and edges off

- Like the app exceptions of hot corner tools for macOS, and a frequent request for hot corner tools on Windows. For apps that need their corners and edges, or shouldn't get their shortcuts: remote desktop and virtual machine windows, where the shortcut can reach the other PC, drawing and design apps, and games that the full-screen checks miss.
- While a listed app is in front, no corner or edge triggers, on any display, and wheel turns reach the apps: a shortcut goes to the app in front wherever the corner or edge is. This applies whatever the full-screen setting. The list can only turn corners and edges off, never on.
- **What counts as one app**, as the taskbar groups them, with the same three identities as App Windows: the app ID that the window in front sets for itself, as browsers do for each profile; otherwise the app ID of its Store app package; otherwise its program file. An app ID matches whichever way it was read, and a program matches by its full path, ignoring case. Both features use the same code for it, `AppOfWindow` in `PCCompanion.Platform.Windows`. An app that moves its program file when it updates, as some apps installed in a folder named after their version do, must be added again.
- **When:** once per trigger, when the delay ends or the push is enough, after the other checks, and as the pointer reaches a corner or edge with a wheel action; only while the list has apps. It reads the window in front's app ID, a property of the window, and its process's package and program path (`OpenProcess` with `PROCESS_QUERY_LIMITED_INFORMATION`). None of these waits for the app, so a trigger never waits for another program.
- While an app running as administrator is in front, corners and edges don't trigger anyway unless PC Companion runs as administrator; then the list works for that app too.
- A skipped corner or edge logs `AppExcluded` at Debug level, never which app.

## Taskbars that hide themselves

- **Why:** pushing against the edge where a taskbar hides itself is how it's shown, so an edge action there would run each time the user wants the taskbar.
- **Which edge:** Windows says, for each edge of a display, which bar hides itself there (`SHAppBarMessage` with `ABM_GETAUTOHIDEBAREX`), for the taskbar and for other programs' bars (spike).
- **When:** as the pointer reaches an edge with a push action, on the thread pool, since the answer comes from Explorer, which can stop responding. A push that's enough before the answer comes uses the edge's last answer, or, before the first, counts no bar.
- **Skipped:** the push action doesn't run, and the result says the taskbar hides itself on that edge. The wheel and the corners at its ends work as usual.

## Recent results

- No other desktop or tool says why a corner didn't trigger: macOS, KDE Plasma, GNOME and hot corner tools for Windows log nothing the user can see, and "hot corners stopped working" is a common complaint on each.
- The host keeps the last 5 results in memory, each with its time, corner or edge, action and outcome:
  - the action ran;
  - the pointer left before the delay ended or the push was enough;
  - a mouse button was held, or the required key wasn't;
  - an app had confined or hidden the pointer, an app was full screen, or a listed app was in front;
  - a taskbar hides itself on that edge;
  - Windows didn't run the action;
  - Run a program didn't start the program: the program is missing or isn't an `.exe`, or Windows couldn't start it.
- The same result again right after itself, such as the pointer passing through a corner several times, shows once, with how many times and the latest time, so it doesn't push the other results out.
- **An edge the pointer rests on without pushing,** or that a mouse button is held on, as when dragging a window there, gives no result: both are ordinary use, and would push the other results out. An edge gives "left too soon" only when the push had got halfway, or pen, touch or a remote session had stayed half the delay.
- Results name no window, app or program, only the corner or edge, the action and the outcome. They aren't written to disk, and they're gone when the feature is turned off or the host exits.
- **On the page:** a **Recent results** card lists them, newest first, such as "Right edge, Task View: skipped because an app was full screen", with the time in the user's format. It shows the results as they come, while the page is shown. Without results, it says that no corner or edge has been reached yet, or, while the feature is off, that results show while it's on. While the feature is on but the host gives no results for three reads in a row, about 3 seconds, as after the feature stopped because of an error, it says that recent results aren't available right now, and to restart PC Companion if corners and edges don't work either; one or two reads without an answer, as right after the feature is turned on, keep what's shown. While an app running as administrator is in front, the host gets no pointer events, so nothing new shows; the page's note about that stays.
- **How they get there:** while the feature is on, the host serves the results through a named pipe that only the user's own account and the system can open, named after the user session, `PCCompanion.HotCorners.Results.<session ID>`, and it rejects remote clients (`CreateNamedPipe` with an access list for the user and the system, and `PIPE_REJECT_REMOTE_CLIENTS`). .NET's `PipeOptions.CurrentUserOnly` isn't used: in a host running as administrator, it gives access to the Administrators group instead of the user, so the settings app couldn't connect, and it doesn't reject remote clients. A program that connects gets the results as JSON and the pipe closes; the host reads nothing from it. The pipe waits for connections on the thread pool.
- **Threads:** a result is added on the host's UI thread to a small buffer behind a lock, which the pipe's thread copies, so the UI thread never waits for a client. Run a program adds its result from the thread pool. While the page is shown, the settings app connects about once a second off its UI thread, waiting at most 500 ms for the host.

## Multiple displays

- Every display's corners and edges can trigger, except a corner that touches another display, and the parts of an edge next to another display: the pointer passes into the next display there instead of stopping.
- Positions are in physical pixels, so different scaling on each display doesn't matter. Effective pixels, such as the 40 at each end of an edge, are scaled by the display's own scale.
- A full-screen app stops only the corners and edges of its own display.

## Settings

- **Feature page:** the on/off switch; a picture of a screen with, at each corner and edge, a list for its push action with an optional key, and a list for its wheel action with an optional key, and for **Run a program**, the program with **Browse** and its arguments; **Corners trigger**, **After the delay** (the default) or **When pushed**; the activation delay; **Push strength**, **Light**, **Medium** or **Firm**, for edges and for corners when pushed; **Don't trigger when an app is full screen** (on by default); **Don't trigger while these apps are in front**, a list of apps with **Add** and a **Remove** button for each; notes about the right and top edges' wheel, shown while one has a wheel action; and a **Permissions & data** card.
- The apps list shows each app's icon and name, and for a program its path in the tooltip; an empty list says so. **Add** offers:
  - **Running app**, a dialog that lists the apps with a window open, by icon and name, without PC Companion and the apps already listed;
  - **Program file**, the Open dialog for an `.exe`, for an app that isn't running.
- An app's name is its display name in the Windows Apps folder for an app ID, otherwise the program's description, otherwise its file name. The name is stored with the app, so the list shows it while the app isn't installed. The settings app reads names, icons and whether a program exists off its UI thread. An app that's already listed isn't added again.
- The picture shows physical corners and edges, so it isn't mirrored for right-to-left languages.
- Without room for the picture, as in a narrow window, the corners and edges are listed one below another, without the screen, in the language's direction.
- When the feature is first turned on, the top-left corner shows Task View and the others do nothing, close to GNOME's Activities corner. No edge and no wheel has an action until the user chooses one.
- The switch on Home changes only `enabled` and keeps the other settings.
- A corner or edge keeps its `program` and `arguments` when its action changes, so choosing Run a program again brings them back.
- A program path that doesn't fit its box shows its end after an ellipsis, such as `…\System32\charmap.exe`; the tooltip, and the box while editing, show the whole path.
- **The name:** the feature was Hot Corners. Its page, navigation item, Home card, strings, docs, README and website take the new name with the edges; its settings section, pipe names and icon stay.
- A section that can't be read, for example after a hand edit, turns the feature off with a warning in the log. A delay outside 0 to 1,000 ms is brought into range, and a corner or edge with an unknown action, wheel action or key does nothing; an unknown trigger or push strength gets the default. A blank app ID or program counts as none, and a listed app with neither an app ID nor a program, or with both, is left out, as is a `null` in the list. Settings from before the edges read as they did: corners keep their actions, and edges and wheels have none.

```json
"features": {
  "hotCorners": {
    "enabled": true,
    "topLeft": { "action": "TaskView", "key": "None" },
    "topRight": { "action": "KeepDisplaysOn", "key": "None", "wheel": "Volume", "wheelKey": "None" },
    "bottomLeft": { "action": "RunProgram", "key": "None", "program": "C:\\Windows\\System32\\notepad.exe", "arguments": "" },
    "bottomRight": { "action": "None", "key": "Control" },
    "top": { "action": "None", "key": "None" },
    "bottom": { "action": "None", "key": "None" },
    "left": { "action": "PreviousDesktop", "key": "None", "wheel": "Desktops", "wheelKey": "Control" },
    "right": { "action": "NextDesktop", "key": "None", "wheel": "Volume", "wheelKey": "None" },
    "activationDelayMilliseconds": 150,
    "cornersTrigger": "Delay",
    "pushStrength": "Medium",
    "ignoreFullScreenApps": true,
    "excludedApps": [
      { "name": "Remote Desktop Connection", "program": "C:\\Windows\\System32\\mstsc.exe" },
      { "name": "Photos", "appId": "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" }
    ]
  }
}
```

- `wheel`: `None`, `Volume`, `Desktops` or `Tracks`; missing means `None`. `wheelKey`: as `key`. `cornersTrigger`: `Delay` or `Push`. `pushStrength`: `Light`, `Medium` or `Firm`.

## Detecting the pointer

- The host watches pointer movement with **raw input** and reads the pointer position on each event. Windows allows one raw input registration for the mouse per process, so the feature shares it with any other feature that watches the pointer (`RawPointerMonitor`). Raw input is Microsoft's recommendation for monitoring input. A low-level mouse hook is silently removed by Windows when one call is too slow, and polling the position wakes the CPU many times a second even when nothing moves.
- **`RawPointerMonitor` grows** to report each event's movement and whether it's a position (`MOUSE_MOVE_ABSOLUTE`), for [Pushing](#pushing). Its other users ignore them.
- A spike confirmed that raw input gets the same pointer events as a low-level hook (791 and 787 for the same mouse movement), costs nothing while the pointer is still, and that `SHQueryUserNotificationState` reports full-screen apps.
- **The wheel** is kept from the apps by the shared low-level hook (`PointerHook`), only while the pointer is on a corner or edge with a wheel action; see [The wheel](#the-wheel).
- While an app running as administrator is in front, Windows doesn't deliver pointer events to PC Companion, by raw input or by a hook, and silently drops the shortcuts and keys it sends. Corners and edges don't trigger then, and the wheel reaches the app; the feature page says so. Running PC Companion as administrator avoids it.

## Threads

- **The host's UI thread** runs the feature, as before: `Apply`, raw input events, adding up pushes, the delays, the checks when a corner or edge would trigger or the pointer reaches one with a wheel action, claiming and releasing the wheel, and sending the actions' shortcuts and keys. Each is a few comparisons, or reads that wait for no program.
- **The pointer hook's thread** keeps claimed wheel notches and passes them to the UI thread, which runs the wheel action; see [Threads and processes](../../architecture/features.md#threads-and-processes).
- **The thread pool** runs Run a program and the question about taskbars that hide themselves, and passes their results back with `MessageWindow.BeginInvoke`; the results pipe waits for connections there.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The Notifications shortcut differs between Windows 10 and 11.
- Power requests, which Keep displays on uses, are in every supported version.
- The volume and track keys, Ctrl + Windows + Left and Right, and `ABM_GETAUTOHIDEBAREX` are in every supported version.
- The full-screen and captured-pointer checks use documented APIs that every supported version has: `GetClipCursor`, `GetCursorInfo`, `EnumWindows`, `GetWindowRect`, `GetWindowLongPtr`, `DwmGetWindowAttribute` with `DWMWA_CLOAKED`, `GetShellWindow`, `GetWindowThreadProcessId` and `MonitorFromWindow`.
- Apps that turn corners and edges off use the same documented APIs as App Windows, in every supported version: `SHGetPropertyStoreForWindow` for a window's app ID, `GetApplicationUserModelId` and `QueryFullProcessImageName`, and the Windows Apps folder (`FOLDERID_AppsFolder`) for names and icons.
- No undocumented API is used.

## Security and privacy

- **Reads:** the pointer position, the mouse's movement and whether a mouse button or the chosen key is pressed, only while the feature is on and only to compare with the corners and edges; the movement adds up only while the pointer is on one. When a corner or edge would trigger, or the pointer reaches one with a wheel action, it also reads whether the pointer is confined or hidden, and for the windows on its display, front to back until one decides, their position, style flags and whether they're cloaked or minimized, and whether each belongs to the Windows shell's process, by comparing process IDs. As the pointer reaches an edge with a push action, it asks Windows which bar hides itself on that edge. It reads no window titles, class names, process names or contents. While apps are listed, it also reads, at those moments, the app ID, package and program path of the window in front, only to compare them with the list. Nothing is stored or sent, and only which corner or edge ran which action or why it was skipped, and why Keep displays on ended, is logged, as Debug entries; never the pointer's position or movement. For Run a program, the log names the program's file name, never its folder or arguments, which can hold personal paths or secrets.
- **Keyboard:** reads only the state of the chosen keys, when a corner or edge would trigger and as the wheel turns; no keyboard hook.
- **Changes:** sends only the fixed Windows shortcuts and keys in the tables, and starts only the program the user chose for a corner or edge; nothing the user types or configures is sent as input. The volume keys change the volume as the keyboard's would. Keep displays on changes no setting; it holds a power request only while the pointer stays on its corner or edge.
- **The wheel:** the pointer hook keeps wheel notches from the app under the pointer only on the outermost pixels of a corner or edge with a wheel action, only while the pointer is there, and passes nothing else to PC Companion. The hook's thread only compares each event with the claims.
- **Screen saver and locking:** like a video player, Keep displays on delays the screen saver, and a lock that comes with it, while the pointer rests on its corner or edge. Someone at the PC must put the pointer there, any app running as the user can make the same request, `powercfg /requests` shows it with its reason, and locking the PC ends it.
- **Programs:** the program and its arguments are stored in `settings.json` and used only to start that program. Any program running as the user can change that file, but it could also start the same program itself, so this crosses no boundary as long as the program starts with the user's rights. So it does, also while PC Companion runs as administrator, which then hands the program and its arguments to a copy of itself without admin rights. Programs start directly from a full path to an `.exe`, without a shell or a search of folders.
- **Apps that turn corners and edges off:** the list, each app's name and its app ID or program path, is stored in `settings.json`, and never logged or sent. A program path can include the user's folder name. The list can only turn corners and edges off, so a changed settings file can't make them do more. The settings app's **Running app** dialog reads the apps of the open windows, their app IDs, packages, program paths, names and icons, only while it's open, and keeps only the app chosen.
- **Recent results:** kept in the host's memory and given only to programs of the user's own account on this PC, through a pipe the host only writes to. Any program running as the user can read them, but they hold only corners and edges, actions, outcomes and times, which say when the user moved the pointer there and nothing about what was on screen. They're never written to disk or sent elsewhere. While the host runs as administrator, the pipe still takes no input, so it gives programs without admin rights no way to reach it.
- **Network:** none.
- **Rights:** runs without admin rights, and never starts a program with them. Turning the feature off stops reading the pointer and removes the wheel claims.

## Performance

- No timer runs while the pointer is away from the corners and edges; each pointer event costs a few comparisons. While the pointer is on one, each event adds its movement to the push; a timer runs only for a corner's delay, and for an edge reached by pen, touch or a remote session.
- **Once the pointer stops,** its position is read once more, 50 ms after the last move, as Title Bar Actions does: raw input can report a move before the pointer's position follows it, so the move onto an edge can be read too early, and no other event would come before the wheel turns.
- The full-screen and captured-pointer checks run once per trigger, and once as the pointer reaches a corner or edge with a wheel action. They read windows only until one decides and send no messages to other apps' windows, so an app that stopped responding can't hold them. The app check reads only the window in front, at the same moments and only while apps are listed.
- **The pointer hook** is set only while the pointer is on a corner or edge with a wheel action, unless another feature claims. While it's set, every pointer event in every program passes through its thread, which only compares it with the claims.
- **The taskbar question** runs on the thread pool, once each time the pointer reaches an edge with a push action.
- Recent results cost the host a few comparisons and a copy per result, and nothing while no program asks for them. The settings app asks only while the page is shown.
- Keep displays on adds no timer or polling: the power request is set and cleared on pointer and session events.

## Accessibility and localization

- Every list, switch and slider has a label for screen readers, and the page works with the keyboard alone.
- Corner and edge names are physical ("Top left", "Right edge") in every language.
- The lists, the program and arguments boxes and **Browse** carry their corner's or edge's name for screen readers, such as "Top left program" and "Right edge wheel".
- The reason of the Keep displays on power request is translated like the page's strings.
- Each listed app's **Remove** button carries the app's name for screen readers, such as "Remove Photos", and the **Running app** dialog's list works with the keyboard.
- Each recent result is one line of text that screen readers read as it's shown, with its time; new results aren't announced, so moving the pointer doesn't make the page talk.
- **Without a mouse:** Windows' own shortcuts do what the actions do, such as Windows + Tab and the volume keys.

## Testing

- **Unit tests** with fake displays, a fake clock and fake pointer events: the activation delay, leaving and coming back, held buttons, required keys, full-screen apps, and display layouts (side by side, stacked, different sizes and scaling, displays left of or above the main one).
- **Unit tests for edges and pushing:** an edge on each side, and none where another display continues it; the 40 effective pixels near each corner; movement outward adding up within a second, and older movement dropping out; each push strength; moving along the edge restarting the push; a pointer resting on an edge never triggering it, and giving no result; corners when pushed; positions from pen, touch or a remote session falling back to the delays; the wait of 350 ms, or the delay and 50 ms, after a trigger; an edge where a taskbar hides itself skipping the push action, with its last answer used until the new one comes, and keeping its wheel.
- **Unit tests for the wheel,** with a fake pointer hook: claims made only while the pointer is on a corner or edge with a wheel action and the checks pass, and removed when it leaves; the claimed area, the corner's pixel or the edge's line; keys matched exactly; smaller steps adding up to notches; desktops and tracks at most one step every 300 ms; the keys each action sends, with the unassigned key after the Windows key; claims that win over other claims.
- **Unit tests for full screen and a captured pointer**, with fake windows from front to back: a confined or hidden pointer skips the corner with the full-screen setting on or off, a pointer hidden for touch doesn't; a borderless window covering the corner's display skips it, one on another display doesn't; overlays, always-on-top windows that don't cover the display, cloaked and minimized windows are passed over; a maximized window with its frame, the desktop and Task View aren't full screen; exclusive full screen counts only on the display of the window in front, and presentation mode on every display.
- **Unit tests for apps that turn corners and edges off**, with a fake app in front: an app ID set by the window or by the package matches, and a program matches by path whatever its case; a listed app in front skips corners and edges on every display, and keeps the wheel from being claimed, with the full-screen setting on or off; nothing about the app in front is read while the list is empty; the settings keep the list, and leave out an app with neither or both of an app ID and a program; the page adds an app once, from a program file or a running app, and removes it.
- **Unit tests for recent results:** the pointer leaving before the delay ends or the push is enough is a result; the last 5 are kept, newest first, and the same result in a row is counted with its latest time; Run a program's results; the JSON the pipe gives; the text of each outcome; what the page shows after reads with and without an answer; and, with a pipe the test creates, that a client gets the results and that the server reads nothing from it.
- **Unit tests for Keep displays on**, with a fake power request: it starts after the delay or push; it ends when the pointer leaves, the action changes, the feature stops or the PC is locked; it lasts through other settings and display changes while the pointer stays; and after a lock it starts again only once the pointer leaves and comes back.
- **Unit tests for Run a program**, with fake host commands: splitting the arguments, which paths are accepted, the program and arguments in the settings, that the host's command starts the program, and that the program is checked and started in the background, each corner's or edge's on its own.
- **Unit tests for the settings:** the example; settings from before the edges; unknown actions, wheel actions, keys, triggers and strengths.
- **Sandbox checks** (`scripts/sandbox/groups/HotCorners.ps1`): the Home switch turns the feature on, the page shows its settings in English and Arabic with Task View in the top-left corner, and with the pointer moved by `SendInput`, a corner shows the desktop and the host logs it; a held mouse button or a missing required key skips the corner; holding the required key adds no virtual desktop; turned off, the feature stops. For Run a program, the page shows the program and its arguments, a program that doesn't exist shows its problem below the box and one that exists shows none, also right after the missing one, and **Browse** opens the Open dialog; everything in Windows Sandbox runs as administrator with UAC off, so there the checks confirm that the program starts, as the host's own. For Keep displays on, `powercfg /requests` lists the host's display and system requests with their reason while the pointer is in the corner, and neither after it leaves. For full screen and a captured pointer: a borderless window covering the display skips the corner and the host logs why, and the corner triggers again once the window closes; a pointer confined with `ClipCursor` skips the corner; a window in the corner that hides the pointer skips it. For apps that turn corners off: with Character Map listed and in front, the corner is skipped and the host logs `AppExcluded`; the page shows the listed apps, **Running app** lists Character Map and adds it, **Program file** adds a program through the Open dialog, and **Remove** removes it, each saved in the settings. For recent results: with the page open, a corner skipped by a full-screen window, and the pointer leaving a corner before the delay ends, show on the page as they happen, and once the host has ended, the page says that recent results aren't available.
- **Sandbox checks for edges and the wheel:** relative moves by `SendInput` pushing against an edge show the desktop, the same moves along the edge don't, and a position sent by `SendInput`, as from pen or a remote session, triggers the edge after 500 ms; corners set to When pushed trigger on a push and not on a rest; a corner that just triggered doesn't trigger again within the wait; the wheel on the right edge's outermost pixels over a maximized test window changes the volume and doesn't scroll the window, and one pixel inside it scrolls and the volume stays; the wheel on an edge with Desktops switches desktops, at most one step for two quick notches; with the taskbar set to hide itself, a push on the bottom edge gives the taskbar's result and the bottom edge's wheel still works; the page shows the edges' lists and the notes about the right and top edges.
- **By hand:** a real mouse on one and several displays, a full-screen game or video, an exclusive and a borderless full-screen game, a virtual machine and a browser game that capture the pointer, a full-screen video on a second display while working on the first, an overlay of a graphics driver or game launcher, listed apps such as Remote Desktop Connection, a Store app and one browser profile, recent results on the page while PC Companion runs as administrator with UAC on, an app running as administrator in the foreground, and Run a program on a PC with UAC on: arguments, a path with spaces, a missing program, a program that needs admin rights, and, with Always run as administrator, that the program runs without admin rights. For Keep displays on, on Windows 10 and 11 with the screen saver and display timeout at 1 minute: neither starts while the pointer rests in the corner, both do after it leaves, the displays turn off on the lock screen after locking, and Sleep in Start works.
- **By hand, for edges and the wheel:** each push strength with several mice, including a high-resolution one, and a touchpad; everyday use near edges with actions, without pushing: a maximized browser's scroll bar, its tabs and the taskbar; displays of different sizes and scales, with and without Ease cursor movement between displays; pen and a Remote Desktop session; the wheel with a smooth-scrolling and a free-spinning wheel; the track keys with a media player; the wheel on the top edge over a maximized window with Title Bar Actions' wheel action; Windows 10.

## Spike results

A spike in Windows Sandbox, on Windows 11 24H2 (build 26100) at 100% scale on one display of 1520 × 823 pixels, with a test window of its own:

- **Pushing:** 20 relative moves of 5 counts against each edge, sent with `SendInput`, all reached raw input with their movement, adding up to 100, while the pointer stayed on the edge. Moves to a position reached raw input as positions (`MOUSE_MOVE_ABSOLUTE`), with no movement to add up.
- **The wheel at an edge:** a low-level hook keeping wheel notches only on the right edge's outermost pixels kept 3 notches over a maximized window, which didn't scroll; one pixel inside, the window scrolled 9 lines and nothing was kept. Smaller steps of 30 were kept one by one.
- **Under the edges:** on the maximized test window, the right and left edges' outermost pixels were its text box's border, with the scroll bar next to it; the top edge's was its title bar (`HTCAPTION`), and the bottom edge's the taskbar.
- **Volume:** the Volume Down and Volume Up keys, sent with `SendInput`, changed the volume by 2% each and showed Windows' volume display; the audio API's `VolumeStepDown` changed it by 2% without the display. The Sandbox's volume is its own: the host's didn't change.
- **Desktops:** Ctrl + Windows + Left and Right switched desktops; two switches 50, 150, 300 and 600 ms apart each moved two desktops; Left on the first desktop stayed there; Start didn't open.
- **Taskbars that hide themselves:** with the taskbar set to hide itself (`ABM_SETSTATE`), `ABM_GETAUTOHIDEBAREX` named it for the bottom edge, and nothing for any edge otherwise; the hidden taskbar kept 2 pixels on the screen, and the pointer on the bottom edge showed it.
- **Not checked:** how far real mice and touchpads move when pushed, several displays, real apps' scroll bars at the edge, and the track keys.

## Not included

- **Command lines** in cmd or PowerShell syntax, such as pipes, custom shortcuts, and actions for each app. The only setting for each app is the list of apps that turn corners and edges off.
- **Switching desktops while dragging a window to an edge,** as KDE Plasma offers: Windows snaps a window dragged to an edge, and corners and edges don't trigger while a button is held.
- **Brightness** with the wheel: built-in screens and external monitors need different ways, and monitors answer slowly. A later improvement.
- **Part of an edge** only, such as its middle, or a wider edge.
- **Different corners and edges for each display:** [#121](https://github.com/Lazyb0y/pc-companion/issues/121).
- **A mark that shows a corner or edge is about to trigger:** [#150](https://github.com/Lazyb0y/pc-companion/issues/150).
- **A delay for each corner:** [#153](https://github.com/Lazyb0y/pc-companion/issues/153).
- **Other features' actions** for corners and edges, beyond the desktops: [#152](https://github.com/Lazyb0y/pc-companion/issues/152).
- **Clicks and the horizontal wheel** at corners and edges: a click hits the window below, such as its Close button.
- **Touch edge swipes,** which Windows owns.
