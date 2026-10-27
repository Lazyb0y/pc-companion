# Display Profiles

Save display setups as named profiles, such as Desk, TV and Presenting, and switch between them from the PC Companion icon's menu: which displays are on, the main display, the arrangement, and each display's resolution, refresh rate, orientation and HDR.

- Module: Display (`PCCompanion.Modules.Display`). Settings section: `features.displayProfiles`. Issue: [#55](https://github.com/Lazyb0y/pc-companion/issues/55).
- Inspired by KDE Plasma and GNOME, which remember a setup for each set of displays and ask before keeping a change, and by autorandr, which saves display setups as named profiles on Linux.
- This first version leaves out each display's scale, which Windows sets only through undocumented functions; see [Not included](#not-included).
- Off until the user turns it on.

## Behavior

- The PC Companion icon's menu gets a **Display profile** submenu, above Settings and Exit, with the profiles in the order of the profile list. Choosing one switches the displays to it.
- The profile that matches the current setup has a check mark (a radio mark), the first one when several match. When none matches, for example after a change in Windows Settings, the submenu starts with a dimmed **Other display setup in use**.
- A profile that needs a display that isn't connected is dimmed.
- The submenu ends with **Display Profiles settings**, which opens the feature's page.
- The submenu shows only while the feature is on and has at least one profile.
- After a switch that could leave a display black, PC Companion asks whether to keep the new setup, and goes back to the previous one after 15 seconds without an answer; see [Keep or revert](#keep-or-revert).
- The page shows the same: **In use** on the matching profile, and a **Use** button on the others, available while the feature is on.
- PC Companion stores nothing about which profile is in use; it compares the current setup with the profiles each time the displays change.
- PC Companion never changes the displays on its own: not when it starts, not when the feature is turned on or off, and not when a display is connected or disconnected. Then Windows does what it always does: it brings back the setup last kept with the same displays, which is the last profile the user kept with them. Turning the feature off or uninstalling PC Companion leaves the displays as they are.

## Compared with other systems

- **Windows** remembers one setup for each set of connected displays, and Windows + P switches between four fixed ones: PC screen only, Duplicate, Extend and Second screen only. Windows can't keep two setups for the same displays, such as a desk setup and a TV setup at another resolution and refresh rate, and Windows + P changes no resolution, refresh rate, main display or HDR. Profiles do, in two clicks, and ask before keeping a change, as Windows Settings does.
- **KDE Plasma and GNOME** also remember one setup for each set of displays, and ask before keeping a change made in their settings, but have no named setups. GNOME recognizes a display partly by its connector, so a setup is lost when a dock or a DisplayPort hub numbers its connectors differently. Profiles recognize a display on any connection.
- **autorandr** and other profile tools for Linux save named setups and switch when the displays change, but ask nothing before keeping one. autorandr recognizes a display by its whole EDID, which some displays change; KDE Plasma stopped using it for that reason. Profiles use the model and serial number from the EDID, as KDE Plasma does now.
- **macOS** remembers one arrangement for each set of displays, with no named setups, and can't tell identical displays apart, so they swap places after sleep. Tools that add named setups are paid or for the command line. Profiles tell identical displays apart by their serial numbers, or by their connections when they have none.
- **Display profile tools for Windows:** the most complete one is paid. The free ones mostly ask nothing before keeping a setup, so a mode a display can't show leaves it black until a restart in safe mode, and their profiles often stop working after a graphics driver update or a cable change, until they're saved again, because they keep the graphics adapter's ID, which changes. Some apply a setup several times in a row to make it stick. Profiles ask before keeping a setup, save it in Windows only once it's kept, keep no adapter IDs, check the setup they applied, and need no admin rights.

## Profiles

A profile holds a name and a setup, as Windows Settings shows it in **System > Display**:

- **The displays that are on.** The profile also names the displays that were connected and off when it was made, so the page can show them. Every connected display that isn't on in the profile is turned off when the profile is used, as with **Disconnect this display** in Windows Settings.
- **The main display**, one of those that are on, as with **Make this my main display**.
- **The arrangement:** each display's position on the desktop, as the user drags it in Windows Settings. Displays at the same position, with the same resolution, show the same picture, as with **Duplicate these displays**.
- For each display that's on, its **display resolution**, its **refresh rate** as Windows reports it, such as 59.94 Hz, its **display orientation** (Landscape, Portrait, Landscape (flipped) or Portrait (flipped)), and **Use HDR**, on or off, for displays that support HDR.

Making and changing profiles:

- A profile is made from the current setup, never drawn by hand: the user arranges the displays in Windows Settings, then selects **Add from current setup** and gives the profile a name. **Update from current setup** replaces a profile's setup with the current one. So every profile is a setup that Windows accepted and the user saw working, and Windows Settings stays the place to arrange displays.
- When the feature is first turned on, the list starts with one profile, **Original setup**, made from the current setup, so the user always has a way back. When the setup can't be read then, such as in a remote session, the list starts empty.
- Up to 20 profiles, each with up to 16 displays. In a list edited by hand, the profiles after the 20th are left out of the menu, with a warning in the log, and the page says so on each. Names are up to 60 characters, and no two profiles have the same name, ignoring case.
- A profile that can't be used, such as one with no display on, no main display or two, a main display that isn't at the top-left corner of the desktop, displays that overlap without showing the same picture or that leave a gap between them, or a value out of range, is left out of the menu, with a warning in the log that gives its number. The page shows the problem on the profile.
- A profile matches the current setup when the same displays are on, recognized as below, with the same main display, positions, resolutions and orientations, the same refresh rates to two decimal places, as Windows Settings shows them, and the same HDR setting on displays that support it.

## Recognizing displays

- For each connected display, Windows gives the maker and model from the display's EDID, the data every display reports about itself, its name, such as `DELL U2720Q`, its connection type, such as HDMI, DisplayPort or built-in, and its device path, which names the connection it's on (`DisplayConfigGetDeviceInfo` with `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME`). That gives no serial number, so PC Companion reads the display's EDID too (`DisplayMonitor.GetDescriptor`, found by the same device path), for its serial number: the number and the serial number text, either of which a display may leave empty.
- The device path changes when the same display is plugged into another port, dock or graphics adapter: Windows keeps a separate entry for each connection a display has been on. The graphics adapter's ID changes at every restart and when its driver restarts. So neither identifies a display, and a profile keeps neither as one.
- A profile recognizes each of its displays, in this order:
  1. by its model and serial number, on any connection, when it has a serial number that no other connected display of that model has;
  2. by its model and connection, for identical displays without serial numbers, or with the same one, which some models report;
  3. by its model alone, when only one display of that model is connected.
- A laptop's built-in display is recognized by its connection type and model, so a profile made with the laptop's own screen recognizes it on any dock.
- A display without an EDID, such as some virtual displays, has no model, and is recognized by its connection only.
- When two identical displays without serial numbers are both on other connections than when the profile was made, PC Companion can't tell them apart, and takes them in the order of their connections. The page says so on the profile; when their places come out swapped, **Update from current setup** sets them right.
- A profile can be used when every display that's on in it is connected and recognized. Otherwise it's dimmed in the menu, and the page says which display isn't connected. A profile is never applied in part: a setup made for three displays rarely suits two, and for those two, Windows already brings back the setup last kept with them.
- The page names a display by its name from the EDID, and the laptop's own as **Built-in display**. Two displays with the same name also show their connection type and their place in the profile's picture.

## Switching

- PC Companion switches the displays with `SetDisplayConfig`, the documented function that Windows Settings uses, which needs no admin rights:
  - it reads the connected displays (`QueryDisplayConfig` with `QDC_ALL_PATHS`), recognizes the profile's displays among them, and builds the setup on the adapters and connections they're on now: a path for each display that's on, with its position, resolution and orientation, and its refresh rate, kept as the exact fraction Windows reported, such as 60000/1001, with the signal timing left for Windows to choose; displays at the same position share a source, as with Duplicate; and no path for the others, which turns them off;
  - Windows checks the setup first (`SDC_VALIDATE`), and when it refuses, nothing changes;
  - then Windows applies it (`SDC_APPLY` with `SDC_USE_SUPPLIED_DISPLAY_CONFIG`), without saving it yet; see [Keep or revert](#keep-or-revert);
  - HDR is set after that, display by display: with `DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE` on Windows 11 version 24H2 and later, where turning on color management for displays without HDR is a separate setting, and with `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE` before. Whether a display supports HDR is read the same way: on version 24H2 and later, the older function also reports displays that support only color management.
- PC Companion never lets Windows change the setup it asks for (`SDC_ALLOW_CHANGES`): with it, Windows accepts any resolution and refresh rate and quietly applies another. Without it, Windows refuses a resolution or refresh rate a display doesn't offer, such as 144 Hz over a cable that carries only 60, and nothing changes. The page then says which display doesn't offer it, from the modes Windows lists for each display that's on (`EnumDisplaySettings`).
- After a switch, PC Companion reads the setup again and compares it with the profile. Windows can apply a setup in its own way, such as moving a display to close a gap, or choosing another refresh rate. The radio mark shows what's really in use, and a display that doesn't match the profile is named in a warning in the log and in a note on the page.
- Windows moves the windows from a display that's turned off to the others, and Windows 11 can put them back when the display returns, with **Remember window locations based on monitor connection**. Profiles don't move windows.

## Keep or revert

- After a switch that turns a display on or off, or changes a resolution, refresh rate, orientation or HDR, PC Companion asks **Keep these display settings?**, as Windows Settings does, in a Windows dialog with **Keep changes**, **Revert** and the seconds left. Without an answer within 15 seconds, the previous setup comes back. A switch that only moves displays or changes the main display can't leave a display black, so it's kept at once.
- The dialog shows in the middle of the new main display, in front of other windows, with the keyboard focus on **Keep changes**. **Revert**, Esc and closing the dialog go back at once. It's shown explicitly (`ShowWindow`) as it's created, since in a host that Windows started with a hidden window, it would otherwise stay hidden.
- The host shows the question and keeps the time, for a switch from the menu and from the page alike. The host has no window that a switch can move or turn off; the settings window, when it's on a display that the profile turns off, is moved by Windows like any other window, and the question doesn't depend on it.
- The new setup isn't saved in Windows' memory of setups until the user keeps it. **Keep changes** applies it again with `SDC_SAVE_TO_DATABASE`, which saves it without changing the displays' mode again, so Windows uses it the next time these displays are connected, as after a change in Windows Settings. So if the PC restarts, or a display is reconnected, before an answer, Windows brings back the setup it had saved.
- **Revert** applies the setup from before the switch, as Windows reported it then. When Windows refuses it, such as after a display was unplugged meanwhile, PC Companion returns to the last setup Windows saved for the displays connected now (`SDC_USE_DATABASE_CURRENT`), Windows' own way back from a setup that wasn't saved.
- Anything that means the user can't answer reverts at once: the PC locking, sleeping or switching to another user, the feature turning off, and PC Companion exiting.
- Choosing another profile while the question is open closes it and switches again; **Revert** then goes back to the last setup that was kept.
- If PC Companion stops unexpectedly while the question is open, the new setup stays until the PC restarts or the displays are reconnected, when Windows brings back the setup it saved. Windows + P works meanwhile.

## When profiles can't be used

- In a Remote Desktop session, and in other remote sessions such as Windows Sandbox's, Windows reports the session's own display, not the PC's, and refuses every change. So PC Companion neither reads nor switches the displays there (`SM_REMOTESESSION`): the profiles are dimmed, and the submenu starts with a dimmed **Display profiles aren't available in a remote session**; the page shows the same note, with its **Use** and **Add from current setup** buttons off.
- While Windows refuses to read the displays for another reason, the menu and the page keep showing what they last read, and a profile chosen then isn't applied, with a warning in the log.

## Threads

- Display Profiles has no thread of its own for its work: it waits for Windows to report a change, and switching is rare.
- **The host's UI thread** runs `Apply`, which reads the section, checks the profiles and starts or stops the feature's hidden window, and builds the submenu each time the PC Companion icon's menu opens, from the state the feature already has, without reading anything. Its hidden window gets `WM_DISPLAYCHANGE`, the arrival and removal of displays (`RegisterDeviceNotification` for `GUID_DEVINTERFACE_MONITOR`), and the session and power messages that make a question revert. The 15-second countdown runs on this thread, and its end passes the revert on. A chosen profile goes to the thread pool.
- **The thread pool** reads the displays and the setup (`QueryDisplayConfig`, `DisplayConfigGetDeviceInfo` and `DisplayMonitor`), switches and reverts (`SetDisplayConfig`, which waits while the displays change mode), one at a time and in order, and passes the results back to the UI thread with `MessageWindow.BeginInvoke`. Each change of the setup brings one `WM_DISPLAYCHANGE`, while `SetDisplayConfig` runs, and a display that arrives or leaves also brings a device message, so messages that come while a read is waiting or running lead to one more read, not one each.
- **The question's thread:** an STA thread with its own message loop shows the dialog (`TaskDialogIndirect`), so the host's UI thread, its menu and the other features keep running while it's open. It updates the seconds left each second; the UI thread decides when time is up, and closes the dialog.
- **The pipe:** the thread pool reads the settings app's requests and passes them to the UI thread; see [The settings app and the host](#the-settings-app-and-the-host).
- **The settings app** reads the displays and the setup the same way, on its own thread pool, and watches `WM_DISPLAYCHANGE` while the page is open. It never switches the displays itself. Only Original setup is read while its UI thread waits: turning the feature on for the first time, from Home or the page, reads the setup once on the thread pool and waits for the result, about 100 ms the first time, mostly for the first EDID.
- **Stopping** reverts a switch that waits for an answer, then removes the notifications, closes the hidden window and the pipe; a read that's running finishes, and its result is dropped.
- **Failures:** the thread pool callbacks and the question's thread run inside the feature's error boundary. A failure there is logged, reverts a switch that waits for an answer, and keeps the feature off until the host restarts, like a failing `Apply`. Windows refusing a read or a switch isn't such a failure: it's logged as a warning, and the feature keeps running.

## The PC Companion icon's menu

- The submenu comes through `IHostMenuItems`, with the submenus and radio marks of `PopupMenu`, as Network Profiles' does; see [Running in the host](../../architecture/features.md#running-in-the-host).
- The profiles can change while the menu is open, when the settings are saved meanwhile. A profile chosen then is applied only if the list still has it unchanged, wherever it is now; otherwise nothing changes, with a Debug entry in the log.

## The settings app and the host

- **Use** on the page asks the host to switch, through a named pipe that only the user's own account and the system can open, and that rejects remote clients, as Folder Rules' pipe does. The request gives the profile's number and its contents, and the host switches only if its list has that profile unchanged. So a switch from the page gets the same question, countdown and revert as one from the menu, whatever happens to the settings window.
- The settings app lets the host bring the question to the front (`AllowSetForegroundWindow`) before it asks.
- The page reads the current setup itself, for **Add from current setup**, **Update from current setup** and **In use**.

## Settings

- **Feature page:** the on/off switch; **Profiles**, a list with **Add from current setup**, and for each profile a picture of its arrangement, a line for each display, such as "DELL U2720Q: 3840 x 2160, 59.94 Hz, main display" or "Built-in display: off", the problem when it can't be used now, **In use** or **Use**, and **More options** with **Rename**, **Update from current setup**, **Move up**, **Move down** and **Delete**; a note on what profiles don't change, such as the scale, color and brightness; **Windows display settings**, whose **Open** opens Windows' Display page (`ms-settings:display`); and a **Permissions & data** card.
- **Add from current setup** and **Rename** open a dialog with the **Name** and the setup it holds. Problems show below the box, and **Save** is available only when the name has none. **Update from current setup** and **Delete** ask first.
- The switch on Home changes only `enabled` and keeps the profiles. Turning the feature on for the first time adds Original setup.
- A section that can't be read, for example after a hand edit, turns Display Profiles off with a warning in the log.

```json
"features": {
  "displayProfiles": {
    "enabled": true,
    "profiles": [
      {
        "name": "Desk",
        "displays": [
          {
            "model": "DELA0A3",
            "serial": "5f1c0e9a2b7d4c61",
            "connection": "\\\\?\\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            "builtIn": false,
            "name": "DELL U2720Q",
            "on": true,
            "main": true,
            "x": 0,
            "y": 0,
            "width": 3840,
            "height": 2160,
            "refreshRate": { "numerator": 60000, "denominator": 1001 },
            "orientation": "Landscape",
            "hdr": false
          },
          {
            "model": "BOE0A1E",
            "serial": "",
            "connection": "\\\\?\\DISPLAY#BOE0A1E#4&3a2b1c0d&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            "builtIn": true,
            "name": "",
            "on": true,
            "main": false,
            "x": 3840,
            "y": 960,
            "width": 1920,
            "height": 1200,
            "refreshRate": { "numerator": 120000, "denominator": 1000 },
            "orientation": "Landscape",
            "hdr": null
          }
        ]
      },
      {
        "name": "TV",
        "displays": [
          {
            "model": "SAM7088",
            "serial": "",
            "connection": "\\\\?\\DISPLAY#SAM7088#5&1f2e3d4c&0&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            "builtIn": false,
            "name": "SAMSUNG",
            "on": true,
            "main": true,
            "x": 0,
            "y": 0,
            "width": 3840,
            "height": 2160,
            "refreshRate": { "numerator": 120000, "denominator": 1000 },
            "orientation": "Landscape",
            "hdr": true
          },
          {
            "model": "DELA0A3",
            "serial": "5f1c0e9a2b7d4c61",
            "connection": "\\\\?\\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            "builtIn": false,
            "name": "DELL U2720Q",
            "on": false
          }
        ]
      }
    ]
  }
}
```

- `model` is the maker's code and the product code from the EDID, as in Windows' hardware IDs; `serial` is a hash of the serial number, empty when the display has none; `connection` is the device path when the profile was made; `hdr` is `null` for a display without HDR. A display that's `on: false` has only the fields that recognize and name it.

## Not included

- Each display's scale. Windows has no documented function to set it, only an undocumented one that Windows Settings uses. Profiles leave each display at the scale Windows keeps for it. Setting it behind a check of the Windows build, as the undocumented functions in [Windows support](../../compatibility/windows-support.md) do, comes later ([#236](https://github.com/Lazyb0y/pc-companion/issues/236)).
- Switching on its own when displays are connected or disconnected, which belongs to profiles and context triggers ([#56](https://github.com/Lazyb0y/pc-companion/issues/56)). Windows already brings back the setup last kept with the same displays, as KDE Plasma, GNOME and macOS do, and a kept profile is that setup; switching on its own would only choose between several profiles for the same displays, which needs other context, such as the network, the time or an app.
- Switching without the question, for a profile already kept with the same displays on the same connections ([#238](https://github.com/Lazyb0y/pc-companion/issues/238)).
- A keyboard shortcut or a command line for switching ([#237](https://github.com/Lazyb0y/pc-companion/issues/237)).
- Window positions for each profile.
- Color profiles, brightness, the brightness of SDR content in HDR, night light, color depth, dynamic and variable refresh rates, the scaling mode for resolutions that don't fill the screen, color management for displays without HDR, the wallpaper, and settings in the graphics driver's own control panel, such as custom resolutions and combined displays.
- Wireless displays, which connect only on request.
- Importing and exporting profiles.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The APIs are documented: `QueryDisplayConfig`, `SetDisplayConfig`, `DisplayConfigGetDeviceInfo` and `DisplayConfigSetDeviceInfo` (Windows 7 and later), `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE` (Windows 10 version 1709 and later), `DisplayMonitor` (Windows 10 version 1803 and later), `EnumDisplaySettings`, `RegisterDeviceNotification` and `TaskDialogIndirect`, from Common Controls 6, which the host's manifest already loads.
- `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2` and `DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE` are in the Windows SDK from build 26100, Windows 11 version 24H2, without a reference page yet. PC Companion uses them only on build 26100 and later.
- Displays on USB docks that use a driver of their own, displays behind a DisplayPort hub, and TVs are tested by hand.

## Security and privacy

- **Changes:** the PC's display setup, only when the user chooses a profile in the menu or selects **Use** on the page: which displays are on, the main display, the arrangement, and each display's resolution, refresh rate, orientation and HDR. Windows' memory of setups changes only when the user keeps a setup. The display setup is the PC's, not the user's alone, as with changes in Windows Settings, which every user can make without admin rights.
- **Safety:** a setup a display can't show is undone after 15 seconds without an answer, and isn't saved in Windows until it's kept, so a restart or a reconnection brings back the last kept setup. Modes a display doesn't list aren't applied.
- **Reads:** the connected displays' names, makers, models, EDIDs, connection types and device paths, and the current setup, when the displays change and when the feature starts.
- **Serial numbers** identify a display, so PC Companion keeps only a hash of each in the settings, never shows it and never logs it. It keeps nothing else from the EDID.
- **Settings:** the profiles are in `settings.json`. Any program running as the user can change that file, but it could also change the displays itself, with the same functions, so this crosses no boundary. Running as administrator changes nothing: setting the displays needs no admin rights.
- **Untrusted values:** a hand-edited file can hold anything, so every value is checked before it's used: the lengths, the ranges, one main display at the top-left corner, no control characters, and Windows' own check of the setup.
- **The pipe:** only the user's own account and the system can open it, it rejects remote clients, and a request can only choose a profile from the host's own settings.
- **Network:** none. `PRIVACY.md` gets a line on Display Profiles.
- **Logs:** Debug entries when a profile is chosen, applied, kept or reverted, and why, by number, when the profile that the setup matches changes, by number, and when displays are connected or removed, by model and connection type. Warnings when a profile can't be used, when the setup Windows applied doesn't match the profile, and when Windows refuses a read or a switch, with its error code. Never profile names, display names, serial numbers or their hashes.
- **Rights:** runs without admin rights and changes nothing that needs them.
- **Failures:** a switch Windows refuses leaves the displays as they were, and the page says so in a note until the next **Use**.

## Performance

- Between changes nothing runs: no timer and no polling. The hidden window gets Windows' messages.
- Opening the menu reads nothing. Reads and switches run on the thread pool; a display's EDID is read once for each device path and kept while the feature runs.
- Reading the setup takes about 0.1 ms. Reading an EDID through `DisplayMonitor` took 80 ms the first time and 12 ms after; loading it added 12 system DLLs, about 2 MB of private memory and 6 MB to the working set, mostly shared system code. A switch that changed the resolution took 30 to 50 ms, and saving it on **Keep changes** 6 ms; the display itself can take a second or more to show a picture again.

## Accessibility and localization

- The submenu is a Windows menu: it works with the keyboard, with screen readers, which read the radio mark as checked, and in high contrast, follows dark mode as the rest of the PC Companion icon's menu does, and is laid out right to left in right-to-left languages. A `&` in a profile's name shows as written.
- The question is a Windows dialog: Enter keeps and Esc reverts, screen readers read the question, the seconds left and the buttons, it uses the high contrast colors, and it's laid out right to left in right-to-left languages. Windows draws task dialogs light, also in dark mode.
- The page and its dialogs work with the keyboard alone, and keyboard focus stays on a profile when the list changes, as on Network Profiles' page. Buttons carry the profile's name, such as "Use Desk", and **In use** is announced.
- The arrangement picture is drawn as the displays stand, also in right-to-left languages, and in high contrast colors. Screen readers skip it: the lines below it say the same.
- Resolutions and refresh rates follow the user's regional settings, such as 59,94 Hz. Display names are shown as the display reports them, left to right.
- Original setup gets its name in the user's language when the feature is first turned on; after that it's a name like any other.
- The strings are translated like the other pages', marked "Needs native review."

## Spike results

A spike with a .NET 10 helper that reads, checks and switches the displays and shows the question, on a Windows 11 PC (build 26300) with one display on DisplayPort, and in Windows Sandbox (Windows 11 24H2), confirmed:

- **Reading:** `QueryDisplayConfig` took about 0.1 ms. The target name gave the display's maker, product code, name and device path, whose last part is the connection's target ID, and no serial number. The EDID, through `DisplayMonitor` and through the device registry key alike, held both a serial number and serial number text. `QDC_DATABASE_CURRENT` gave Windows' saved setup for the connected displays, which matched the setup in use.
- **Windows' check:** without `SDC_ALLOW_CHANGES`, `SDC_VALIDATE` refused every resolution and refresh rate the display doesn't list: 59.94, 75, 144 and 240 Hz at 1920 x 1080, where it lists only 60 Hz, and 1234 x 567 and 3840 x 2160, with `ERROR_BAD_CONFIGURATION` or `ERROR_GEN_FAILURE`. It accepted the listed modes, with the signal timing left to Windows. With `SDC_ALLOW_CHANGES`, it accepted all of them. A check took 1 to 9 ms.
- **Switching:** applying another resolution without saving took 30 to 45 ms and sent one `WM_DISPLAYCHANGE` while it ran, and Windows' saved setup stayed as it was. `SDC_USE_DATABASE_CURRENT` brought the saved setup back in 46 ms. Applying the same setup again with `SDC_SAVE_TO_DATABASE` took 6 ms, sent no message and saved it, without changing the mode again. A setup that changes nothing did nothing, unless forced with `SDC_NO_OPTIMIZATION`, which changed the mode again in 300 ms. No `WM_SETTINGCHANGE` or device message came.
- **HDR:** on build 26300, the older `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO` reported advanced color as supported on a display without HDR, which supports only color management, while `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2` reported no HDR.
- **The EDID:** `DisplayMonitor` took 80 ms the first time and 12 ms after, and loading it added 12 system DLLs, about 2 MB of private memory and 6 MB to the working set. The device registry key took 0.4 ms.
- **Windows Sandbox** is a remote session (`SM_REMOTESESSION`). `QueryDisplayConfig` worked there and reported the session's own display, with no EDID, at 32 Hz; `SDC_VALIDATE` refused every setup, even the one in use, with `ERROR_INVALID_PARAMETER`.
- **The question,** as a task dialog on its own STA thread: it showed 75 to 90 ms after the helper started, in front, with the keyboard focus on **Keep changes**. Enter and **Keep changes** through UI Automation kept; Esc, closing the window and **Revert** reverted; with no answer, the thread that kept the time clicked **Revert** for it. The seconds left changed each second, and UI Automation read the question, the seconds left and the buttons by name. With right-to-left layout, the dialog was mirrored.
- **A hidden start:** a program started with a hidden window, as Windows may start one, shows its first window as hidden. A task dialog in such a program stayed hidden until it was shown explicitly with `ShowWindow` when created, which then worked. So the question is always shown that way.
- **Dark mode:** the task dialog stayed light in dark mode, also after the same call that makes the menus dark.
- **Not checked yet**, all on real displays by hand: two or more displays, turning displays on and off, the main display, Duplicate, a rotated display; recognizing displays on other ports, docks and DisplayPort hubs, after a restart and a graphics driver update, and two identical displays; a laptop with its lid closed; HDR on an HDR display; a TV; a setup applied without saving followed by a reconnection or a restart; the scale after a change of resolution; switching while the session is locked or a UAC prompt is open; whether the question takes the focus after a menu choice and after **Use** on the page, which Windows Sandbox doesn't prevent; and the settings window on a display that's turned off.

## Testing

- **Unit tests** with a fake display system:
  - recognizing displays: by serial number on any connection, identical displays by connection, a model alone, the built-in display, two identical displays that can't be told apart;
  - matching: the displays that are on, the main display, positions, resolutions, refresh rates to two decimal places, orientations, HDR only where supported;
  - building the setup: a path for each display that's on, on its current adapter and connection, none for the others, displays at the same position sharing a source;
  - checks: one main display at the top-left corner, overlaps and gaps, ranges, control characters, the lengths, duplicate names, the 20-profile limit; unusable profiles are left out with a warning;
  - keep or revert: which switches ask; **Keep changes** saves; **Revert**, Esc and the end of the countdown bring back the previous setup unsaved, and the saved one when Windows refuses it; locking, sleeping, switching user, turning the feature off and exiting revert; another profile chosen during the question;
  - the menu: order, radio mark, Other display setup in use, dimmed profiles, the remote session note, `&` in names, no submenu while off or with no profiles; a profile changed or deleted while the menu is open isn't applied;
  - the pipe: only a profile the host has unchanged is applied.
- **Sandbox checks** (`scripts/sandbox/groups/DisplayProfiles.ps1`), in a remote session, where PC Companion neither reads nor switches the displays: the Home switch turns the feature on with no profiles; the page shows the remote session note with **Use** and **Add from current setup** off, and renames, moves and deletes profiles written into the settings by the check, with keyboard focus staying on the profile; the submenu, read with UI Automation, shows the note and the profiles dimmed; **Display Profiles settings** opens the page; turned off, the submenu is gone.
- **What the Sandbox can't check:** reading and switching the displays, so neither Original setup, **Add from current setup**, switching, the question, keeping and reverting, nor anything with more than one display, recognizing displays, HDR, refresh rates or a black screen.
- **By hand**, on real displays:
  - one display: each kind of change, the question, **Keep changes**, **Revert**, Esc and the countdown, from the menu and from the page, and a restart before answering;
  - two or three displays: turning displays on and off, the main display, Duplicate, the question on the new main display, the settings window on a display that's turned off, and a profile whose display isn't connected;
  - a display moved to another port and through a dock, after a restart and after a graphics driver update; two identical displays;
  - a laptop with its lid open and closed;
  - a mode a display can't show, reverted by the countdown;
  - HDR on Windows 11 version 24H2 or later and on Windows 10;
  - a Remote Desktop session;
  - Always run as administrator with UAC on;
  - a screen reader reading the submenu, the question and the page, and a right-to-left language.
