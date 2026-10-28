# Lock Keys

Show a short notice on screen when Caps Lock, Num Lock or Scroll Lock turns on or off, such as "Caps Lock on", on the display you're typing on.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.lockKeys`. Issue: [#40](https://github.com/Lazyb0y/pc-companion/issues/40).
- Inspired by the lock key notices of KDE Plasma and ChromeOS, and by the Caps Lock indicator of macOS.
- Off until the user turns it on.

## Behavior

- When a chosen lock key turns on or off, a notice shows for 2 seconds, then fades out: a lock icon and the key's state, such as "Caps Lock on" or "Num Lock off".
- Caps Lock and Num Lock are chosen by default, Scroll Lock isn't; see [Which keys](#which-keys). With **Show when a key turns off** turned off, only keys turning on show a notice.
- The notice shows on the display of the window in front, at the bottom center by default; see [Where the notice shows](#where-the-notice-shows).
- There's one notice at a time. A change while it shows replaces its text and starts its time again, so fast presses never stack notices. When more than one key changed since the last check, as when a remote session sets them all at once, the notice lists each, one per line, Caps Lock first.
- The notice never takes the focus, lets clicks through to what's below it, and isn't on the taskbar or in Alt + Tab.
- It doesn't show while an app is full screen on its display, unless the user turns this off; see [Full screen](#full-screen).
- Changes made while the user was away, such as on the lock screen or in a UAC prompt, show no notice afterwards; see [Changes made elsewhere](#changes-made-elsewhere).
- Optionally, a small badge stays on screen while Caps Lock is on; see [Keep showing while Caps Lock is on](#keep-showing-while-caps-lock-is-on).
- Lock Keys announces nothing to screen readers, which already say when a lock key changes; see [Accessibility and localization](#accessibility-and-localization).
- Apps running as administrator don't stop it: reading the keys' state needs no admin rights, so a key pressed while one is in front shows a notice too.

## Compared with other systems

- **Windows** shows nothing on screen for lock keys. Toggle Keys plays a tone, password boxes and the sign-in screen warn when Caps Lock is on, and Narrator says when a lock key changes. Windows 11 shows its own volume and brightness indicators at the bottom center of the screen, and can move them. Lock Keys shows its notice the same way, with the same three positions.
- **macOS** shows a Caps Lock badge at the text cursor that stays while Caps Lock is on, with no setting to turn it off, and users complain that it shows in full-screen games and virtual machines. Lock Keys can be turned off for each key, stays out of full-screen apps, and its badge that stays is optional.
- **KDE Plasma** shows the lock keys in a tray widget, and in an optional notification whose setting is hard to find. **GNOME** shows nothing beyond its warnings in password boxes. Extensions and tools for Linux desktops show panel icons or notices, some on every display at once. Lock Keys shows one notice, on the display where the user types.
- **ChromeOS** keeps a notice while Caps Lock is on, with an icon in the status area, and queues its screen-reader announcement so it doesn't cut off other speech. Lock Keys leaves the announcement to the screen reader, which already makes it, so nothing is said twice.
- **Laptop and keyboard makers' utilities** show large notices that updates turned on with no way to turn them off, that can minimize full-screen games, that show twice when two utilities run, and that stop working after sleep, with heavy background services. Lock Keys never takes the focus, stays out of full-screen apps, is off until the user turns it on, and costs about 0.1% of one processor core while on.
- **Lock key tools for Windows** either use a keyboard hook, which receives every key the user types, which antivirus software can take for a keylogger, and which Windows doesn't feed while an app running as administrator is in front, or check the keys up to 60 times a second. Their tray icons end up in the hidden icons menu, and their overlays can stack on top of each other. Lock Keys reads only the three keys' state, about 10 times a second, works while an app running as administrator is in front, and keeps one notice that changes in place.

## Which keys

- **Caps Lock** and **Num Lock**, chosen by default.
- **Scroll Lock**, not chosen by default: few programs use it, many laptops don't have it, and many KVM switches switch computers on two quick presses of it, which would show two notices.
- Each has its own switch on the page. **Show when a key turns off**, on by default, applies to all three.
- Pressing Shift to turn off Caps Lock, when the user set Windows to do that, and keyboard layouts whose Caps Lock works as Shift Lock, show the same notice: Lock Keys reads the state, not the key press.

## Where the notice shows

- **Display:** the display of the window in front (`GetForegroundWindow`, `MonitorFromWindow`), where the user's typing goes. With no window in front, such as on the desktop, the display with the pointer; when the pointer can't be read, the main display.
- **Position**, in the display's work area, so it stays clear of the taskbar wherever the taskbar is:
  - **Bottom center**, by default, 24 effective pixels above the bottom of the work area, where Windows 11 shows its own indicators;
  - **Top center**, 24 effective pixels below its top;
  - **Top left**, 24 effective pixels from its top and left edges.
- The positions are physical, the same in every language, as Hot Corners' corners are.
- A notice that's showing stays where it is when the window in front moves to another display; a change that replaces its text moves it there.
- A change of the displays while it shows (`WM_DISPLAYCHANGE`) hides it.
- **How long:** 2 seconds by default, from 1 to 5 seconds in half seconds. When Windows' animation effects are on (`SPI_GETCLIENTAREAANIMATION`), it fades in over 100 ms and out over 200 ms; otherwise it shows and hides at once.

## How the notice looks

- A rounded box like Windows 11's own indicators, with 8-pixel corners on Windows 11 and square ones on Windows 10. It holds a lock icon, closed for on and open for off, and the text, such as "Caps Lock on". Both are drawn at the display's scale, and the text also at Windows' text size (**Accessibility > Text size**).
- **Icon:** Lock (`E72E`) and Unlock (`E785`) from Segoe Fluent Icons on Windows 11, and from Segoe MDL2 Assets, which has the same icons, on Windows 10.
- **Text:** Segoe UI, 14 effective pixels high.
- **Colors** follow the Windows mode, like the taskbar's, not the settings app's theme. In the dark mode the box is near black with white text, in the light mode near white with black text, each with a thin border. The box is opaque, so the text stays readable over anything.
- **High contrast:** the theme's window background (`COLOR_WINDOW`) and text color (`COLOR_WINDOWTEXT`), with a 2-pixel border in the text color and square corners.
- The Windows mode, high contrast and the text size are read each time a notice shows, so a change shows at the next notice.
- No meaning depends on color: the icon and the text say the state.
- **Right to left:** in right-to-left languages, the box is mirrored, with the icon on the right, and the text is drawn right to left (`ETO_RTLREADING`).
- **How:** drawn with GDI into a 32-bit bitmap and shown in a layered window (`UpdateLayeredWindow`), with `LayeredWindow` and `TextMask` from `PCCompanion.Platform.Windows`. The window is topmost, can't be activated (`WS_EX_NOACTIVATE`, shown with `SW_SHOWNOACTIVATE`), lets clicks through (`WS_EX_TRANSPARENT`), and is a tool window, so it isn't on the taskbar or in Alt + Tab. Its class name is `PCCompanion.LockKeys.Notice`, and its name, which screen readers can read, is its text. A fade changes only the window's opacity, without drawing it again. `LayeredWindow` gets the click-through style and the opacity change, and `TextMask` text drawn right to left.

## Keep showing while Caps Lock is on

- Off by default. When it's on and Caps Lock turns on, the notice shows as usual, then, instead of fading out, shrinks to a small badge, the lock icon and "Caps Lock", at the same position, which stays until Caps Lock turns off.
- Like macOS and ChromeOS, for Caps Lock only: Num Lock is on most of the time, so a badge for it would rarely go away, and Scroll Lock is rarely used.
- The badge is drawn like the notice, smaller: 28 effective pixels high, with 12-pixel text.
- It moves to the display of the window in front when that changes (`EVENT_SYSTEM_FOREGROUND`), and is hidden while an app is full screen on that display, as the notice is. It's checked when the window in front changes and once a second while the badge is on, since a window can go full screen without changing which window is in front.
- A notice for another key shows next to the badge, above it at the bottom of the screen and below it at the top, so neither covers the other.
- It hides while the PC is locked or another desktop is in front, and when the displays turn off, and shows again after, when Caps Lock is still on.

## Full screen

- **Don't show while an app is full screen**, on by default: the notice and the badge don't show while an app is full screen on the display where they would. An app that's full screen on another display doesn't stop them.
- Full screen is decided with Hot Corners' rules, by the same code: a borderless window covering the display, ignoring overlays and always-on-top windows that don't cover it; exclusive full screen on the display of the window in front; and presentation mode on every display. See [Full screen and a captured pointer](hot-corners.md#full-screen-and-a-captured-pointer). The code moves from Hot Corners' folder to one the module's features share.
- Exclusive full screen skips the notice even with this setting off: a window can't show over it, and trying can take a game out of full screen.
- Like Hot Corners, the check reads the windows front to back until one decides, once for each notice, and sends no messages to other apps' windows.
- A skipped notice logs why at Debug level.

## Changes made elsewhere

The state is read again silently, without a notice, so the user doesn't get a late notice for a change made where the notice couldn't show:

- when the feature starts, as at sign-in or after its settings change, and when the host starts;
- when the session is unlocked or connected again (`WTSRegisterSessionNotification`, through `SessionMonitor`): on the lock screen, Windows warns in the password box when Caps Lock is on;
- when Windows switches back from another desktop, such as a UAC prompt or the sign-in screen (`EVENT_SYSTEM_DESKTOPSWITCH`). A desktop counts as another one when it can't be opened or isn't the user's own (`OpenInputDesktop`, whose name isn't `Default`);
- when the displays turn back on (`DisplayStateMonitor`), as after sleep.

While the session is locked, another desktop is in front or the displays are off, the check doesn't run, the notice and the badge hide, and nothing shows.

In a Remote Desktop session, the PC the user connects from sets the session's lock keys to its own when it connects, which reads silently, and again when its window gets the focus back. A change then is real for the session, so it shows a notice.

## Detecting a change

- The host reads the toggle state of the three keys (`GetKeyState`, the low bit) on its UI thread every 100 ms, with a coalescable timer (`CoalescableTimer`) that may be up to 50 ms late, so Windows can serve it with other timers' wakeups. A change shows at most about 150 ms after the key is pressed, usually under 100.
- Windows changes a lock key's state when the key goes down, so the notice can show while the key is still held. Presses closer together than one check show only the state at the end, which is the state the user gets.
- Microsoft documents `GetKeyState` as the calling thread's state, which changes as the thread reads its keyboard messages. In practice, Windows brings the state of a thread with no keyboard input waiting up to date with the keyboard's, so the host's UI thread, which never has the keyboard focus, reads the current state. .NET's `Console.CapsLock` and Windows Forms' `Control.IsKeyLocked` rely on the same, and the spike confirmed it; see [Spike results](#spike-results) and [Compatibility](#compatibility). `GetKeyboardState` isn't used: it gives the state only as of the last `GetKeyState`.
- **Why not another way:**
  - a low-level keyboard hook receives every key typed in every app, makes every key press wait for the host, is removed by Windows without notice when it's slow, gets nothing while an app running as administrator is in front, and is what antivirus software looks for in keyloggers;
  - raw keyboard input also receives every key, and gets nothing while an app running as administrator is in front, as the spike confirmed;
  - registering the lock keys as shortcuts (`RegisterHotKey`) would take them away from every app;
  - Windows reports lock key changes through no event, broadcast or accessibility interface, and keyboards are opened only by Windows itself.
- Reading the state also catches changes made without a key press, such as a remote session setting the keys, which a hook misses.

## Threads

- Lock Keys has no thread of its own: each check takes microseconds, so it runs on the host's UI thread, like App Windows' layout check. See [Threads and processes](../../architecture/features.md#threads-and-processes).
- **The host's UI thread** runs `Apply`, the check, the notice and the badge, on the feature's hidden window, which also gets the session, display state, display change and window events.
  - A check reads three states, 10 to 50 ns each.
  - A change finds the display and checks for full screen, then draws and shows the notice: drawing took 2 to 12 ms the first time and 0.5 to 2 ms after, and showing it under 2 ms the first time and under 0.7 ms after. Each step of a fade, about 0.1 ms, changes only the opacity.
  - The badge's check, once a second while it's on, reads the window in front's display and checks for full screen.
- Nothing waits for the disk, the network or another program: drawing uses only GDI, and the full-screen check sends no messages to other apps' windows. The host's `SlowHandlerWatch` times these handlers like every other.
- **Stopping** hides the notice and the badge, stops the timer, and removes the hidden window, the windows and the notifications.
- **Failures:** a failure in a handler is logged and stops Lock Keys until the host restarts, like a failing `Apply`; the other features keep running.

## Settings

- **Feature page:** the on/off switch; **Keys**, with a switch each for **Caps Lock**, **Num Lock** and **Scroll Lock**, and **Show when a key turns off**; **Position** (Bottom center, Top center, Top left); **Show for**, a slider from 1 to 5 seconds with the value before it; **Don't show while an app is full screen**; **Keep showing while Caps Lock is on**; **Sounds and screen readers**, which says that Windows can also play a sound when a lock key changes and that Narrator says it, with **Open** for Windows' keyboard accessibility settings (`ms-settings:easeofaccess-keyboard`), where Toggle Keys is; and a **Permissions & data** card.
- Changing the position, the duration or **Keep showing while Caps Lock is on** while the feature is on shows the notice once, with Caps Lock's current state, so the user sees the result. It's skipped while an app is full screen, like any notice.
- With none of the three keys chosen, the page says that no notices will show.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Lock Keys off with a warning in the log. A duration outside 1,000 to 5,000 ms is brought into range, and an unknown position is Bottom center.

```json
"features": {
  "lockKeys": {
    "enabled": true,
    "capsLock": true,
    "numLock": true,
    "scrollLock": false,
    "showWhenOff": true,
    "position": "BottomCenter",
    "durationMilliseconds": 2000,
    "ignoreFullScreenApps": true,
    "keepCapsLockBadge": false
  }
}
```

## Not included

- Following Windows 11's own **Position of on-screen indicators** setting. Windows keeps it in an undocumented registry value, so following it would need the version-specific code that [Windows support](../../compatibility/windows-support.md) requires, and only some of its values are known; see [Spike results](#spike-results). Lock Keys offers the same three positions instead.
- A notice for keyboard layout or input language changes. Windows shows its own switcher for Windows + Space and the layout in the taskbar, and reports no layout change to other programs: telling one apart from switching to a window with another layout would need its own design.
- Insert, whose overtype mode each app keeps for itself; Fn Lock, which keyboards handle themselves without telling Windows; and the modes of input methods, such as Kana, which Windows shows itself.
- A notice at the text cursor, which many apps don't report.
- Sounds, which Windows' Toggle Keys plays.
- Icons in the notification area, which Windows puts in the hidden icons menu.
- A badge for Num Lock or Scroll Lock, apps that turn notices off, a warning when Shift is pressed while Caps Lock is on, and clicking the notice to change a key.
- Notices on the lock screen, the sign-in screen and UAC prompts, where Windows warns about Caps Lock in the password box itself.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The APIs are documented and in every supported version: `GetKeyState`, `SetCoalescableTimer`, `UpdateLayeredWindow`, `GetDpiForMonitor`, `MonitorFromWindow`, `WTSRegisterSessionNotification`, `RegisterPowerSettingNotification` with `GUID_CONSOLE_DISPLAY_STATE`, `SetWinEventHook` with `EVENT_SYSTEM_FOREGROUND` and `EVENT_SYSTEM_DESKTOPSWITCH`, `OpenInputDesktop` and `SystemParametersInfo`, plus those of Hot Corners' full-screen check.
- Rounded corners and Segoe Fluent Icons on Windows 11 (build 22000 and later), square corners and Segoe MDL2 Assets on Windows 10.
- Reading the current state from a thread without the keyboard focus relies on how `GetKeyState` behaves, not on what Microsoft documents for it; see [Detecting a change](#detecting-a-change). .NET and most lock key tools rely on it too. The spike confirmed it on Windows 11; Windows 10 is tested by hand.

## Security and privacy

- **Reads:** the on or off state of Caps Lock, Num Lock and Scroll Lock, about 10 times a second while the feature is on. It never receives the keys the user types: no keyboard hook, no raw keyboard input, no shortcut. When a notice shows, and once a second while the badge is on, it reads which display the window in front is on, whether the pointer can be read, and, for the full-screen check, the windows' positions, style flags and whether they're cloaked or minimized, as Hot Corners does. It reads no window titles, class names, process names or contents.
- **Changes:** nothing. It never changes a lock key, and sends no input.
- **Logs:** Debug entries when a chosen key turns on or off, with whether the notice showed or why it was skipped, and when the state is read again after the user was away, and why. Debug entries are written only while detailed logging is on. Never anything else about the keyboard.
- **Settings:** the choices in `settings.json`, which can only turn notices on or off, so a changed file can't make Lock Keys do more.
- **Network:** none. `PRIVACY.md` gets a line on Lock Keys.
- **Rights:** runs without admin rights and needs none, also for apps running as administrator in front. With Always run as administrator, it works the same.

## Performance

- The check runs about 9 times a second and reads three values. In the spike, with three more reads in each check, it used 422 ms of processor time in 5 minutes, about 0.14% of one core, against none for a process that only slept. Its timer came every 111 ms on average, and never more than 134 ms apart.
- It stops while the PC is locked, another desktop is in front, or the displays are off. On PCs with Modern Standby, Windows stops desktop apps when the screen turns off, so it costs nothing then.
- A notice costs about 1 to 3 ms of the UI thread, and up to 12 ms for its first drawing; the badge's check once a second costs well under 1 ms.

## Accessibility and localization

- **Screen readers:** the notice isn't announced. Narrator says "Caps Lock on" and "Caps Lock off", and the same for Num Lock and Scroll Lock, when **Toggle keys** is on under **Have Narrator announce when I type**, which it is by default, and other screen readers do the same. An announcement from Lock Keys would be heard twice. The notice's window has its text as its name, so screen readers can still find it, and it never takes the focus. The page says so in **Sounds and screen readers**.
- The notice and the badge follow the display's scale and Windows' text size, use the high contrast colors, and say their state in words and with the icon, never only with color.
- Key names and states use Microsoft's terminology for each language, such as the German "Feststelltaste", and are translated like the page's strings, marked "Needs native review." In right-to-left languages, the notice is mirrored and its text runs right to left.
- The page works with the keyboard alone, and every switch, list and slider has a label for screen readers.

## Spike results

A spike with a .NET 10 helper on a Windows 11 PC (build 26300) with UAC on, and in Windows Sandbox (Windows 11 24H2), confirmed:

- **Reading the state:** a hidden window's thread that never had the keyboard focus, with only its timer, read every change of Caps Lock, Num Lock and Scroll Lock made in an app in front, within one check, up to about 110 ms after the key went down, with `GetKeyState` alone. `GetKeyboardState` showed each change one check later, only after `GetKeyState` had run. Each read took 10 to 50 ns.
- **Key down:** with a key held for a second, the state changed when it went down. Three presses 20 ms apart showed one change, to the final state; two presses 30 ms apart showed none.
- **An app running as administrator in front:** with Task Manager in front, reading the state caught all four changes made there; raw keyboard input, in the same process, got none of the key presses.
- **Locking:** the lock screen's picture is on the user's own desktop, where the check went on and saw Caps Lock change. The sign-in screen and a UAC prompt are another desktop: Windows reported the desktop switch, no window was in front and the pointer couldn't be read. A change made in a UAC prompt showed 57 ms after the prompt closed, so the state is read again silently then.
- **Cost:** the timer, at 100 ms with 50 ms of tolerance, came every 111 ms on average. With six reads in each check, it used 422 ms of processor time in 5 minutes on the PC, and 344 ms in Windows Sandbox; a process that only slept used none.
- **Drawing:** a notice took 2 to 12 ms to draw the first time, the Arabic text the most, then 0.5 to 2 ms at 100% to 300% scale, and under 0.7 ms to show. A fade step took about 0.1 ms. The Arabic text was shaped and ordered right, with the icon on the right.
- **Windows' indicator position:** **Position of on-screen indicators**, in **Settings > System > Notifications**, offers Bottom center, Top left and Top center. Choosing Top left wrote 2 to `PositionIndex` in `HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\ConfirmatorPosition`, which isn't documented.
- **Windows Sandbox** copies the lock keys of the Sandbox to the PC that runs it, late and not always right: keys sent inside it turned off the PC's Num Lock, and for a moment the PC had Caps Lock on while the Sandbox had it off. It doesn't take the PC's state when it starts. So the Sandbox checks never press lock keys.
- **Not checked yet**, all by hand: Remote Desktop to and from the PC, a virtual machine window, full-screen games, several displays with different scales, a keyboard without lock key lights, the notice in front of an app running as administrator that's always on top, sleep, and Windows 10.

## Testing

- **Unit tests** with a fake key state, a fake clock and fake windows:
  - a chosen key turning on or off shows one notice, an unchosen one none, and turning off shows none with **Show when a key turns off** off;
  - a change while the notice shows replaces it and starts its time again; two keys changed at once are listed, Caps Lock first;
  - no notice when the feature starts, after unlocking, after a switch back from another desktop, and after the displays turn on; the check stops while the session is locked, another desktop is in front, or the displays are off;
  - full screen skips the notice on its display, not on another; exclusive full screen skips it with the setting off;
  - the display: the window in front's, then the pointer's, then the main display; the three positions in work areas with the taskbar on each side, the same in right-to-left languages;
  - the badge: it stays while Caps Lock is on, goes when it turns off, follows the window in front to another display, hides for full screen and while away, and isn't used for Num Lock or Scroll Lock;
  - drawing, on bitmaps: the size at 96 to 288 DPI and with a larger text size, the light, dark and high contrast colors, rounded and square corners, and the icon on the right in right-to-left languages;
  - the settings: defaults, the duration's range, an unknown position, and the sample notice after a change of the position, the duration or the badge setting.
- **Sandbox checks** (`scripts/sandbox/groups/LockKeys.ps1`), which never press lock keys, since Windows Sandbox copies them to the PC that runs it: the Home switch turns the feature on; the page shows its settings in English and Arabic and saves each; changing the position while the feature is on shows the sample notice, found by its class name, with its name, at the bottom center and then the top of the work area, gone after its time, in the light and dark Windows modes and in Arabic, with screenshots; a borderless window covering the display skips it, and the host logs why; turned off, changing the settings shows nothing; and the host's UI thread is never held.
- **What the Sandbox can't check:** real key presses, so the notice for a key, the badge, and the cases of [Changes made elsewhere](#changes-made-elsewhere).
- **By hand:** Caps Lock, Num Lock and Scroll Lock in an app, with an app running as administrator in front with UAC on, including one that's always on top, on the lock screen and in a UAC prompt; fast presses and a held key; the badge, also across displays; several displays with different scales and the taskbar on another side; a full-screen video, and an exclusive and a borderless full-screen game; Remote Desktop to and from the PC, and a virtual machine window; sleep and resume; Narrator with **Toggle keys** on, hearing each change once; high contrast, a larger text size and a right-to-left language; a keyboard without lock key lights; and Windows 10.
