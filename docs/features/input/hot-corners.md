# Hot Corners

Move the pointer into a corner of the screen to run an action, such as showing all windows or locking the PC.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.hotCorners`.
- Inspired by macOS Hot Corners, the Activities hot corner in GNOME, and screen edges in KDE Plasma.
- Off until the user turns it on.

## Behavior

- Each of the four corners can run one action, or nothing.
- A corner triggers when the pointer reaches it and stays for the **activation delay**: 150 ms by default, adjustable from 0 to 1,000 ms.
- After it triggers, a corner doesn't trigger again until the pointer has left it.
- A corner can require a key: Ctrl, Alt, Shift or the Windows key must be held when the activation delay ends, whether it was pressed before or after the pointer reached the corner. This avoids accidental triggers, as in macOS.
- A corner doesn't trigger:
  - when a mouse button is held as the pointer reaches it or when the delay ends, because dragging a window into a corner snaps it there;
  - while an app has captured the pointer: confined it to part of the screen, as games and virtual machines do, or hidden it, as browser games do;
  - while an app is full screen on the corner's display, such as a game, a video or a presentation, unless the user turns this off. A full-screen app on another display doesn't stop it;
  - while an app the user listed is in front, on any display;
  - on the lock screen or a UAC prompt, which Windows shows on a separate desktop.
- See [Full screen and a captured pointer](#full-screen-and-a-captured-pointer) and [Apps that turn corners off](#apps-that-turn-corners-off) for how these are checked.
- A corner the pointer is already in when Hot Corners starts, or when its settings or the displays change, triggers only after the pointer leaves it and comes back; saving other settings, such as another feature's, doesn't.
- The feature page shows the last few results, such as which corner ran which action, or why a corner didn't trigger; see [Recent results](#recent-results).

## Actions

| Action | What happens | How |
|---|---|---|
| Task View | Shows all open windows and virtual desktops | Windows + Tab |
| Show desktop | Shows the desktop; again brings the windows back | Windows + D |
| Start | Opens Start | Windows key |
| Search | Opens Windows search | Windows + S |
| Notifications | Opens the notification center (Windows 11) or the Action center (Windows 10) | Windows + N, or Windows + A on Windows 10 |
| Lock | Locks the PC | `LockWorkStation` |
| Turn off displays | Turns the displays off until the pointer moves | `SC_MONITORPOWER` |
| Screen saver | Starts the screen saver | `SC_SCREENSAVE` |
| Keep displays on | While the pointer stays in the corner, the screen saver doesn't start and the displays don't turn off | A power request; see [Keep displays on](#keep-displays-on) |
| Run a program | Starts the program chosen for the corner | Started directly, without a shell; see [Run a program](#run-a-program) |

- Shortcuts are sent as keyboard input only where Windows has no API for the action.
- A modifier the user is holding, such as the key a corner requires, is released before the shortcut is sent: Ctrl + Windows + D would add a virtual desktop. An unassigned key is pressed first, so releasing the Windows key or Alt doesn't open Start or the menu bar.
- Screen saver does nothing when no screen saver is chosen in Windows settings.
- Not included: command lines in cmd or PowerShell syntax, such as pipes, custom shortcuts, per-app actions, and screen edges. The only setting for each app is the list of apps that turn corners off.

## Keep displays on

- Inspired by Disable Screen Saver in macOS Hot Corners. Unlike the other actions, which run once, it lasts while the pointer stays in the corner.
- It triggers like the other actions, with the same activation delay, key and exceptions.
- While it lasts, the screen saver doesn't start, the displays don't turn off and the PC doesn't go to sleep on its own.
- It ends when the pointer leaves the corner, the corner gets another action, Hot Corners is turned off or stops, or the PC is locked or switched to another user. Locking ends it so the displays turn off on the lock screen as usual; after unlocking, the pointer must leave the corner and come back.
- Other changes to the settings or the displays don't end it while the pointer is still in a corner with this action.
- It doesn't stop the user: Sleep in Start, the power button and closing the lid work as usual, and Windows ends the request then.
- **How:** a power request (`PowerCreateRequest`, then `PowerSetRequest` for the display and the system), released with `PowerClearRequest`. Its reason, "Hot Corners: the pointer is in a corner that keeps the displays on.", is shown by `powercfg /requests` in the user's language. Session locks come from `WTSRegisterSessionNotification`.
- It changes no screen saver or power setting, and Windows ends the request when the host exits, so nothing is left behind if PC Companion stops unexpectedly.
- Policies that limit what apps can keep awake, such as `powercfg /requestsoverride` or Group Policy, apply to it as to any app.

## Run a program

- Close to the custom command in KDE Plasma, Cinnamon and XFCE. Each corner with this action has its own program and arguments.
- **Program:** the full path of an `.exe` file, typed or chosen with **Browse**. A name without a folder, a relative path or another type of file doesn't run: Windows would otherwise search folders for a program of that name, and it runs `.bat` and `.cmd` files through cmd, which reads arguments differently. The path is used as written; environment variables aren't expanded.
- **Arguments:** optional, in one box. They're split by the Windows command-line rules and passed to the program one by one (`ProcessStartInfo.ArgumentList`), never through a shell, so pipes and redirection don't apply. To run a script, choose `powershell.exe` or `cmd.exe` as the program.
- The program starts in its own folder, with the user's rights, and runs on its own: PC Companion doesn't wait for it, watch it or stop it.
- It's checked and started on the thread pool, not the host's UI thread: Windows can take many seconds to start a program, for example from a network share that's offline, and the other corners and features don't wait meanwhile. A failure there is logged and leaves Hot Corners running.
- A program that needs admin rights doesn't start, because Windows refuses it without asking; a warning is logged.
- **While PC Companion runs as administrator, Run a program does nothing** and a warning is logged; the feature page says so. Any program running as the user can change the settings file, so that file must never make PC Companion run code as administrator.
- A corner without a program, or whose program is missing or isn't an `.exe`, does nothing and logs a warning when it triggers. The feature page shows the problem below the box.

## Full screen and a captured pointer

- Inspired by GNOME, which checks full screen for each display. KDE Plasma checks only the active window, and Windows' own full-screen state (`SHQueryUserNotificationState`) sees exclusive full screen but not a borderless game, and doesn't say which display a full-screen app is on.
- Both checks run only when a corner's delay ends, never on each pointer move.
- **Captured pointer:** the corner is skipped when the pointer is confined to less than the area of all displays (`GetClipCursor`), or hidden (`GetCursorInfo`). A pointer that Windows hides during touch or pen input doesn't count. This applies whether or not **Don't trigger when an app is full screen** is on: a corner that an app pins the pointer to isn't one the user aimed for.
- **Full screen, for the corner's display:** PC Companion looks at the visible top-level windows from front to back (`EnumWindows`) and skips:
  - windows that are cloaked, as on another virtual desktop, or minimized, or that don't overlap the display;
  - overlays: click-through windows (`WS_EX_TRANSPARENT`), windows that can't be activated (`WS_EX_NOACTIVATE`) and tool windows, such as the overlays of graphics drivers, game launchers and chat apps;
  - always-on-top windows that don't cover the whole display, such as the taskbar or a picture-in-picture video.
- The first window left decides. The display is full screen when that window covers the whole display, has no title bar or resizable frame (`WS_CAPTION`, `WS_THICKFRAME`), and doesn't belong to the Windows shell's process, which shows the desktop and Task View. So a maximized window, which keeps its frame, isn't full screen, even with a taskbar that hides itself.
- Windows' own state adds two cases: exclusive full screen (`QUNS_RUNNING_D3D_FULL_SCREEN`) counts on the display of the window in front, which such an app always is, and presentation mode (`QUNS_PRESENTATION_MODE`) counts on every display. Its busy state (`QUNS_BUSY`) isn't used, since it doesn't say which display: a video on one display stopped the corners of every display.
- A skipped corner logs why at Debug level: the pointer was confined or hidden, or an app was full screen.

## Apps that turn corners off

- Like the app exceptions of hot corner tools for macOS, and a frequent request for hot corner tools on Windows. For apps that need their corners, or shouldn't get a corner's shortcut: remote desktop and virtual machine windows, where the shortcut can reach the other PC, drawing and design apps, and games that the full-screen checks miss.
- While a listed app is in front, no corner triggers, on any display: a corner's shortcut goes to the app in front wherever the corner is. This applies whatever the full-screen setting. The list can only turn corners off, never on.
- **What counts as one app**, as the taskbar groups them and as in App Windows: the app ID that the window in front sets for itself, as browsers do for each profile; otherwise the app ID of its Store app package; otherwise its program file. An app ID matches whichever way it was read, and a program matches by its full path, ignoring case. An app that moves its program file when it updates, as some apps installed in a folder named after their version do, must be added again.
- **When:** once per trigger, when the delay ends, after the other checks, and only while the list has apps. It reads the window in front's app ID, a property of the window, and its process's package and program path (`OpenProcess` with `PROCESS_QUERY_LIMITED_INFORMATION`). None of these waits for the app, so a trigger never waits for another program.
- While an app running as administrator is in front, corners don't trigger anyway unless PC Companion runs as administrator; then the list works for that app too.
- A skipped corner logs `AppExcluded` at Debug level, never which app.

## Recent results

- No other desktop or tool says why a corner didn't trigger: macOS, KDE Plasma, GNOME and hot corner tools for Windows log nothing the user can see, and "hot corners stopped working" is a common complaint on each.
- The host keeps the last 5 results in memory, each with its time, corner, action and outcome:
  - the action ran;
  - the pointer left before the delay ended;
  - a mouse button was held, or the required key wasn't;
  - an app had confined or hidden the pointer, an app was full screen, or a listed app was in front;
  - Windows didn't run the action;
  - Run a program didn't start the program: PC Companion runs as administrator, the program is missing or isn't an `.exe`, or Windows couldn't start it.
- The same result again right after itself, such as the pointer passing through a corner several times, shows once, with how many times and the latest time, so it doesn't push the other results out.
- Results name no window, app or program, only the corner, the action and the outcome. They aren't written to disk, and they're gone when Hot Corners is turned off or the host exits.
- **On the page:** a **Recent results** card lists them, newest first, such as "Top left, Task View: skipped because an app was full screen", with the time in the user's format. It shows the results as they come, while the page is shown. Without results, it says that no corner has been reached yet, or, while Hot Corners is off, that results show while it's on. While Hot Corners is on but the host gives no results for three reads in a row, about 3 seconds, as after Hot Corners stopped because of an error, it says that recent results aren't available right now, and to restart PC Companion if corners don't work either; one or two reads without an answer, as right after Hot Corners is turned on, keep what's shown. While an app running as administrator is in front, the host gets no pointer events, so nothing new shows; the page's note about that stays.
- **How they get there:** while Hot Corners is on, the host serves the results through a named pipe that only the user's own account and the system can open, named after the user session, `PCCompanion.HotCorners.Results.<session ID>`, and it rejects remote clients (`CreateNamedPipe` with an access list for the user and the system, and `PIPE_REJECT_REMOTE_CLIENTS`). .NET's `PipeOptions.CurrentUserOnly` isn't used: in a host running as administrator, it gives access to the Administrators group instead of the user, so the settings app couldn't connect, and it doesn't reject remote clients. A program that connects gets the results as JSON and the pipe closes; the host reads nothing from it. The pipe waits for connections on the thread pool.
- **Threads:** a result is added on the host's UI thread to a small buffer behind a lock, which the pipe's thread copies, so the UI thread never waits for a client. Run a program adds its result from the thread pool. While the page is shown, the settings app connects about once a second off its UI thread, waiting at most 500 ms for the host.

## Multiple displays

- Every display's corners can trigger, except a corner that touches another display: the pointer passes into the next display there instead of stopping.
- Positions are in physical pixels, so different scaling on each display doesn't matter.
- A full-screen app stops only the corners of its own display.

## Settings

- **Feature page:** the on/off switch; a picture of a screen with a list for each corner's action and an optional key, and for **Run a program**, the program with **Browse** and its arguments; the activation delay; **Don't trigger when an app is full screen** (on by default); **Don't trigger while these apps are in front**, a list of apps with **Add** and a **Remove** button for each; and a **Permissions & data** card.
- The apps list shows each app's icon and name, and for a program its path in the tooltip; an empty list says so. **Add** offers:
  - **Running app**, a dialog that lists the apps with a window open, by icon and name, without PC Companion and the apps already listed;
  - **Program file**, the Open dialog for an `.exe`, for an app that isn't running.
- An app's name is its display name in the Windows Apps folder for an app ID, otherwise the program's description, otherwise its file name. The name is stored with the app, so the list shows it while the app isn't installed. The settings app reads names, icons and whether a program exists off its UI thread. An app that's already listed isn't added again.
- The picture shows physical corners, so it isn't mirrored for right-to-left languages.
- Without room for the picture, as in a narrow window, the corners are listed one below another, without the screen, in the language's direction.
- When the feature is first turned on, the top-left corner shows Task View and the others do nothing, close to GNOME's Activities corner.
- The switch on Home changes only `enabled` and keeps the other settings.
- A corner keeps its `program` and `arguments` when its action changes, so choosing Run a program again brings them back.
- A program path that doesn't fit its box shows its end after an ellipsis, such as `…\System32\charmap.exe`; the tooltip, and the box while editing, show the whole path.
- A section that can't be read, for example after a hand edit, turns Hot Corners off with a warning in the log. A delay outside 0 to 1,000 ms is brought into range, and a corner with an unknown action or key does nothing. A blank app ID or program counts as none, and a listed app with neither an app ID nor a program, or with both, is left out, as is a `null` in the list.

```json
"features": {
  "hotCorners": {
    "enabled": true,
    "topLeft": { "action": "TaskView", "key": "None" },
    "topRight": { "action": "KeepDisplaysOn", "key": "None" },
    "bottomLeft": { "action": "RunProgram", "key": "None", "program": "C:\\Windows\\System32\\notepad.exe", "arguments": "" },
    "bottomRight": { "action": "None", "key": "Control" },
    "activationDelayMilliseconds": 150,
    "ignoreFullScreenApps": true,
    "excludedApps": [
      { "name": "Remote Desktop Connection", "program": "C:\\Windows\\System32\\mstsc.exe" },
      { "name": "Photos", "appId": "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" }
    ]
  }
}
```

## Detecting the pointer

- The host watches pointer movement with **raw input** and reads the pointer position on each event. Raw input is Microsoft's recommendation for monitoring input. A low-level mouse hook is silently removed by Windows when one call is too slow, and polling the position wakes the CPU many times a second even when nothing moves.
- A spike confirmed that raw input gets the same pointer events as a low-level hook (791 and 787 for the same mouse movement), costs nothing while the pointer is still, and that `SHQueryUserNotificationState` reports full-screen apps.
- While an app running as administrator is in front, Windows doesn't deliver pointer events to PC Companion, by raw input or by a hook, and silently drops the shortcuts it sends. Corners don't trigger then; the feature page says so. Running PC Companion as administrator avoids it.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The Notifications shortcut differs between Windows 10 and 11.
- Power requests, which Keep displays on uses, are in every supported version.
- The full-screen and captured-pointer checks use documented APIs that every supported version has: `GetClipCursor`, `GetCursorInfo`, `EnumWindows`, `GetWindowRect`, `GetWindowLongPtr`, `DwmGetWindowAttribute` with `DWMWA_CLOAKED`, `GetShellWindow`, `GetWindowThreadProcessId` and `MonitorFromWindow`.
- Apps that turn corners off use the same documented APIs as App Windows, in every supported version: `SHGetPropertyStoreForWindow` for a window's app ID, `GetApplicationUserModelId` and `QueryFullProcessImageName`, and the Windows Apps folder (`FOLDERID_AppsFolder`) for names and icons.

## Security and privacy

- **Reads:** the pointer position and whether a mouse button or the chosen key is pressed, only while the feature is on and only to compare with the corners. When a corner's delay ends, it also reads whether the pointer is confined or hidden, and for the windows on the corner's display, front to back until one decides, their position, style flags and whether they're cloaked or minimized, and whether each belongs to the Windows shell's process, by comparing process IDs. It reads no window titles, class names, process names or contents. While apps are listed, it also reads, when a corner's delay ends, the app ID, package and program path of the window in front, only to compare them with the list. Nothing is stored or sent, and only which corner ran which action or why it was skipped, and why Keep displays on ended, is logged, as Debug entries. For Run a program, the log names the program's file name, never its folder or arguments, which can hold personal paths or secrets.
- **Keyboard:** reads only the state of the chosen key, when a corner's activation delay ends; no keyboard hook.
- **Changes:** sends only the fixed Windows shortcuts in the table, and starts only the program the user chose for a corner; nothing the user types or configures is sent as input. Keep displays on changes no setting; it holds a power request only while the pointer stays in its corner.
- **Screen saver and locking:** like a video player, Keep displays on delays the screen saver, and a lock that comes with it, while the pointer rests in the corner. Someone at the PC must put the pointer there, any app running as the user can make the same request, `powercfg /requests` shows it with its reason, and locking the PC ends it.
- **Programs:** the program and its arguments are stored in `settings.json` and used only to start that program. Any program running as the user can change that file, but it could also start the same program itself, so this crosses no boundary while PC Companion runs as the user. Running as administrator it would, so the action doesn't run then. Programs start directly from a full path to an `.exe`, without a shell or a search of folders.
- **Apps that turn corners off:** the list, each app's name and its app ID or program path, is stored in `settings.json`, and never logged or sent. A program path can include the user's folder name. The list can only turn corners off, so a changed settings file can't make a corner do more. The settings app's **Running app** dialog reads the apps of the open windows, their app IDs, packages, program paths, names and icons, only while it's open, and keeps only the app chosen.
- **Recent results:** kept in the host's memory and given only to programs of the user's own account on this PC, through a pipe the host only writes to. Any program running as the user can read them, but they hold only corners, actions, outcomes and times, which say when the user moved the pointer into a corner and nothing about what was on screen. They're never written to disk or sent elsewhere. While the host runs as administrator, the pipe still takes no input, so it gives programs without admin rights no way to reach it.
- **Rights:** runs without admin rights, and never starts a program with them. Turning the feature off stops reading the pointer.

## Performance

- No timer runs while the pointer is away from the corners; each pointer event costs a few comparisons. A timer runs only while the pointer is in a corner, for the activation delay.
- The full-screen and captured-pointer checks run once per trigger, when the delay ends. They read windows only until one decides and send no messages to other apps' windows, so an app that stopped responding can't hold them. The app check reads only the window in front, once per trigger and only while apps are listed.
- Recent results cost the host a few comparisons and a copy per result, and nothing while no program asks for them. The settings app asks only while the page is shown.
- Keep displays on adds no timer or polling: the power request is set and cleared on pointer and session events.

## Accessibility and localization

- Every list, switch and slider has a label for screen readers, and the page works with the keyboard alone.
- Corner names are physical ("Top left") in every language.
- The program and arguments boxes and **Browse** carry their corner's name for screen readers, such as "Top left program".
- The reason of the Keep displays on power request is translated like the page's strings.
- Each listed app's **Remove** button carries the app's name for screen readers, such as "Remove Photos", and the **Running app** dialog's list works with the keyboard.
- Each recent result is one line of text that screen readers read as it's shown, with its time; new results aren't announced, so moving the pointer doesn't make the page talk.

## Testing

- **Unit tests** with fake displays, a fake clock and fake pointer events: the activation delay, leaving and coming back, held buttons, required keys, full-screen apps, and display layouts (side by side, stacked, different sizes and scaling, displays left of or above the main one).
- **Unit tests for full screen and a captured pointer**, with fake windows from front to back: a confined or hidden pointer skips the corner with the full-screen setting on or off, a pointer hidden for touch doesn't; a borderless window covering the corner's display skips it, one on another display doesn't; overlays, always-on-top windows that don't cover the display, cloaked and minimized windows are passed over; a maximized window with its frame, the desktop and Task View aren't full screen; exclusive full screen counts only on the display of the window in front, and presentation mode on every display.
- **Unit tests for apps that turn corners off**, with a fake app in front: an app ID set by the window or by the package matches, and a program matches by path whatever its case; a listed app in front skips corners on every display, with the full-screen setting on or off; nothing about the app in front is read while the list is empty; the settings keep the list, and leave out an app with neither or both of an app ID and a program; the page adds an app once, from a program file or a running app, and removes it.
- **Unit tests for recent results:** the pointer leaving before the delay ends is a result; the last 5 are kept, newest first, and the same result in a row is counted with its latest time; Run a program's results; the JSON the pipe gives; the text of each outcome; what the page shows after reads with and without an answer; and, with a pipe the test creates, that a client gets the results and that the server reads nothing from it.
- **Unit tests for Keep displays on**, with a fake power request: it starts after the delay; it ends when the pointer leaves, the corner's action changes, the feature stops or the PC is locked; it lasts through other settings and display changes while the pointer stays in the corner; and after a lock it starts again only once the pointer leaves and comes back.
- **Unit tests for Run a program**, with a fake process starter: splitting the arguments, which paths are accepted, the program and arguments in the settings, that nothing starts while PC Companion runs as administrator, and that the program is checked and started in the background, each corner's on its own.
- **Sandbox checks** (`scripts/sandbox/groups/HotCorners.ps1`): the Home switch turns the feature on, the page shows its settings in English and Arabic with Task View in the top-left corner, and with the pointer moved by `SendInput`, a corner shows the desktop and the host logs it; a held mouse button or a missing required key skips the corner; holding the required key adds no virtual desktop; turned off, the feature stops. For Run a program, the page shows the program and its arguments, a program that doesn't exist shows its problem below the box, and **Browse** opens the Open dialog; everything in Windows Sandbox runs as administrator, so there the checks confirm that the program doesn't start and that a warning is logged. For Keep displays on, `powercfg /requests` lists the host's display and system requests with their reason while the pointer is in the corner, and neither after it leaves. For full screen and a captured pointer: a borderless window covering the display skips the corner and the host logs why, and the corner triggers again once the window closes; a pointer confined with `ClipCursor` skips the corner; a window in the corner that hides the pointer skips it. For apps that turn corners off: with Character Map listed and in front, the corner is skipped and the host logs `AppExcluded`; the page shows the listed apps, **Running app** lists Character Map and adds it, **Program file** adds a program through the Open dialog, and **Remove** removes it, each saved in the settings. For recent results: with the page open, a corner skipped by a full-screen window, and the pointer leaving a corner before the delay ends, show on the page as they happen, and once the host has ended, the page says that recent results aren't available.
- **By hand:** a real mouse on one and several displays, a full-screen game or video, an exclusive and a borderless full-screen game, a virtual machine and a browser game that capture the pointer, a full-screen video on a second display while working on the first, an overlay of a graphics driver or game launcher, listed apps such as Remote Desktop Connection, a Store app and one browser profile, recent results on the page while PC Companion runs as administrator with UAC on, an app running as administrator in the foreground, and Run a program on a PC with UAC on: arguments, a path with spaces, a missing program, and a program that needs admin rights. For Keep displays on, on Windows 10 and 11 with the screen saver and display timeout at 1 minute: neither starts while the pointer rests in the corner, both do after it leaves, the displays turn off on the lock screen after locking, and Sleep in Start works.
