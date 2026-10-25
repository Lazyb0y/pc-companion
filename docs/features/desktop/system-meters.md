# System Meters

See how busy the PC is, with live CPU, memory and network use on the taskbar.

- Module: Desktop (`PCCompanion.Modules.Desktop`). Settings section: `features.systemMeters`. Issue: [#53](https://github.com/Lazyb0y/pc-companion/issues/53).
- Inspired by iStat Menus in macOS and the system monitor widgets of KDE Plasma, and placed on the taskbar like Windows' own Widgets entry.
- Off until the user turns it on.

## Meters

| Meter | What it measures | Tooltip |
|---|---|---|
| CPU | How busy the processors are, in percent, as Task Manager shows it | CPU: 42% |
| Memory | How much of the physical memory is in use, in percent, as Task Manager shows it | Memory: 61%<br>9.7 GB of 15.9 GB in use |
| Network | How fast the PC's network adapters receive and send data, in bits per second, as Task Manager shows it | Network<br>Receive: 1.2 Mbps<br>Send: 80 Kbps |

- The user turns each meter on or off on the feature's page. When the feature is first turned on, all three show.
- **Show on** chooses where: **Taskbar**, the default, puts all the meters in one item on the taskbar; **Notification area** gives each meter its own icon there. Where the taskbar item can't show, the meters use notification icons until it can; see [When the strip can't show](#when-the-strip-cant-show).
- The PC Companion icon stays as it is and still opens the settings.
- The meters update every second by default; **Update every** offers 1, 2 or 5 seconds.
- Use is measured between two readings, so the meters show their first values one update after they start.

## On the taskbar

The meters share one item on the taskbar, the strip, in the free space next to the Widgets entry or the notification area.

- **Columns:** CPU, memory and network, each in its own column; a meter that's off leaves out its column. The network column shows the receive rate above the send rate, each after its arrow.
- **Layout** chooses how CPU and memory show:

| Layout | CPU and memory |
|---|---|
| Values and names (default) | Each in its own column: the icon, the value, and below it the name |
| Large values | Each in its own column: the icon and the value in large text, the height of two lines, without the name |
| Compact | Together in one column, CPU above memory, each with a small icon and no name |

- **Icons** chooses their style:
  - **Color**, the default: a blue processor and a green memory module, drawn by PC Companion, a green download arrow and an orange upload arrow. Their mid-tone colors read on light and dark taskbars alike.
  - **One color:** thin outline icons from Segoe Fluent Icons, in the taskbar's text color.
  - **None:** no icons, for the narrowest strip; the rates keep plain ↓ and ↑ arrows in the text color, which tell them apart.
- **Text:** values in Segoe UI Variable Text Semibold and names and rates in Segoe UI Variable Text, 12 pixels high at 100% scale, two lines on the taskbar; large values are in Segoe UI Variable Display Semibold, 26 pixels high. White on a dark taskbar and black on a light one, like the clock, smoothed in gray since ClearType needs a known background.
- **High contrast:** text and icons in the theme's text color (`COLOR_WINDOWTEXT`); the Color style draws the One color icons, and no meaning depends on color: the arrows' direction tells receive from send.
- Icons and values are centered by their visible pixels, not by the font's line box, which has room above and below.
- Each value has a fixed place, sized for its widest value, such as `100%` and `99.9 Kbps`, so the strip keeps its width as the numbers change. At 100% scale, in English, it's 252 pixels wide with Values and names and icons, 192 without icons, 297 with Large values and icons, and 237 without icons; Compact is the narrowest, at 160 and 132.
- **Scale:** sizes follow the DPI of the main display, where the taskbar's notification area is, and the strip is laid out again when it changes.
- **Right to left:** in right-to-left languages, the columns run right to left, CPU first; digits, rates and arrows aren't mirrored. The strip goes next to the notification area on whichever side Windows puts it.
- **Hover:** a rounded highlight, as on taskbar buttons, and a tooltip with every meter's name and values.

### How the strip works

- **Window:** a layered, topmost tool window that never takes the focus (`WS_EX_LAYERED`, `WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE`), drawn pixel by pixel with `UpdateLayeredWindow`. Its background is almost transparent, so clicks between the letters still reach it. Drawn text and icons are kept, so an update draws only text that changed.
- **Finding room:** UI Automation (`IUIAutomation`, a COM API, not a UI framework) lists the taskbar's buttons, with a cache request for three properties only: the bounding rectangle, the automation ID and the class name. App buttons have the class `Taskbar.TaskListButtonAutomationPeer`; Start, Search, Task View and Widgets have the automation IDs `StartButton`, `SearchButton`, `TaskViewButton` and `WidgetsButton`; the notification area is the taskbar's child window `TrayNotifyWnd`. The free space runs from the last app button to the Widgets entry, when it's on the notification area's side, or else to the notification area. The strip stands at the end of that space, 4 pixels from the Widgets entry or the notification area.
- The positions of the taskbar's own child windows can't be used: on Windows 11 they're out of date. The task list's window ends where its first buttons do.
- **When it looks:** at start; when a window that gets a taskbar button appears or goes away (`EVENT_OBJECT_SHOW` and `EVENT_OBJECT_HIDE`, half a second after the last one, to group them); when the taskbar's settings change (`WM_SETTINGCHANGE`), as for its alignment or the Widgets entry; when the taskbar restarts (`TaskbarCreated`); when the displays or their scale change; and every 30 seconds, for changes nothing reports, such as pinning an app. While the meters are paused, it doesn't look; it looks once when they resume, since the taskbar may have changed meanwhile.
- **Staying in sight:** the strip is topmost. When Explorer reorders its windows, moves the focus, takes the mouse or opens a menu (`EVENT_OBJECT_REORDER`, `EVENT_OBJECT_FOCUS` and `EVENT_SYSTEM_MENUSTART` to `EVENT_SYSTEM_CAPTUREEND`, from Explorer's process only), when another window comes to the front (`EVENT_SYSTEM_FOREGROUND`), and at each update, it checks whether the taskbar has come above it, and if so raises itself. A click on the taskbar brings the taskbar to the front, so the strip blinks out for a moment before it's back.
- **Full screen:** while Windows takes topmost away from the taskbar, as for a full-screen app, the strip hides with the taskbar and comes back with it.
- **Start and flyouts:** while Start, Search or one of the taskbar's flyouts is open, Windows draws the taskbar in a layer above apps' windows, which apps can't join, so the strip is hidden until they close.
- The strip is never a child or an owned window of the taskbar: Windows would then tie the taskbar's input to the host's thread, and a host that stops answering could freeze the taskbar.

### When the strip can't show

- The automation IDs, class names and child windows above aren't a documented API, so, as with the tray menu's dark mode, the strip runs only on the Windows builds it was tested on: Windows 11 from build 22621 (version 22H2) to `MeterStrip.MaxTestedBuild`, now 26300. Raise it after testing a new release.
- On Windows 10, from version 2004 to 22H2 (builds 19041 to 19045), the strip goes at the end of the task band, the taskbar's band of app buttons (`MSTaskSwWClass`), after its last button. Windows 10 keeps the task band's position up to date, and other toolbars and the news entry are outside it. The strip draws in Segoe UI and Segoe MDL2 Assets there, since Windows 10 has neither Segoe UI Variable nor Segoe Fluent Icons. It isn't tested on Windows 10 yet.
- The meters show as notification icons instead:
  - on other builds;
  - while the taskbar hides itself (`ABM_GETSTATE` reports `ABS_AUTOHIDE`);
  - while the taskbar has no room for the strip, as with many app buttons, is too low for two lines of text, as with small taskbar buttons, or stands on a side of the display, as Windows 10 allows;
  - while the taskbar isn't running, or doesn't answer UI Automation, as while it restarts.
- They move back to the strip as soon as it can show, and a Debug entry in the log says why they moved.

## In the notification area

With **Show on** set to **Notification area**, or where the strip can't show, each meter has its own icon.

### Icons

- **CPU and memory** show the percentage as a whole number, without the percent sign, as large as fits: two digits fill the icon's width, one digit is drawn at the same size, so the number doesn't change size between 9 and 10, and `100` is drawn smaller to fit.
- **Network** shows two outlined slots side by side, like two battery icons: receive on the left and send on the right. Each fills from the bottom on a logarithmic scale: empty at 1 Kbps or less, half full at 1 Mbps and full at 1 Gbps or more, with the same height for each tenfold step. Any traffic above 1 Kbps fills at least one row, and the empty slots keep the icon recognizable when nothing moves.
- **Colors:** white in the dark Windows mode and black in the light mode, like Windows' own taskbar icons, on a transparent background. In high contrast, the theme's text color (`COLOR_WINDOWTEXT`). No meaning depends on color.
- The Windows mode and high contrast are read before each update, as the tray menu reads them when it opens, so a change shows at the next update.
- **Size:** the small icon size for the DPI of the main display, where the notification area is: 16 pixels at 100% scale, 20 at 125%, 24 at 150% and 32 at 200% (`GetSystemMetricsForDpi` with `SM_CXSMICON`, and `GetDpiForMonitor`). It's read before each update, so a new scale shows at the next update. Icons are drawn at that size, never scaled, so the digits stay sharp.
- **Digits:** Segoe UI Semibold, with the digits 0 to 9 in every language, as the settings app shows numbers.
- **Right to left:** digits and slots aren't mirrored, so numbers read as everywhere else and receive stays on the left.
- **How:** drawn in the host with GDI into a 32-bit bitmap, then made into an icon with `CreateIconIndirect`. Digits are drawn white on black and their brightness becomes the transparency; slots are set pixel by pixel. The taskbar copies each icon it gets, so the host destroys the icon once it's sent.

### Taskbar icons

- The icons belong to the meters thread's hidden window and have fixed IDs: 2 for CPU, 3 for memory and 4 for network; the PC Companion icon is 1. Windows remembers by program path and ID where each icon goes, on the taskbar or in the hidden icons menu, so a meter keeps its place after a restart, after it's turned off and on, and after an update.
- Windows puts the newest icon first, so the icons are added network first and CPU last, to show as CPU, memory and network.
- They use IDs rather than GUIDs: Windows ties an icon's GUID to the program's path, and refuses it from another path, such as after moving from a per-user to an all-users install, unless both builds are signed by the same company.
- When the taskbar restarts, Windows sends `TaskbarCreated` and the icons are added again, as the PC Companion icon is. When the taskbar isn't running yet, early at sign-in, they're added when it starts. When an update fails because the taskbar lost an icon, the icon is added again.
- An icon is sent to the taskbar only when its picture or tooltip changed.
- While the host runs as administrator, the window accepts the taskbar's messages, as the PC Companion icon's window does.

### Hidden icons

- Windows 11 and Windows 10 put new notification icons in the hidden icons menu (^), and the user chooses which icons show on the taskbar. Windows gives apps no documented way to show their own icons, and PC Companion doesn't try.
- The feature's page says how to bring the meters out: drag an icon from the hidden icons menu onto the taskbar, or turn it on in the taskbar settings. **Open taskbar settings** opens `ms-settings:taskbar`. The page doesn't name the section, which differs between Windows versions.

## Tooltips

- The tooltips name each meter and give its values, as in the table above, in the user's language. The strip's tooltip lists every meter that shows.
- Numbers follow the user's regional settings, such as `9,7 GB` in German, and percentages the language's percent format, such as `42 %` in French.
- Memory is in GB of 1,024 MB, with one decimal, as in Task Manager.
- Rates are in Kbps, Mbps and Gbps of 1,000, as in Task Manager: with one decimal below 100, such as `0.4 Kbps` or `56.0 Kbps`, and none from 100, such as `120 Kbps`. No traffic shows as `0 Kbps`.
- A notification icon's tooltip has at most 127 characters, the limit Windows sets; tests check that every language fits.
- Windows refreshes a tooltip that's open when the values change, and shows tooltips in right-to-left languages right to left, so they need no Unicode control characters.
- Screen readers read a notification icon's tooltip as its name. Windows 11 names a notification icon with the tooltip it was added with, followed by its current tooltip, as with its own network icon ("Network Network / Internet access"). So each meter's icon is added with just the meter's name, such as "CPU", and gets its values right after: screen readers then read "CPU CPU: 42%", never a value from when the icon was added. When the user moves an icon onto the taskbar, Windows names it again with the tooltip it has then, such as "CPU: 5% CPU: 42%", until the taskbar restarts or the meters start again.

## Selecting the meters

- Clicking the strip, or selecting a meter's icon with the mouse or with Enter or Space from the keyboard, opens Task Manager, which shows what uses the CPU, memory and network.
- Task Manager opens on its own default page, which the user chooses in Task Manager: **Settings > Default start page** on Windows 11, **Options > Set default tab** on Windows 10. Windows has no documented way for another app to choose the page, and PC Companion doesn't change Task Manager's settings. The feature's page says how to make Task Manager open on Performance.
- Right-clicking the strip, or right-click, Shift + F10 or the menu key on an icon, shows a menu with **Task Manager**, **Settings** and **Exit**. Settings and Exit do what they do in the PC Companion icon's menu, and the menu follows the Windows mode, dark or light, as that one does (`MenuTheme`).
- The host gives features its Settings and Exit commands through a small interface in Core, so the module doesn't depend on the host. They run on the host's UI thread; see [Threads](#threads).
- **How:** `Taskmgr.exe` from the System32 folder, started through the shell (`ShellExecuteEx`) with no arguments, as Windows starts it from the taskbar. That way Windows gives it admin rights for administrators without asking, as with Ctrl + Shift + Esc. Nothing from the settings or the user reaches its path or arguments.

## Reading the values

- **CPU:** the performance counter `\Processor Information(_Total)\% Processor Utility`, read through PDH. It's what Task Manager's Processes and Performance tabs show: it accounts for the processors' speed and covers every processor group. It's capped at 100%, as in Task Manager, since it goes above 100 while processors run faster than their base speed. The counter is added by its English name (`PdhAddEnglishCounter`), so it's found in every Windows language.
- When the counter can't be added or read, for example because performance counters are turned off, the CPU meter uses `GetSystemTimes`: the share of time the processors weren't idle. That can differ from Task Manager under load, and on PCs with more than 64 logical processors it covers only one processor group. A warning is logged once.
- **Memory:** `GlobalMemoryStatusEx`. In use is the physical memory minus the available memory, as in Task Manager.
- **Network:** `GetIfTable2`: the bytes received and sent (`InOctets` and `OutOctets`) by the adapters that are up and are hardware, such as Ethernet, Wi-Fi and cellular adapters. Filter, loopback and virtual adapters, such as VPNs and Hyper-V's virtual switch, are left out: their traffic also passes through a hardware adapter, so it would count twice.
- A rate is the change since the last reading divided by the time between the two readings. That time is measured rather than assumed, so a late update doesn't inflate the rate. An adapter that appears or disappears between two readings, or whose counts go back, as when it's reset, adds nothing to that update.
- Each meter reads only while it shows; the PDH query is open only while the CPU meter is on.
- **When:** a coalescable timer on the meters thread's window (`SetCoalescableTimer`), so Windows can serve it with the same wakeup as other timers.
- **Pauses:** while the session is locked or disconnected (`WTSRegisterSessionNotification`, as Hot Corners uses it) and while the display is off (`RegisterPowerSettingNotification` with `GUID_CONSOLE_DISPLAY_STATE`), since no one can see the meters then. They resume once Windows reports the session unlocked or connected again, whichever it reports first, while the display is on. They keep their last values, and the next values show one update after the meters resume. While they're paused, the strip doesn't look for room either.

## Threads

- The meters work all the time, so they run on a thread of their own, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: an STA thread with its own hidden window and message loop. `Shell_NotifyIcon` waits for Explorer at every update, with no time limit, opening the PDH query takes about half a second, and finding the strip's room takes up to 70 ms; on their own thread, a slow Explorer or counter delays only the meters, never the other features.
- **The host's UI thread** runs only `Apply`: it reads the section, then starts the meters thread, passes it the new settings with `MessageWindow.BeginInvoke` on its window, or asks it to stop. It never waits for the meters thread.
- **The meters thread** runs everything else: the timer, the readings (PDH, `GlobalMemoryStatusEx` and `GetIfTable2`), the strip's window and its UI Automation queries and event hooks, drawing the strip and the icons, `Shell_NotifyIcon`, `TaskbarCreated` and the icons' messages, the session and display notifications, the menu, and starting Task Manager with `ShellExecuteEx`. It gets its own copy of the settings, so the two threads share no state.
- The meters thread never sets its own language, so it follows the host's, which `CultureInfo.DefaultThreadCurrentUICulture` sets; a new language shows at the next update.
- **Settings and Exit** from the meters' menu run on the host's UI thread: the host's implementation of the Core interface passes them there. Opening the settings app is UI-thread code, and `MessageLoop.Quit` ends only the loop of the thread that calls it.
- **Stopping:** the meters thread removes the strip and its icons, releases the PDH query, its hooks and its windows, and ends. When the feature is turned on again before the old thread has ended, the new thread waits for it before it creates its window. When the host exits, it waits up to 2 seconds for the thread to remove its icons, so none is left on the taskbar. It's a background thread, so one that doesn't end in time doesn't keep the host running.
- **Failures:** an unhandled exception on the meters thread would end the host, so everything there runs inside the feature's error boundary, as the other features guard their handlers: the strip's own window and event handlers get the boundary from the meters thread. A failure is logged, removes the strip and the icons, ends the thread and keeps the feature off until the host restarts, like a failing `Apply`.

## Spike results

A spike in Windows Sandbox, on Windows 11 build 26100, and on a desktop PC with Windows 11 build 26300, confirmed:

- Opening the PDH query took 0.37 s in the Sandbox and 0.54 s on the PC, and added 1.5 to 3.3 MB; a reading then takes 10 to 20 µs. Processor utility and processor time differ even at rest: 7 to 10% against 10 to 15% over the same seconds on the PC.
- `GetIfTable2` takes about 1 ms, since it lists every interface: 40 on the PC. The Sandbox's network adapter counts as hardware, and its three filter interfaces report the same byte counts, so leaving filters out keeps traffic from counting four times.
- Drawing and creating a number icon takes about 0.2 ms, and the network icon 0.1 ms. 10,000 icons leaked no GDI or USER handles.
- With all three notification icons on the taskbar and sent every second, Explorer used about 0.4% of one processor; sending only the icons that changed, about 0.1%, within the 0.2% it varies at rest. The meters used about 0.2%.
- `Shell_NotifyIcon` has no time limit: with Explorer suspended, one update waited 56 seconds, until Explorer resumed. While Explorer restarts, updates fail at once, and `TaskbarCreated` arrives once the new taskbar is ready.
- Windows refreshes an open tooltip, shows Arabic tooltips right to left without control characters, and names each notification icon for screen readers with the tooltip it was added with, followed by the current one.
- New notification icons go to the hidden icons menu, newest first. In both high contrast themes tried, Windows' own taskbar icons use `COLOR_WINDOWTEXT`.
- The strip found its room with UI Automation in 30 to 70 ms, between the last app button and the Widgets entry on a left-aligned taskbar, and before the notification area on a centered one. The taskbar's own child windows gave stale positions.
- Looking for room every 3 seconds cost Explorer 1.25% of one processor; looking only on the events above, with drawn text kept, the strip used about 0.3% and Explorer stayed within its usual variation.
- A click on the taskbar brings it above the strip. Raising the strip at each update left it hidden for up to a second; reacting to Explorer's `EVENT_OBJECT_REORDER` brings it back within a blink. Placing the strip right after the window above the taskbar, instead of making it topmost, left it stuck behind the taskbar after Start or a full-screen window.
- While Start is open, the strip is hidden, and it comes back when Start closes.
- `FindWindow` doesn't find the strip's window, though `EnumWindows` lists it, so the Sandbox checks look for it with `EnumWindows`.
- The menu opens from the meters thread, and Task Manager started through the shell in 69 ms.
- Turning the Sandbox's network adapter off took 0.13 s, and it was back with its gateway 0.4 s after turning it on. Pings to the gateway get no answer, since this PC's firewall drops them, but count as sent; requests to Windows' connectivity check address give traffic both ways.
- Not tested in the Sandbox: Windows 10, display scales other than 100%, a full-screen app, which neither Edge's full screen nor a window covering the screen produced there, the display turning off and the session locking, UAC prompts, and the taskbar settings list, since Windows in the Sandbox isn't activated.

## Settings

- **Feature page:** the on/off switch; **Meters**, with a switch each for CPU, Memory and Network; **Show on**, Taskbar or Notification area; **Layout** and **Icons**, for the strip, and turned off with Notification area; **Update every**, 1, 2 or 5 seconds; a note that Task Manager opens on its default start page; with Notification area, a note about the hidden icons menu with **Open taskbar settings**; and a **Permissions & data** card.
- When the feature is first turned on, all three meters show on the taskbar, with Values and names and color icons, and they update every second.
- The switch on Home changes only `enabled` and keeps the other settings.
- With every meter off, the feature shows nothing and reads nothing.
- A section that can't be read, for example after a hand edit, turns System Meters off with a warning in the log. An unknown `showOn`, `layout` or `icons` uses its default, with a warning. An interval outside 1 to 10 seconds is brought into range; one the list doesn't offer, such as 3, works, and the list shows none chosen.

```json
"features": {
  "systemMeters": {
    "enabled": true,
    "cpu": true,
    "memory": true,
    "network": true,
    "showOn": "Taskbar",
    "layout": "ValuesAndNames",
    "icons": "Color",
    "updateIntervalSeconds": 1
  }
}
```

- `showOn`: `Taskbar` or `NotificationArea`. `layout`: `ValuesAndNames`, `LargeValues` or `Compact`. `icons`: `Color`, `OneColor` or `None`.

## Not included

- A panel with details or graphs when the meters are selected.
- What each app uses.
- GPU, disk, temperature, battery and fan meters.
- Choosing adapters or processors, other colors, and alerts.
- Choosing which page Task Manager opens on.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11, on x64 and ARM64.
- The strip runs on Windows 11 builds 22621 to the newest tested one, and on Windows 10 versions 2004 to 22H2, where it isn't tested yet; elsewhere the meters show as notification icons. See [When the strip can't show](#when-the-strip-cant-show).
- Every API is documented and in every supported version: processor utility since Windows 8, `SetCoalescableTimer` since Windows 8, `GetSystemMetricsForDpi` since Windows 10 version 1607, and the others earlier. What the strip relies on about the taskbar's layout isn't documented, so [Windows support](../../compatibility/windows-support.md) lists it with the tested builds.

## Security and privacy

- **Reads:** system-wide figures only: how busy the processors are, the physical memory in use, and the bytes the network adapters received and sent. Nothing about other processes: no process list and no per-process counters. `GetIfTable2` also returns the adapters' names and hardware addresses; they're never kept or logged.
- **The taskbar:** UI Automation reads only the position, automation ID and class name of the taskbar's buttons, never their names, which hold window titles such as a document's or a web page's. The strip reads no other window, and loads nothing into Explorer: its event hooks run in the host (`WINEVENT_OUTOFCONTEXT`).
- **Network:** makes no connections; reading the counters sends nothing. `PRIVACY.md` needs no change.
- **Stores:** nothing but its settings. Readings stay in memory, only the last one for each meter.
- **Logs:** Debug entries when the meters start and stop, which meters show, where and how often they update, why the strip moved to the notification area or back, pauses and resumes, each icon Windows took again after a taskbar restart, the command chosen in the meters' menu, and Task Manager opened. Warnings when Windows refuses an icon or doesn't open Task Manager, when the CPU meter falls back to `GetSystemTimes`, when the settings section can't be read or has a choice this version doesn't know, and when the meters don't stop within 2 seconds as the host exits; an error when a failure stops the feature. Never the values, adapter names or addresses, or anything about the taskbar's buttons beyond their number.
- **Changes:** adds the strip or up to three icons, and starts only Task Manager, from a fixed path without arguments. The settings hold only switches, choices from fixed lists and a number, so a changed settings file can't make PC Companion do anything else, also while it runs as administrator.
- **Rights:** runs without admin rights; every API works for standard users. While PC Companion runs as administrator, Task Manager starts with admin rights, as it does for administrators anyway.
- **Tooltips:** they show system-wide figures that any program running as the user can read itself, so they disclose nothing new.
- **Failures:** everything on the meters thread runs inside the feature's error boundary. A failure removes the strip and the icons and stops the feature until the host restarts, and the other features keep running. The PDH query, the adapter table, the hooks and every icon are released after use, so a long session leaks no handles.

## Performance

- No timer runs while the feature is off, while every meter is off, while the PC is locked or while the display is off.
- The meters add one thread while the feature is on, which sleeps between updates. The host's UI thread does only `Apply`'s part, so the meters add nothing to the time other features wait.
- An update makes one PDH reading, one `GlobalMemoryStatusEx` call and one `GetIfTable2` call, each only for a meter that shows, then draws only what changed: on the strip, the text whose value changed; in the notification area, the icons that changed. Memory changes slowly, so its icon is seldom sent.
- The strip looks for room only on the events above and every 30 seconds, never while the meters are paused, each time for 30 to 70 ms. Checking its place above the taskbar takes about 10 µs.
- Each icon sent makes Explorer draw it again, which adds to Explorer's CPU use rather than the host's.

## Accessibility and localization

- **Notification area:** Windows + B moves the keyboard focus there, where Enter or Space on a meter opens Task Manager and Shift + F10 opens its menu. Screen readers read each icon's tooltip.
- **Strip:** it isn't part of the taskbar's keyboard navigation. Its window's name holds the meters' names and values, for screen readers that look for it. **Show on** says that the notification area is the choice for reaching the meters with the keyboard.
- No meaning depends on color, and in high contrast the strip and the icons use the theme's colors.
- The page works with the keyboard alone, and its switches and lists have labels for screen readers.
- The strings are in `Strings.resx` and translated like the others, marked "Needs native review." The tooltips, the strip's names and the menu use the host's language.

| Key | English | Where |
|---|---|---|
| `ModuleDesktopName` | Desktop | Navigation pane and Home: the group of features for the desktop and the taskbar |
| `SystemMetersName` | System Meters | Feature name |
| `SystemMetersDescription` | See how busy your PC is, with live CPU, memory and network use on the taskbar. | Page and Home card |
| `SystemMetersMetersHeader` | Meters | Page: the group of switches |
| `SystemMetersMetersDescription` | Choose what to show. | Below `SystemMetersMetersHeader` |
| `SystemMetersCpuHeader` | CPU | Page: switch; also the CPU meter's name on the strip and its notification icon's first tooltip |
| `SystemMetersCpuDescription` | How busy the processor is, in percent, as in Task Manager. | Below `SystemMetersCpuHeader` |
| `SystemMetersMemoryHeader` | Memory | Page: switch; also the memory meter's name on the strip and its notification icon's first tooltip |
| `SystemMetersMemoryDescription` | How much of the memory is in use, in percent. | Below `SystemMetersMemoryHeader` |
| `SystemMetersNetworkHeader` | Network | Page: switch; also the network icon's first tooltip |
| `SystemMetersNetworkDescription` | How fast the PC receives and sends data. | Below `SystemMetersNetworkHeader` |
| `SystemMetersShowOnHeader` | Show on | Page: setting name, and the screen reader name of its list |
| `SystemMetersShowOnDescription` | On the taskbar, the meters share one item. In the notification area, each has its own icon, which you can reach with the keyboard: press Windows logo key + B. | Below `SystemMetersShowOnHeader` |
| `SystemMetersShowOnTaskbar` | Taskbar | Page: list item |
| `SystemMetersShowOnNotificationArea` | Notification area | Page: list item |
| `SystemMetersLayoutHeader` | Layout | Page: setting name, and the screen reader name of its list |
| `SystemMetersLayoutDescription` | How CPU and memory show on the taskbar. | Below `SystemMetersLayoutHeader` |
| `SystemMetersLayoutValuesAndNames` | Values and names | Page: list item |
| `SystemMetersLayoutLargeValues` | Large values | Page: list item |
| `SystemMetersLayoutCompact` | Compact | Page: list item |
| `SystemMetersIconsHeader` | Icons | Page: setting name, and the screen reader name of its list |
| `SystemMetersIconsColor` | Color | Page: list item |
| `SystemMetersIconsOneColor` | One color | Page: list item |
| `SystemMetersIconsNone` | None | Page: list item |
| `SystemMetersIntervalHeader` | Update every | Page: setting name, and the screen reader name of its list |
| `SystemMetersIntervalSeconds_one`, `_other` | {0} second, {0} seconds | Page: the list's items, with the plural forms of each language |
| `SystemMetersTaskManagerInfo` | Selecting the meters opens Task Manager on its default start page. To open it on Performance, choose Performance in Task Manager's settings. | Page: note |
| `SystemMetersHiddenIconsInfo` | Windows may put new icons in the hidden icons menu (^) of the taskbar. To keep a meter in sight, drag its icon onto the taskbar, or turn it on in the taskbar settings. | Page: note, with Notification area |
| `SystemMetersOpenTaskbarSettings` | Open taskbar settings | Page: button in the note |
| `SystemMetersPermissions` | Reads how busy the processor is, how much memory is in use and how much data the network adapters receive and send, for the whole PC; never what each app uses. On the taskbar, reads only where its buttons are, never their names. Makes no network connections, and stores and sends nothing. | Below `FeaturePermissionsHeader` |
| `SystemMetersCpuToolTip` | CPU: {0} | Tooltip line; {0} is a percentage, such as 42% |
| `SystemMetersMemoryToolTip` | Memory: {0} | Tooltip line; {0} is a percentage |
| `SystemMetersMemoryInUse` | {0} of {1} in use | Tooltip line; {0} and {1} are sizes from `SystemMetersGigabytes` |
| `SystemMetersGigabytes` | {0} GB | A size; {0} is a number with one decimal |
| `SystemMetersNetworkToolTip` | Network | Tooltip line |
| `SystemMetersReceive` | Receive: {0} | Tooltip line; {0} is a rate |
| `SystemMetersSend` | Send: {0} | Tooltip line; {0} is a rate |
| `SystemMetersKilobitsPerSecond` | {0} Kbps | A rate, on the strip and in tooltips |
| `SystemMetersMegabitsPerSecond` | {0} Mbps | A rate |
| `SystemMetersGigabitsPerSecond` | {0} Gbps | A rate |
| `SystemMetersTaskManager` | Task Manager | The meters' menu; Settings and Exit use `TraySettings` and `TrayExit` |

## Testing

- **Unit tests** with fake readings and a fake clock:
  - CPU: utility capped at 100, and the fallback to `GetSystemTimes` when the counter can't be added or read: the busy share, no time passed, and times that go back;
  - memory: the percentage and the sizes, and a total of zero;
  - network: which adapters count (hardware, filter, loopback, down), rates over the measured time, late updates, and adapters that appear, disappear or reset;
  - formatting: percentages, sizes, the rate units and decimals in English, German, French and Arabic, and every language's tooltips within 127 characters.
- **Unit tests for the strip,** with fake taskbar buttons and windows:
  - finding room: left-aligned and centered taskbars, the Widgets entry on either side or off, a mirrored taskbar, and too little room;
  - the layouts and icon styles: their column widths at 96 to 288 DPI, fixed places for the widest values, and a meter turned off leaving out its column;
  - drawing: text and icon colors in the dark and light modes and in high contrast, icons and values centered by their visible pixels, and the near-transparent background;
  - staying in sight: raising when the taskbar is above, hiding while the taskbar isn't topmost and coming back after, and nothing to do otherwise;
  - moving to the notification area and back: an untested build, Windows 10, an auto-hiding taskbar, no room, no taskbar, and **Show on**.
- **Unit tests for the notification icons:** the size at 96 to 288 DPI; white, black and the high contrast color on a transparent background; numbers that fit and are centered, for 0, 7, 42 and 100; the network slots at 0, 1 Kbps, 1 Mbps, 1 Gbps and more; and no growth in GDI handles over 1,000 icons.
- **Unit tests for the feature,** with a fake taskbar, timer, session and display: only the chosen meters, their first values one update after starting, turning meters and the feature on and off, a new interval and a new layout, sending only icons that changed, showing an icon again after a failed update, pausing and resuming, selecting opening Task Manager, the menu's commands, settings that can't be read, unknown choices, an interval out of range, and a failure that removes the strip and the icons and stops the feature.
- **Unit tests for the meters thread:** it starts and takes new settings without `Apply` waiting, Settings and Exit reach the host's UI thread, stopping removes the strip and the icons and ends it, turning the feature on again before it ended starts a new thread only after the old one, the host's exit waits for it at most 2 seconds, an icon Windows took again after a taskbar restart is logged, the strip doesn't look for room while the meters are paused and looks once when they resume, and a failure on it, also in the strip's own handlers, stops the feature and leaves the host running.
- **Sandbox checks** (`scripts/sandbox/groups/SystemMeters.ps1`):
  - the Home switch turns the feature on, and the page shows its settings with every meter on, Taskbar, Values and names, Color and **Update every** at 1 second; with Notification area, Layout and Icons are off and the note about hidden icons shows;
  - the strip appears in the taskbar's free space, found with `EnumWindows`, on a centered and a left-aligned taskbar, covering none of its buttons; it shows its tooltip, names the meters for screen readers, keeps its width as the values change, and each layout and icon style changes its width as expected;
  - clicking the strip starts Task Manager, and right-clicking it shows the meters' menu; the icons' menu is dark in the dark Windows mode, and **Settings** opens the settings app;
  - the strip's text follows the light and dark modes, and the strip comes back after Explorer restarts and after a click on the taskbar;
  - while the meters are paused, turning on the taskbar's auto-hide changes nothing, and once they resume, the meters move to notification icons at once; the Sandbox can't lock its session, so the check sends the meters' window the session's lock and unlock reports;
  - with an auto-hiding taskbar, or with **Show on** set to Notification area, the meters show as notification icons, found with UI Automation in the hidden icons menu by their names, which are their tooltips; **Open taskbar settings** opens Settings; selecting an icon starts Task Manager, and restarting Explorer brings the icons back, which the host logs for each;
  - busy processes on every logical processor raise CPU above 80%, and it falls once they stop; allocating 1 GB raises memory by about its share;
  - with networking on, pings to the Sandbox's gateway show in send, and requests to Windows' connectivity check address in receive;
  - turning a meter off removes it, and turning it on again brings it back in its place on the taskbar; turning the feature off removes the strip and the icons.
- **Sandbox networking:** this group needs networking, for real adapter counts, and says so with a `# Networking: on` line at its top. When a run includes such a group, `sandbox-test.ps1` turns networking on in the Sandbox's configuration, and `run-checks.ps1` turns every network adapter off before Smoke and on only during such groups. While they're on, the hosts file points `api.github.com` at 127.0.0.1 and the group's settings turn the update check off, so the real GitHub gets nothing. Runs without such a group keep networking off.
- The App group checks the page's layout at each window width, and the Languages group checks it in Arabic, as for the other feature pages.
- **By hand:**
  - Windows 11 versions 22H2, 23H2 and 24H2, Windows 10, and an ARM64 PC;
  - display scales from 100% to 200%, a scale change while the meters run, several displays, and the taskbar's alignment, size and Widgets entry changing;
  - clicks on the taskbar, Start, Search and the taskbar's flyouts, a full-screen game and video, and an auto-hiding taskbar;
  - a right-to-left Windows display language, the taskbar in an accent color, and high contrast;
  - UAC on, as an administrator and as a standard user, for starting Task Manager;
  - Ethernet, Wi-Fi and a VPN, which mustn't count twice;
  - sleep and resume, and Explorer's and the host's CPU use over an hour with the strip and with the icons;
  - switching to another user and back, and disconnecting and reconnecting over Remote Desktop, where the meters mustn't resume while the lock screen still shows;
  - Narrator with the strip and with Windows + B on the icons;
  - bringing the icons out of the hidden icons menu, then checking that they keep their place after a restart and an update.
