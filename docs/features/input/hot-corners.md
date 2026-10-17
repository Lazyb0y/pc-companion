# Hot Corners

Move the pointer into a corner of the screen to run an action, such as showing all windows or locking the PC.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.hotCorners`.
- Inspired by macOS Hot Corners, the Activities hot corner in GNOME, and screen edges in KDE Plasma.
- Off until the user turns it on.

## Behavior

- Each of the four corners can run one action, or nothing.
- A corner triggers when the pointer reaches it and stays for the **activation delay**: 150 ms by default, as in KDE Plasma, adjustable from 0 to 1,000 ms.
- After it triggers, a corner doesn't trigger again until the pointer has left it.
- A corner can require a key: hold Ctrl, Alt, Shift or the Windows key while the pointer reaches it. This avoids accidental triggers, as in macOS.
- A corner doesn't trigger:
  - while a mouse button is held, because dragging a window into a corner snaps it there;
  - while an app is full screen, such as a game, a video or a presentation, unless the user turns this off;
  - on the lock screen or a UAC prompt, which Windows shows on a separate desktop.

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

- Shortcuts are sent as keyboard input only where Windows has no API for the action.
- Not included: running an app or a command, custom shortcuts, per-app settings, and screen edges.

## Multiple displays

- Every display's corners can trigger, except a corner that touches another display: the pointer passes into the next display there instead of stopping.
- Positions are in physical pixels, so different scaling on each display doesn't matter.

## Settings

- **Feature page:** the on/off switch; a picture of a screen with a list for each corner's action and an optional key; the activation delay; **Don't trigger when an app is full screen** (on by default); and a **Permissions & data** card.
- The picture shows physical corners, so it isn't mirrored for right-to-left languages.
- When the feature is first turned on, the top-left corner shows Task View and the others do nothing, close to GNOME's Activities corner.

```json
"features": {
  "hotCorners": {
    "enabled": true,
    "topLeft": { "action": "TaskView", "key": "None" },
    "topRight": { "action": "None", "key": "None" },
    "bottomLeft": { "action": "None", "key": "None" },
    "bottomRight": { "action": "None", "key": "Control" },
    "activationDelayMilliseconds": 150,
    "ignoreFullScreenApps": true
  }
}
```

## Detecting the pointer

- The host watches pointer movement with **raw input** and reads the pointer position on each event. Raw input is Microsoft's recommendation for monitoring input. A low-level mouse hook is silently removed by Windows when one call is too slow, and polling the position wakes the CPU many times a second even when nothing moves.
- With an app running as administrator in the foreground, Windows may block pointer events and the shortcuts; the feature page says so. Running PC Companion as administrator is the way around it.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The Notifications shortcut differs between Windows 10 and 11.

## Security and privacy

- **Reads:** the pointer position and whether a mouse button or the chosen key is pressed, only while the feature is on and only to compare with the corners. Nothing is stored or sent, and only which corner ran which action is logged, as a Debug entry.
- **Keyboard:** reads only the state of the chosen key, when a corner is reached; no keyboard hook.
- **Changes:** sends only the fixed Windows shortcuts in the table; nothing the user types or configures is sent as input.
- **Rights:** runs without admin rights. Turning the feature off stops reading the pointer.

## Performance

- No timer runs while the pointer is away from the corners; each pointer event costs a few comparisons. A timer runs only while the pointer is in a corner, for the activation delay.

## Accessibility and localization

- Every list, switch and slider has a label for screen readers, and the page works with the keyboard alone.
- Corner names are physical ("Top left") in every language.

## Testing

- **Unit tests** with fake displays, a fake clock and fake pointer events: the activation delay, leaving and coming back, held buttons, required keys, full-screen apps, and display layouts (side by side, stacked, different sizes and scaling, displays left of or above the main one).
- **Sandbox checks:** turn the feature on through the settings file, move the pointer into a corner, and check the host's Debug entry and that the action ran where that can be detected.
- **By hand:** a real mouse on one and several displays, a full-screen game or video, and an app running as administrator in the foreground.
