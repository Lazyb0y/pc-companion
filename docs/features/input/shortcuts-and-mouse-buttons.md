# Shortcuts & Mouse Buttons

Give PC Companion's actions keyboard shortcuts and mouse buttons of your own, such as Ctrl + Alt + H to hide the other apps, or the mouse's Forward button to switch to a display profile.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.shortcutsAndMouseButtons`. Issue: [#39](https://github.com/Lazyb0y/pc-companion/issues/39).
- Inspired by KDE Plasma's global shortcuts, which give any app's action a shortcut, and by the mouse-button shortcuts for Mission Control in macOS.
- Off until the user turns it on.

## Behavior

- **The page lists triggers.** Each one is a keyboard shortcut or a mouse button that runs one of PC Companion's actions, such as Hide Apps' Hide others, Show or hide Terminal from Drop-down Apps, or Use Office from Network Profiles. **Add** opens the editor: choose the action, then press the keys or choose the button.
- **Actions come from the other features,** and run as their own shortcuts and menu items do; see [Actions](#actions). A trigger whose feature is off does nothing, and its keys and button reach the apps as usual.
- **Shortcuts** go by the keys' position, so they stay on the same keys in every keyboard layout. Windows tells PC Companion only when one is pressed (`RegisterHotKey`), so there's no keyboard hook.
- **Mouse buttons:** Back and Forward, alone or with keys, and the middle button with the Windows key. The app under the pointer doesn't get a click that runs an action.
- **Not in these apps:** each trigger can skip apps, so the app gets the keys or the click itself, such as a browser that keeps its Back button.
- **Mouse buttons pause while an app is full screen,** such as a game, unless the user turns this off.
- **Conflicts are found while recording,** with Windows, other programs and PC Companion's own shortcuts, and every shortcut Windows refuses later shows on its page.
- **No trigger exists** until the user adds one.
- It runs only PC Companion's actions: it doesn't remap keys or buttons, send keys or Windows' shortcuts, type text or run programs.

## Compared with other systems

- **Windows** gives shortcut keys only to Start's and the desktop's shortcuts, to open a program, and they can wait seconds for a window that stopped responding. Back and Forward are app commands that each app handles, and Windows has no setting for them; mouse makers' software sets them only for their own mice.
- **KDE Plasma** gives any global action a shortcut, and since Plasma 6.1 can turn extra mouse buttons into key presses. Users report shortcuts that stop working or change keys in another keyboard layout, since a shortcut is bound to a character ([309754](https://bugs.kde.org/show_bug.cgi?id=309754), [375518](https://bugs.kde.org/show_bug.cgi?id=375518), [453661](https://bugs.kde.org/show_bug.cgi?id=453661)). Shortcuts here go by the keys' position.
- **GNOME** has custom shortcuts for commands only. Users report the same layout problems ([mutter#3153](https://gitlab.gnome.org/GNOME/mutter/-/issues/3153), [mutter#822](https://gitlab.gnome.org/GNOME/mutter/-/issues/822)), and a custom shortcut that another binding already has is saved and silently does nothing ([gnome-control-center#891](https://gitlab.gnome.org/GNOME/gnome-control-center/-/issues/891)). Here the editor says when a shortcut is taken, and the page says when Windows refuses one later.
- **macOS** gives app menu items shortcuts, and Mission Control, Application windows and Show Desktop a mouse button, but no other actions. It marks conflicting shortcuts with a warning.
- **Mouse makers' software** gives buttons actions and keys, per app, but only for their own devices, and only while their software runs.
- **Tools for Windows** that bind keys to scripts need scripts, and often use keyboard hooks, which Windows removes without telling them when they're slow.
- **What this feature adds:** one place for shortcuts and mouse buttons to PC Companion's actions, by key position, with conflicts found before saving, per-app exceptions decided in microseconds, and mouse buttons that stay out of games.

## Actions

| Feature | Action | What happens |
|---|---|---|
| Hide Apps | Hide app, Hide others, Show all hidden windows | As Hide Apps' shortcuts and menu item |
| Minimize to Tray | Send to tray | Sends the window in front to the tray, as Windows + Shift + Z |
| End App | Choose an app to end | Starts choosing, as Ctrl + Alt + Esc |
| Drop-down Apps | Show or hide *app*, for each listed app | As the app's own shortcut |
| App Windows | Next window of the app, Previous window of the app | As Alt + the key above Tab, and with Shift |
| Clipboard Actions | Actions for the copied text, Actions for the selected text | Opens the menu, as Windows + Alt + V and Windows + Alt + S; the second needs Selected text on |
| Display Profiles | Use *profile*, for each profile | Switches, with the same question and revert as the menu |
| Network Profiles | Use *profile*, for each profile | Switches, with the same checks as the menu |
| Shortcuts & Mouse Buttons | Lock, Turn off displays, Start the screen saver | As Hot Corners' actions (`ShellActions`) |

- **Each action does what its feature does** from its own shortcut or menu, with its settings, its checks and its notes: Display Profiles asks whether to keep the new setup, and Hide Apps plays the warning sound for a window that stays.
- **Actions that act on a window** act on the window in front, also for a mouse button.
- **Turn off displays and Start the screen saver** wait until the shortcut's keys or the button are released, then half a second, since any input turns the displays back on and ends the screen saver.
- **Available only while the action's feature is on,** supported on this PC, and has what the action names: a profile with that name, or that app in Drop-down Apps' list. Otherwise its shortcut isn't registered and its button isn't claimed, so they reach the apps, and the page says why at the trigger.
- **Not offered:**
  - Windows' shortcuts, other keys, text and programs: this isn't a key remapper. Hot Corners and Clipboard Actions run programs, and Start's shortcuts have shortcut keys.
  - Keep displays on, which lasts while the pointer stays in a corner.
  - Title Bar Actions' and Middle-Click Paste's actions, which act on the title bar or text box under the pointer.
- **Profiles and apps are named** as their features store them: a profile by its name, which is unique in its list, and a Drop-down app by its app ID or program. Renaming a profile leaves its triggers without one; the page says so at each.

## How features offer actions

```csharp
// PCCompanion.Core
public sealed record FeatureAction(string FeatureId, string ActionId, string? Target = null);

public interface IFeatureActions
{
    // Called on the host's UI thread; answers at once from what the feature already knows.
    bool CanRun(FeatureAction action);

    // Called on the host's UI thread; work that can wait goes to the feature's thread or the thread pool.
    void Run(FeatureAction action);
}

public interface IHostActions
{
    // Any thread. The host runs the action on its UI thread, through the feature that offers it.
    void Run(FeatureAction action);

    // Any thread. Whether the action can run now, as its feature answered after the last settings change.
    bool CanRun(FeatureAction action);

    // Raised on the host's UI thread after the host applied changed settings, so availability may have changed.
    event EventHandler? Changed;
}
```

- **A feature that offers actions** implements `IFeatureActions`, as it does `IHostMenuItems`. `Target` is a profile's name or a Drop-down app's app ID or program, read by the feature that offers the action.
- **The host** gives `IHostActions` to this feature, the only one that takes it. So modules still reference only Core, Platform.Windows and Localization; see [Projects](../../architecture/features.md#projects).
- **The settings app** lists the actions from each feature's catalog entry: an `Actions` function reads the feature's section and returns its actions with their names, such as "Use Office". A feature without one offers none.
- **Failures:** a feature whose `CanRun` or `Run` fails is stopped, like one whose menu choice fails.
- **The front:** Windows lets a process bring a window to the front right after it got a shortcut (`WM_HOTKEY`), so actions take the front as after their feature's own shortcut. After a mouse button, see [The unassigned key](#the-unassigned-key).
- **Adding an action later** changes only the feature that offers it: its `IFeatureActions`, its catalog entry and its spec.

## Keyboard shortcuts

### Which shortcuts

- **A shortcut** is one key with Ctrl, Alt or the Windows key, or several of them, and Shift if wanted. Shift alone isn't enough: it types.
- **F13 to F24** may be pressed alone or with any of the keys. No keyboard types with them, and mouse makers' software, programmable keypads and macro keys can send them, so a mouse's extra buttons work through them too.
- **The key:** a letter, digit or punctuation key, F1 to F11 and F13 to F24, Esc, Tab, Space, Enter, Backspace, Insert, Delete, Home, End, Page Up, Page Down, the arrows, Print Screen, Pause or the Menu key.
- **Never:**
  - F12, which Windows keeps for debuggers;
  - Windows + L and Ctrl + Alt + Del, which Windows never gives to a program;
  - the modifier keys alone, Caps Lock, Num Lock, Scroll Lock, input method keys, and media and browser keys, which Windows and apps use;
  - the numeric keypad, whose keys change with Num Lock.
- **Ctrl + Alt with a key that types a character with AltGr** in any keyboard layout the user has: Windows treats AltGr as Ctrl + Alt, so the shortcut would take the character from every app (spike: a registered Ctrl + Alt + E stopped AltGr + E from typing € in German). The check asks each layout (`GetKeyboardLayoutList`, then `ToUnicodeEx` with Ctrl and Alt down, and Shift when the shortcut has it, without changing the keyboard's state). It found € and @ in German, and ą, ę, ł, ś and ż in Polish (Programmers), and none in US English (spike).
  - The editor refuses such a shortcut, and names the layout and the character.
  - The host checks again when it registers a shortcut and whenever the user's layouts change, and doesn't register one that types a character, with a warning; the page shows why. So adding a layout later doesn't take its characters away.

### Keys by position

- **Stored by position,** with the names of the web's `KeyboardEvent.code`, such as `KeyH`, `Digit1`, `Backquote` and `F13`, which stand for scan codes.
- **Registered in the layout of the window in front:** the host maps each key to its virtual key there (`MapVirtualKeyEx`) and registers the shortcut again when it changes, as App Windows does for the key above Tab. `ShortcutKeeper` does this; it moves from Window Management to `PCCompanion.Platform.Windows`, so both modules use it, and takes any key, not only the key above Tab and the digits.
- **Keys that are the same in every layout,** such as F13 or the arrows, need no checks; while the user's layouts all agree on a shortcut's keys, no timer runs, as for App Windows.
- **Shown as the current layout writes them,** such as "Ctrl + Alt + ;" in US English and "Ctrl + Alt + Ö" in German, for the same key. Named keys, such as Esc, have their own strings.

### Registering

- **`RegisterHotKey` with `MOD_NOREPEAT`,** on the feature's thread, so holding the keys runs the action once. No keyboard hook.
- **With an app running as administrator in front,** Windows still reports the shortcut (as for Hide Apps and End App).
- **Alt and the app's menu bar:** the app in front gets the modifiers but not the key, so releasing Alt could open its menu bar. After a shortcut with Alt, the feature sends the unassigned key while Alt is still held, as Hot Corners does; see [The unassigned key](#the-unassigned-key).
- **Windows refuses a shortcut** that another program or Windows has (error 1409): a warning is logged, the page shows it at the trigger, and PC Companion tries again after any change to the settings or the layout.
- **Two of PC Companion's shortcuts on the same keys:** the editor refuses that; see [The editor](#the-editor). When it happens anyway, after a hand edit or when a feature is turned on from Home with its default shortcut, the one registered first keeps the keys, and the other's page shows which feature has them.

### Windows' own shortcuts

- **Windows registers nearly all of its shortcuts,** so `RegisterHotKey` refuses them, and a test registration finds them (spike): nearly every Windows + key shortcut in Microsoft's list, the Copilot key and the Office key, F12, Ctrl + Esc, Ctrl + Shift + Esc, Alt + Tab, Ctrl + Alt + Tab, Alt + F4, Windows + L and Ctrl + Alt + Del.
- **Some register, and then Windows or the apps lose them** (spike): Alt + Space, Alt + Esc, Ctrl + Space, Alt + Print Screen, Windows + /, Windows + Minus, Windows + Esc, Windows + Alt + H, Windows + Alt + Enter, Windows + J, Windows + W and Windows + Y. The editor knows these from a short list, and warns that the action takes the shortcut's place.
- **All of PC Companion's current shortcuts** registered and worked (spike).

## Mouse buttons

### Which buttons

- **Back and Forward** (`XBUTTON1`, `XBUTTON2`), alone or with any of Ctrl, Alt, Shift and the Windows key.
- **The middle button with the Windows key,** and any of the others. A middle-click without the Windows key belongs to the apps, to Middle-Click Paste and to Title Bar Actions, which never use the Windows key.
- **The keys must match exactly,** as for `PointerHook`'s other claims: a trigger for Back doesn't run on Ctrl + Back.
- **A press runs the action once,** however long the button is held.
- **Not offered:** the left and right buttons, the wheels, and a plain middle-click. Windows reports no more than five buttons; a mouse's other buttons can send F13 to F24 through its maker's software.
- **Back and Forward alone** stop going back and forward in browsers and File Explorer. The editor says so, and suggests adding those apps to **Not in these apps**, or a key.

### Keeping the click

- **The shared pointer hook,** `PointerHook`, keeps the press and its release from the app under the pointer: the app gets neither, no `WM_XBUTTONDOWN` and no `WM_APPCOMMAND`, and Edge and File Explorer don't go back (spike). See [Keeping the click from the app](../windows/title-bar-actions.md#keeping-the-click-from-the-app).
- **Its claims** cover every display. The hook is set only while some mouse-button trigger is active: its action is available, and it isn't paused for full screen.
- **`PointerHook` and `PointerKeeper` grow:**
  - the Back and Forward buttons, whose releases are kept with their presses, as the middle button's are;
  - a claim's windows where it doesn't apply, for [Not in these apps](#not-in-these-apps);
  - a claim that doesn't apply while the pointer is confined or hidden; see [Full screen and a captured pointer](#full-screen-and-a-captured-pointer).
- **The action runs** when the hook reports a kept press to the feature's thread.
- **Windows removing the hook:** `RawPointerMonitor` grows to report the Back and Forward buttons, and a press the hook never saw while it was set means Windows removed it. A warning is logged and the hook is set again, as for Middle-Click Paste; that press doesn't run the action.
- **Raw input reports kept presses too** (spike), so other features that watch the pointer still see them.

### The unassigned key

- **At each kept press,** the feature's thread sends the unassigned key `0xE8` (`ShellActions`' mask key) with `SendInput`. Apps ignore it, and it types nothing. It does two things:
  - **Start and the menu bar:** with the Windows key held, Start opens when the key is released, whether the click was kept or not. The unassigned key stops it (spike), and stops Alt from opening the menu bar, as for Hot Corners.
  - **The front:** after a kept press alone, Windows refused to let PC Companion bring a window to the front, which End App, Drop-down Apps, the Clipboard Actions menu and Display Profiles' question need. After the unassigned key, Windows allowed it (spike).
- **The second effect isn't documented.** If a Windows version changes it, actions still run after a mouse button, but a window they bring forward may flash on the taskbar instead of coming to the front; shortcuts are unaffected. A hand test on a real PC confirms it, and the Sandbox checks look for it on each run.

### Full screen and a captured pointer

- **While the window in front is full screen on its display,** mouse-button triggers pause: their claims are removed, so the buttons reach the app, and the hook isn't set at all unless another feature claims. The check is Hot Corners' (`FullScreenCheck`), for the display of the window in front.
- **When it's checked:** each time another window comes to the front, and when the window in front changes its place or size, which an out-of-context WinEvent hook (`EVENT_OBJECT_LOCATIONCHANGE`) reports for that window's process only, set again at each change of the window in front.
- **While an app confines the pointer to part of the screen, or hides it,** as games and virtual machines do, the hook lets the buttons through. It checks at each press (`GetClipCursor`, `GetCursorInfo`), which calls no other program.
- **The setting:** **Pause mouse buttons while an app is full screen**, on by default. The captured pointer check always applies.
- **Shortcuts don't pause:** they need Ctrl, Alt or the Windows key, which games seldom use, and a game that does can be added to the shortcut's **Not in these apps**.

## Not in these apps

- **Each trigger has its own list,** empty by default, with **Running app**, **Program file** and **Remove**, as Hot Corners' list. What counts as one app is `AppOfWindow`'s: the window's own app ID, its package's or its program.
- **Shortcuts:** while a listed app is in front, the shortcut isn't registered, so the app gets the keys itself; it's registered again when another app comes to the front. The app of the window in front is read at each change of the window in front, only while some shortcut has a list, and kept until that window closes. Reading it waits for no program.
- **Mouse buttons:** the app under the pointer decides, in the hook, at each press. The hook finds the top-level window under the pointer (`WindowFromPoint`, then `GetAncestor` with `GA_ROOT`) and compares it with the claim's windows of listed apps.
  - **How long:** 20 to 130 µs per press, and 28 µs over a window that stopped responding: it sends the window nothing (spike).
  - **The first call** took 3 to 5 ms, so the hook's thread makes one before it sets the hook.
  - **The windows of listed apps:** the feature's thread reads the app of each top-level window the taskbar shows when the trigger starts, and of each window that comes to the front, and keeps it until the window closes; the reading at the start runs on the thread pool. A window it hasn't read yet counts as not listed.
- **A skipped trigger** logs `AppExcluded` at Debug level, never which app.

## Apps running as administrator

- **Shortcuts** work while one is in front: Windows reports them to PC Companion. The action then works on that app only as far as its feature can; Hide Apps, for example, can't minimize its windows without admin rights.
- **Mouse buttons:** while one is in front, Windows gives the hook no input, so its buttons reach it as usual, and no trigger runs (spike, for [Title Bar Actions](../windows/title-bar-actions.md#apps-running-as-administrator)).
- **With Always run as administrator, or with UAC off,** both work with every app.
- **The page's note** follows the host's rights, read from its process when the page opens, as App Windows' note does.

## The settings app and the host

- **Pausing while recording:** while the editor listens for keys, the host pauses PC Companion's shortcuts, so pressing one records it instead of running it, and the editor's test registration isn't refused by PC Companion itself.
  - The settings app asks through a named pipe, `PCCompanion.Shortcuts.Pause.<session ID>`, that only the user's own account and the system can open, and that rejects remote clients (`RequestPipe`). The request says only "pause" or "resume".
  - A pause lasts 10 seconds and the editor renews it every 5 seconds while it listens, so a settings app that closes or stops never leaves the shortcuts paused.
  - Every shortcut is registered through `HotKey`, which removes them all on their own threads for the pause, and registers them again after it.
- **Shortcuts Windows refused,** for this page and every other page with a shortcut, such as App Windows' ([#175](https://github.com/Lazyb0y/pc-companion/issues/175)): each feature tells `ShortcutRefusals` which of its shortcuts Windows refused, and the host passes the list through a named pipe, `PCCompanion.Shortcuts.Status.<session ID>`, with the same protections, which reads nothing (`SnapshotPipe`). Each page reads it while it shows, and its note goes away once Windows takes the shortcut. Every feature with shortcuts already reports to it, and its page shows the note. PC Companion never learns which program has a shortcut.
- **The rest** the page works out itself, from the settings and the layouts: an action whose feature is off or that has no such profile or app, and a shortcut that types a character with AltGr.

## With the other features

- **Each feature keeps its own shortcut setting,** with its fixed choices. This page lists them, read only, in **Shortcuts on other pages**, each with a link to its page, so every shortcut of PC Companion can be seen in one place.
- **Clipboard Actions** gets None for both its shortcuts, so its menus can have other shortcuts here ([#259](https://github.com/Lazyb0y/pc-companion/issues/259), the shortcut part; a shortcut for each rule stays there).
- **Display Profiles and Network Profiles** get a shortcut for each profile through this feature ([#237](https://github.com/Lazyb0y/pc-companion/issues/237), [#209](https://github.com/Lazyb0y/pc-companion/issues/209), the shortcut parts; the command lines stay there).
- **Drop-down Apps'** apps can have any shortcut here, besides the choices on its page.
- **Middle-Click Paste and Title Bar Actions** share the pointer hook. Their claims never overlap with these, which need the Windows key for the middle button; if they did, the claim made first would win, as for any two claims.
- **Hot Corners** keeps its own actions; sharing the other features' actions with corners is [#152](https://github.com/Lazyb0y/pc-companion/issues/152)'s.
- **App Windows' exceptions** ([#123](https://github.com/Lazyb0y/pc-companion/issues/123)) can reuse how a shortcut is left to a listed app in front.
- **Their specs change** when this is built: Clipboard Actions' Not included loses "Choosing another shortcut"; Display Profiles' and Network Profiles' Not included lose shortcuts; and [Features](../../architecture/features.md) gains `IFeatureActions`.

## Threads

- **A thread of its own,** derived from `MessageLoopThread<TSettings>`, with its own hidden window. Shortcuts need a message loop, and a kept press must start the unassigned key at once, while the Windows key is still held. It runs:
  - the shortcuts, through `ShortcutKeeper`, and their checks for AltGr;
  - the WinEvent hooks for the window in front and its place, reading its app and checking full screen;
  - the claims and their windows of listed apps;
  - the kept presses from `PointerHook`, and the unassigned key;
  - the waits before Turn off displays and Start the screen saver;
  - passing each action to the host through `IHostActions`, except its own three, which it runs itself.
- **`PointerHook`'s thread** compares each event with the claims, finds the window under the pointer for a claim with listed apps, and checks the captured pointer. It calls no other program and waits for nothing.
- **The host's UI thread** runs `Apply`, which passes the section to the feature's thread; each action, through the feature that offers it; and the check of a press the hook never saw.
- **The thread pool** runs the pipes and reads the apps of the windows at the start.
- **Nothing waits for another app:** no message is sent to another program's window. Windows' own shortcut keys wait for windows that stopped responding; these don't.
- **Stopping** unregisters the shortcuts, removes the claims and WinEvent hooks, and ends the thread.
- **Failures:** a failure on the feature's thread is logged and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), on one display, with input sent by `SendInput`, confirmed the following. It ran in two rounds of about 5 minutes.

- **Keeping Back and Forward** in a low-level hook, press and release: a test window got no `WM_XBUTTONDOWN`, `WM_XBUTTONUP` or `WM_APPCOMMAND`, and Edge and File Explorer didn't go back or forward. Let through, they did. Each call of the hook took 2 to 40 µs, and raw input still reported the kept presses.
- **Not in these apps,** decided in the hook: with Edge's process listed, Edge went back and forward, while the test window's presses were kept. Finding the window under the pointer, its top-level window and its process took 20 to 130 µs, and 28 µs over a test window that had stopped responding; the first call took 3 to 5 ms.
- **The Windows key and the middle button:** Start opened when the Windows key was released, whether the click was kept or not. The unassigned key, sent while the Windows key was held, stopped it.
- **The front,** with Windows' foreground lock in force in the Sandbox:

  | Before PC Companion brings a window to the front | Result |
  |---|---|
  | Nothing | Refused |
  | One of its shortcuts was pressed | Allowed |
  | Its hook kept a Back button | Refused |
  | Its hook only watched a click | Refused |
  | Its hook kept the button, then it sent the unassigned key | Allowed |

  A Back button that reached an app that ignores it let any process take the front, also with no hook, most likely because the shell handled the command.
- **Windows' shortcuts:** about 110 shortcuts were registered one at a time, with Character Map in front; the ones refused and the ones taken from Windows are listed in [Windows' own shortcuts](#windows-own-shortcuts). Every shortcut PC Companion has today registered and worked.
- **AltGr:** a registered Ctrl + Alt + E took AltGr + E from German, so € wasn't typed. The check with Ctrl and Alt found the characters listed in [Which shortcuts](#which-shortcuts).
- **Recording in WinUI,** with `PreviewKeyDown` marked handled:
  - every free shortcut arrived with all its keys, such as Windows + Alt + V, Windows + Ctrl + Alt + 1, Windows + Ctrl + Esc, Ctrl + Tab, Shift + F10, F13, F24 and the Menu key, and Start didn't open;
  - Tab, Enter, Space and Esc arrived too, so they need handling; see [The editor](#the-editor);
  - a shortcut Windows or another program has arrived as a key release with no press, such as Windows + E, Ctrl + Shift + Esc and the Copilot key;
  - Alt + Space opened the window menu, Alt + Esc and Ctrl + Alt + Esc switched windows, and Alt + F4 closed the window, even when handled: Windows acts on them in the window itself;
  - AltGr arrived as Right Alt with a Left Ctrl that Windows adds, which tells it from Ctrl + Alt.
- **Not checked yet:** a real mouse and its maker's software; the unassigned key and the front on a real PC; UAC on; Sticky Keys; the editor's handling of Alt + Space, Alt + Esc, Ctrl + Alt + Esc and Alt + F4; several displays; Narrator; full-screen games; Windows 10.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Shortcuts and mouse buttons,** the list of triggers: each row shows the keys or the button, the action, and a warning when it does nothing and why, with **Edit** and **More options**, which has **Delete**; an empty list says so; **Add**;
  - **Pause mouse buttons while an app is full screen**, on by default;
  - **Shortcuts on other pages**, read only;
  - notes: Back and Forward alone stop working in apps unless they're listed; apps running as administrator;
  - a **Permissions & data** card.
- **The icon** is drawn as [Icons](../../design/icons.md) says: a keycap with a mouse beside it, in colors reviewed before it's drawn.
- **When the feature is first turned on,** the list is empty and the full-screen pause is on.
- **The switch on Home** changes only `enabled` and keeps the triggers.

### The editor

- **A dialog** with **Action**, **Trigger** and **Not in these apps**, and **Save**, available only when the trigger has no problem.
- **Action:** a list of every action of the features supported on this PC, by feature, such as "Hide Apps: Hide others" and "Display Profiles: Use Office". An action whose feature is off shows a note with a link to its page.
- **Trigger: Shortcut or Mouse button.**
- **Shortcut:** a box that says "Press the keys" and listens while it has the focus:
  - it shows the keys as they're held, and keeps the shortcut once a key that isn't a modifier is pressed;
  - keys that can't start a shortcut, such as Tab, Esc and Enter without Ctrl, Alt or the Windows key, keep their usual meaning, so keyboard users can leave the box and the dialog; pressed with Ctrl, Alt or the Windows key, they're recorded;
  - a key release with no press means Windows or another program has the shortcut;
  - Alt + Space, Alt + Esc, Ctrl + Alt + Esc and Alt + F4 are kept from Windows' handling in the window while the box listens (`WM_SYSCOMMAND`), to be confirmed when it's built;
  - **Choose keys** shows check boxes for Ctrl, Alt, Shift and the Windows key and a list of keys, for keys that can't be pressed, such as F13 to F24 on most keyboards, or when pressing several keys at once is hard.
- **The checks,** as the shortcut is recorded or chosen, with the reason below the box:
  - the rules in [Which shortcuts](#which-shortcuts), such as "Add Ctrl, Alt or the Windows key.";
  - another feature's shortcut: "Hide Apps uses this shortcut.", with a link to that page; another trigger here: "Lock already has this shortcut.";
  - AltGr: "In Polish (Programmers), Ctrl + Alt + A types ą.";
  - a test registration on the settings app's own window, removed at once, and the key release with no press: "Windows or another program uses this shortcut.";
  - the short list in [Windows' own shortcuts](#windows-own-shortcuts): a warning, "Windows or apps use this shortcut. This action takes its place.", which allows **Save**.
- **Mouse button:** a list with Back, Forward and Middle, and check boxes for Ctrl, Alt, Shift and the Windows key; Middle checks the Windows key and keeps it checked. The same check for duplicates applies, and Back or Forward alone shows the note about apps.
- **Not in these apps:** the app list, as on Hot Corners' page.

### The format

- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log.
- **A trigger that can't be used** is skipped with a warning: an unknown action or key, a shortcut against the rules, a trigger with both or neither of `shortcut` and `mouseButton`, a middle button without the Windows key, or the same keys or button as a trigger earlier in the list. The page shows it with the problem, so it can be fixed or deleted.
- **Listed apps** are read as Hot Corners reads its list.

```json
"features": {
  "shortcutsAndMouseButtons": {
    "enabled": true,
    "triggers": [
      {
        "action": { "feature": "hideApps", "id": "hideOthers" },
        "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyH" }
      },
      {
        "action": { "feature": "dropDownApps", "id": "showOrHide", "target": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
        "shortcut": { "modifiers": [], "key": "F13" }
      },
      {
        "action": { "feature": "displayProfiles", "id": "use", "target": "Desk" },
        "mouseButton": { "button": "Forward", "modifiers": [] },
        "excludedApps": [
          { "name": "Microsoft Edge", "appId": "MSEdge" }
        ]
      },
      {
        "action": { "feature": "shortcutsAndMouseButtons", "id": "lock" },
        "mouseButton": { "button": "Middle", "modifiers": [ "Windows" ] }
      }
    ],
    "pauseMouseButtonsInFullScreen": true
  }
}
```

- `action`: the feature's ID, the action's ID, and a `target` for actions on a profile or a Drop-down app. The IDs are `hideApps`: `hideApp`, `hideOthers`, `showAll`; `minimizeToTray`: `sendToTray`; `endApp`: `chooseApp`; `dropDownApps`: `showOrHide`; `appWindows`: `next`, `previous`; `clipboardActions`: `copiedText`, `selectedText`; `displayProfiles`: `use`; `networkProfiles`: `use`; `shortcutsAndMouseButtons`: `lock`, `turnOffDisplays`, `screenSaver`.
- `modifiers`: any of `Control`, `Alt`, `Shift` and `Windows`.
- `key`: a `KeyboardEvent.code` name from [Which shortcuts](#which-shortcuts), such as `KeyA`, `Digit1`, `Semicolon`, `F13`, `Escape`, `ArrowLeft` or `ContextMenu`.
- `button`: `Back`, `Forward` or `Middle`.
- `excludedApps`: as Hot Corners' list; optional.

## Not included

- **Remapping keys or buttons,** sending keys or Windows' shortcuts, typing text, and running programs or scripts.
- **Mouse gestures,** which #39 also names: drawing a shape with a button held is a feature of its own, with its own research, and can reuse the pointer hook.
- **The wheel, the left and right buttons, and a plain middle-click.**
- **Key sequences,** such as Ctrl + K then Ctrl + C, pressing a key twice, and holding a key.
- **Numeric keypad keys.**
- **Shortcuts for some apps only,** or another action per app; only the list of apps to skip.
- **Acting on the window under the pointer** after a mouse button; Title Bar Actions does that on title bars.
- **Taking another feature's shortcut from this page** ("Use it here"): the editor links to that page instead.
- **A keyboard hook,** also in the settings app while recording: it would keep Windows' own shortcuts from running while the user tries them, but sees every key.
- **Shortcuts on the lock screen and in UAC prompts,** which Windows shows on a desktop of its own.
- **Hiding a chosen app with its own shortcut** ([#295](https://github.com/Lazyb0y/pc-companion/issues/295)), which is an action of Hide Apps or Drop-down Apps that its spec decides.
- **Other features' actions,** such as Dynamic Wallpaper's or System Meters', which their specs can add.
- **Importing other tools' shortcuts.**

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - keys: `RegisterHotKey`, `MapVirtualKeyEx`, `GetKeyboardLayoutList` and `ToUnicodeEx`, whose flag that leaves the keyboard's state alone needs Windows 10 version 1607;
  - the pointer: `SetWindowsHookEx` with `WH_MOUSE_LL`, `WindowFromPoint`, `GetAncestor`, `GetClipCursor`, `GetCursorInfo` and raw input;
  - windows: `SetWinEventHook`, and `SendInput` for the unassigned key.
- **The unassigned key's effect on the front isn't documented.** If Windows changes it, actions after a mouse button lose only the front; see [The unassigned key](#the-unassigned-key).
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:**
  - only the shortcuts it registered, when they're pressed, through `WM_HOTKEY`: no keyboard hook;
  - the Back, Forward and middle buttons and the keys held then, in the shared pointer hook; other pointer input is passed on without being read;
  - with listed apps, the top-level window under the pointer at a press, and the app of the window in front and of the windows the taskbar shows: their app ID, package and program;
  - for the full-screen pause, the places and styles of the windows in front, as Hot Corners does;
  - the user's keyboard layouts, for the AltGr check;
  - the settings app's editor reads keys only while its box listens, in its own window.
- **Changes:** it registers shortcuts, keeps the chosen buttons from the apps, sends the unassigned key, which types nothing, and runs the chosen actions of other features, which have their own reviews. It writes no file and starts no program.
- **Settings:** a changed settings file can only choose shortcuts, buttons, PC Companion's own actions and apps to skip; never a program, a command or text. The actions run with their features' checks: Display Profiles still asks, End App still asks before ending.
- **Pipes:** only the user's own account and the system can open them, and remote clients are rejected. The status pipe reads nothing and gives only which shortcuts Windows refused. The pause pipe reads only "pause" or "resume"; a program that uses it can pause PC Companion's shortcuts, which it could also do by changing the settings file.
- **Safety:**
  - no trigger exists until the user adds one, and Windows + L and Ctrl + Alt + Del are never taken;
  - the hook calls no other program, decides in microseconds, and isn't set while no mouse trigger is active, such as while a game is full screen;
  - a shortcut is never registered while it would take a character from a keyboard layout.
- **Logs:**
  - **Debug entries:** a trigger ran an action, by its feature and action IDs, with a shortcut or a button; a trigger was skipped, and why: a listed app, full screen, a captured pointer or its feature off.
  - **Never** profile names, app names, app IDs, paths or titles, which can name documents, places or users, or any key that isn't a registered shortcut.
  - **Warnings** when Windows refuses a shortcut, when a shortcut types a character with AltGr, when a trigger can't be used or the section can't be read, and when Windows removed the hook.
  - **An error** when a failure stops the feature.
- **Network:** none. `PRIVACY.md` gets a line on Shortcuts & Mouse Buttons.
- **Rights:** it runs with the host's rights: without admin rights unless Always run as administrator is on or UAC is off. Turning the feature off unregisters its shortcuts, removes its claims and ends its thread.

## Performance

- **Between presses,** only the registered shortcuts and, while a mouse trigger is active, the shared hook: 2 to 40 µs for each button event in the spike, and 20 to 130 µs more for a press over a window with listed apps. Moves and other buttons are passed on at once.
- **At each change of the window in front:** the layout check that `ShortcutKeeper` already does, and, only while needed, reading the app of the window and the full-screen check.
- **The place-and-size hook** reports only the process of the window in front.
- **No timer runs** while the user's layouts agree on the shortcuts' keys.

## Accessibility and localization

- **Keyboard:** the editor works with the keyboard alone; Tab and Esc leave the box, and **Choose keys** gives every shortcut without pressing it. Sticky Keys is tested by hand.
- **Screen readers:** the box is named "Shortcut for <action>", and announces the keys recorded and any problem (a live region). Each row reads its keys, its action and its warning.
- **Names:** modifier and named keys have their own strings in Microsoft's terms, such as "Windows logo key" and "Ctrl", which German writes "Strg"; other keys are shown as the user's layout writes them. Mouse buttons are "Back button", "Forward button" and "Middle button".
- **Colors and layout:** the page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages, as the other pages' shortcuts are.
- **Motion:** none.
- **Strings:** translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake layouts, fake windows, a fake clock and fake Windows calls:
  - **the rules:** every allowed and refused key, F13 to F24 alone, Shift alone, F12, the numeric keypad;
  - **AltGr:** German, Polish (Programmers) and US English layouts, with and without Shift;
  - **key positions:** mapping in several layouts, and registering again when the layout changes;
  - **conflicts:** duplicates here and with other features' shortcuts, Windows' short list, and refusals;
  - **mouse buttons:** claims for Back, Forward and Windows + middle with exact keys, the release kept with its press, listed windows, the captured pointer, and the full-screen pause;
  - **Not in these apps:** registering and unregistering with the window in front, and the windows of listed apps;
  - **actions:** availability by feature, profile and app, the waits before Turn off displays and Start the screen saver, and each feature's `Actions` and `IFeatureActions`;
  - **the settings:** defaults, the spec's example, and each kind of trigger that can't be used.
- **Sandbox checks** (`scripts/sandbox/groups/ShortcutsAndMouseButtons.ps1`), with test windows the checks start:
  - **the page:** adding, editing and deleting triggers; the recorder with free, taken and AltGr shortcuts; **Choose keys**; the pause while it listens;
  - **shortcuts:** one runs Hide Apps' Hide app on a test window, and the same shortcut with the test window's app listed reaches the window instead; releasing Alt after an Alt shortcut doesn't open a test window's menu bar;
  - **mouse buttons:** Back runs an action and the test window gets nothing; with the test window's app listed, it gets the button; Windows + middle runs an action and Start doesn't open; a full-screen test window gets the button; an action after a button takes the front;
  - **the status:** a shortcut another test program holds shows its note on this page and on App Windows';
  - **the page** in English and Arabic, in light and dark, with screenshots;
  - no feature holds the host's UI thread, as every group checks.
  - **Never** Lock, Turn off displays or Start the screen saver, which would stop the checks, and never a lock key, which the Sandbox passes to the PC: those are unit-tested and tested by hand.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **By hand:**
  - a real mouse's Back and Forward, with and without its maker's software, and extra buttons sending F13 to F24;
  - the unassigned key and the front on a real PC, with Drop-down Apps, End App, the Clipboard Actions menu and Display Profiles' question;
  - Lock, Turn off displays and Start the screen saver, from a shortcut and a button;
  - UAC on, with an app running as administrator in front, and with Always run as administrator;
  - German and Polish layouts, Sticky Keys, Narrator and a right-to-left language;
  - full-screen games, including ones with anti-cheat software;
  - several displays at different scales;
  - Windows 10.
