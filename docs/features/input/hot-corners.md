# Hot Corners

Move the pointer into a corner of the screen to run an action, such as showing all windows or locking the PC.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.hotCorners`.
- Inspired by macOS Hot Corners, the Activities hot corner in GNOME, and screen edges in KDE Plasma.
- Off until the user turns it on.

## Behavior

- Each of the four corners can run one action, or nothing.
- A corner triggers when the pointer reaches it and stays for the **activation delay**: 150 ms by default, adjustable from 0 to 1,000 ms.
- After it triggers, a corner doesn't trigger again until the pointer has left it.
- A corner can require a key: hold Ctrl, Alt, Shift or the Windows key while the pointer reaches it. This avoids accidental triggers, as in macOS.
- A corner doesn't trigger:
  - when a mouse button is held as the pointer reaches it or when the delay ends, because dragging a window into a corner snaps it there;
  - while an app is full screen, such as a game, a video or a presentation, unless the user turns this off;
  - on the lock screen or a UAC prompt, which Windows shows on a separate desktop.
- A corner the pointer is already in when Hot Corners starts, or when its settings or the displays change, triggers only after the pointer leaves it and comes back.

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
- Not included: command lines in cmd or PowerShell syntax, such as pipes, custom shortcuts, per-app settings, and screen edges.

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
- A program that needs admin rights doesn't start, because Windows refuses it without asking; a warning is logged.
- **While PC Companion runs as administrator, Run a program does nothing** and a warning is logged; the feature page says so. Any program running as the user can change the settings file, so that file must never make PC Companion run code as administrator.
- A corner without a program, or whose program is missing or isn't an `.exe`, does nothing and logs a warning when it triggers. The feature page shows the problem below the box.

## Multiple displays

- Every display's corners can trigger, except a corner that touches another display: the pointer passes into the next display there instead of stopping.
- Positions are in physical pixels, so different scaling on each display doesn't matter.

## Settings

- **Feature page:** the on/off switch; a picture of a screen with a list for each corner's action and an optional key, and for **Run a program**, the program with **Browse** and its arguments; the activation delay; **Don't trigger when an app is full screen** (on by default); and a **Permissions & data** card.
- The picture shows physical corners, so it isn't mirrored for right-to-left languages.
- Without room for the picture, as in a narrow window, the corners are listed one below another, without the screen, in the language's direction.
- When the feature is first turned on, the top-left corner shows Task View and the others do nothing, close to GNOME's Activities corner.
- The switch on Home changes only `enabled` and keeps the other settings.
- A corner keeps its `program` and `arguments` when its action changes, so choosing Run a program again brings them back.
- A program path that doesn't fit its box shows its end after an ellipsis, such as `…\System32\charmap.exe`; the tooltip, and the box while editing, show the whole path.
- A section that can't be read, for example after a hand edit, turns Hot Corners off with a warning in the log. A delay outside 0 to 1,000 ms is brought into range, and a corner with an unknown action or key does nothing.

```json
"features": {
  "hotCorners": {
    "enabled": true,
    "topLeft": { "action": "TaskView", "key": "None" },
    "topRight": { "action": "KeepDisplaysOn", "key": "None" },
    "bottomLeft": { "action": "RunProgram", "key": "None", "program": "C:\\Windows\\System32\\notepad.exe", "arguments": "" },
    "bottomRight": { "action": "None", "key": "Control" },
    "activationDelayMilliseconds": 150,
    "ignoreFullScreenApps": true
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

## Security and privacy

- **Reads:** the pointer position and whether a mouse button or the chosen key is pressed, only while the feature is on and only to compare with the corners. Nothing is stored or sent, and only which corner ran which action, and why Keep displays on ended, is logged, as Debug entries. For Run a program, the log names the program's file name, never its folder or arguments, which can hold personal paths or secrets.
- **Keyboard:** reads only the state of the chosen key, when a corner is reached; no keyboard hook.
- **Changes:** sends only the fixed Windows shortcuts in the table, and starts only the program the user chose for a corner; nothing the user types or configures is sent as input. Keep displays on changes no setting; it holds a power request only while the pointer stays in its corner.
- **Screen saver and locking:** like a video player, Keep displays on delays the screen saver, and a lock that comes with it, while the pointer rests in the corner. Someone at the PC must put the pointer there, any app running as the user can make the same request, `powercfg /requests` shows it with its reason, and locking the PC ends it.
- **Programs:** the program and its arguments are stored in `settings.json` and used only to start that program. Any program running as the user can change that file, but it could also start the same program itself, so this crosses no boundary while PC Companion runs as the user. Running as administrator it would, so the action doesn't run then. Programs start directly from a full path to an `.exe`, without a shell or a search of folders.
- **Rights:** runs without admin rights, and never starts a program with them. Turning the feature off stops reading the pointer.

## Performance

- No timer runs while the pointer is away from the corners; each pointer event costs a few comparisons. A timer runs only while the pointer is in a corner, for the activation delay.
- Keep displays on adds no timer or polling: the power request is set and cleared on pointer and session events.

## Accessibility and localization

- Every list, switch and slider has a label for screen readers, and the page works with the keyboard alone.
- Corner names are physical ("Top left") in every language.
- The program and arguments boxes and **Browse** carry their corner's name for screen readers, such as "Top left program".
- The reason of the Keep displays on power request is translated like the page's strings.

## Testing

- **Unit tests** with fake displays, a fake clock and fake pointer events: the activation delay, leaving and coming back, held buttons, required keys, full-screen apps, and display layouts (side by side, stacked, different sizes and scaling, displays left of or above the main one).
- **Unit tests for Keep displays on**, with a fake power request: it starts after the delay; it ends when the pointer leaves, the corner's action changes, the feature stops or the PC is locked; it lasts through other settings and display changes while the pointer stays in the corner; and after a lock it starts again only once the pointer leaves and comes back.
- **Unit tests for Run a program**, with a fake process starter: splitting the arguments, which paths are accepted, the program and arguments in the settings, and that nothing starts while PC Companion runs as administrator.
- **Sandbox checks** (`scripts/sandbox/groups/HotCorners.ps1`): the Home switch turns the feature on, the page shows its settings in English and Arabic with Task View in the top-left corner, and with the pointer moved by `SendInput`, a corner shows the desktop and the host logs it; a held mouse button or a missing required key skips the corner; holding the required key adds no virtual desktop; turned off, the feature stops. For Run a program, the page shows the program and its arguments and **Browse** opens the Open dialog; everything in Windows Sandbox runs as administrator, so there the checks confirm that the program doesn't start and that a warning is logged. For Keep displays on, `powercfg /requests` lists the host's display and system requests with their reason while the pointer is in the corner, and neither after it leaves.
- **By hand:** a real mouse on one and several displays, a full-screen game or video, an app running as administrator in the foreground, and Run a program on a PC with UAC on: arguments, a path with spaces, a missing program, and a program that needs admin rights. For Keep displays on, on Windows 10 and 11 with the screen saver and display timeout at 1 minute: neither starts while the pointer rests in the corner, both do after it leaves, the displays turn off on the lock screen after locking, and Sleep in Start works.
