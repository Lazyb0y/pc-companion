# Dynamic Wallpaper

Change the desktop's picture with the time of day: at sunrise and sunset, at times the user chooses, or with the Windows mode, light or dark. Each display can have pictures of its own.

- Module: Desktop (`PCCompanion.Modules.Desktop`). Settings section: `features.dynamicWallpaper`. Issue: [#281](https://github.com/Lazyb0y/pc-companion/issues/281).
- Inspired by the dynamic desktop of macOS, GNOME's timed and light-and-dark wallpapers, and KDE Plasma's day and night wallpapers.
- Off until the user turns it on.

## Behavior

- **Change with** chooses when the picture changes: **Sunrise and sunset**, the default, **Times of day**, or **Windows mode**. Each splits the day into periods, and each period has a picture, or a folder of pictures.
- When a period starts, every display gets its picture: the same on every display, the default, or one for each display; see [Displays](#displays).
- PC Companion sets the pictures through Windows' own wallpaper function, the one Windows Settings uses, so Windows shows, fits and keeps them as it does the user's own. It never draws on the desktop, and the host never opens a picture: Windows reads it.
- When someone else changes the background, in Windows Settings or with another app, Dynamic Wallpaper steps aside until the user resumes it. Turning it off puts back the background the user had; see [Working alongside Windows](#working-alongside-windows).
- Windows can choose its accent color from the background (**Personalization > Colors > Accent color: Automatic**). Then each new picture may change the accent color, as it does for a slideshow.
- Nothing is added to the PC Companion icon's menu.

## Compared with other systems

- **macOS** picks a dynamic desktop's pictures by the sun's position, from Location Services or, without them, the time zone, and its light and dark wallpapers follow the appearance. Users report pictures that stop changing after sleep, jumps instead of fades since macOS Sonoma, and wallpapers that follow the time only when the appearance is set to change on its own. Dynamic Wallpaper follows the sun, the clock or the Windows mode, each on its own, with the user's own pictures, and checks again after sleep and after clock and time zone changes.
- **GNOME** changes timed wallpapers from XML files written by hand, with no settings page, and shows one picture on every display; users report changes minutes late or an hour early. Its dark wallpaper follows the style. Dynamic Wallpaper has a page, changes on time with a timer that Windows keeps across sleep and clock changes, and gives each display its own pictures.
- **KDE Plasma** gives each display its own wallpaper and, from Plasma 6.5, switches between day and night pictures by the sun's height when it knows the location, at set times otherwise. Users report a display's wallpaper settings lost when it's disconnected. Dynamic Wallpaper recognizes displays by model and serial number, so their pictures follow them to any port or dock.
- **Xfce** spreads a folder's pictures over the day, for each display and workspace. **ChromeOS** changes its own time-of-day wallpapers with the sun, from a location that Google looks up online, and uses 6:00 and 18:00 without one. Dynamic Wallpaper finds an approximate place from the time zone on the PC, with no network, and lets the user choose a place.
- **Windows** has a slideshow on a timer, Windows spotlight and themes, but nothing that changes the background with the sun, the clock or the Windows mode. It keeps a display's own picture for the connection the display is on, so the picture is lost when the display moves to another port, users report.
- **Wallpaper tools for Windows** that follow the sun look up typed place names with online services, or wait for Windows' location at every start, which users report slowed sign-in. Users also report pictures that don't change after sleep, changes that reach only one virtual desktop, and tools that fight Windows spotlight, slideshows and each other. Dynamic Wallpaper makes no network connections, asks for Windows' location only when the user selects **Use my location**, sets the pictures on each virtual desktop as the user switches to it, and steps aside when anything else changes the background.

## Change with

### Sunrise and sunset

- Four periods, by the sun's height above the horizon at the user's place:
  - **Night:** the sun more than 6° below the horizon;
  - **Sunrise:** the sun rising from 6° below to 6° above the horizon;
  - **Day:** the sun more than 6° above the horizon;
  - **Sunset:** the sun setting from 6° above to 6° below the horizon.
- Day and Night need a picture; Sunrise and Sunset may have none. Without a Sunrise picture, Night lasts until the sun rises and Day starts then, and without a Sunset picture, Day lasts until the sun sets. The sun rises and sets when the top of its disk meets the horizon, with the sun's center 0.833° below it.
- Near the poles, the same rules hold, with no special case. While the sun never sets, it's Day, or Sunset then Sunrise around midnight while the sun comes within 6° of the horizon. While it never rises, it's Night, or Sunrise then Sunset around noon. **Today** on the page says so, such as "The sun doesn't rise today."
- The sun's height is computed on the PC from the place and the time in UTC, with the equations of NOAA's solar calculator, which are in the public domain. They're accurate to about a minute between 72° north and south, and less accurate nearer the poles. The place comes from [Location](#location).
- Daylight saving time and a new time zone don't move the sun, so the periods stay where they are; only the times shown change.

### Times of day

- 2 to 12 times, each with a picture. A picture shows from its time until the next time, and the last one until the first time the next day.
- Times are hours and minutes on the PC's clock, in the time zone set in Windows, and two can't be the same.
- When the clock goes forward for daylight saving time, a time in the hour that's skipped takes effect when the clock jumps. When it goes back, a time in the hour that repeats takes effect the first time.
- After a change of time zone, the times follow the new clock.
- When first chosen, the times are 7:00 with the Day picture and 19:00 with the Night picture.

### Windows mode

- Two periods: **Light** and **Dark**, which follow the Windows mode, the one Start and the taskbar use (**Personalization > Colors > Choose your mode**, or **Windows mode** with **Custom**), whatever changes it.
- Windows changes the mode only when the user or another app does. The page says so.
- When first chosen, Light has the Day picture and Dark the Night picture.

## Location

Sunrise and sunset need a place. **Location** offers two:

- **From your time zone,** the default, needs no permission and makes no connection:
  - PC Companion takes the time zone set in Windows and **Country or region** (`GetUserDefaultGeoName`), finds the time zone database's zone for them (`TimeZoneInfo.TryConvertWindowsIdToIanaId`), and takes that zone's main city from a table that ships with PC Companion. The table comes from the time zone database, which is in the public domain: each zone's main city, from `zone.tab`, or `zone1970.tab` for a zone `zone.tab` doesn't list, with the older names that `tzdata.zi` links to them, such as `Asia/Calcutta`, which Windows uses. `scripts/time-zone-places.ps1` makes it. It adds about 17 KB.
  - The page shows the time zone's name, such as "(UTC+06:00) Dhaka", and that the times are approximate: far from the zone's main city they can be off by tens of minutes, as in a time zone as wide as China's.
  - When Windows' time zone changes, as on a trip with **Set time zone automatically**, the place follows it.
  - A time zone with no place, such as UTC, gives 6:00 for sunrise and 18:00 for sunset, and the page asks for a place.
- **Coordinates:** a latitude and a longitude, in degrees with one decimal, north and east positive, which the user types or fills in with **Use my location**. One decimal is about 11 km, which moves sunrise and sunset by less than a minute, so no more is kept. Choosing From your time zone removes them from the settings; the page keeps them while it's open, for choosing Coordinates again.
- **Use my location** asks Windows for the PC's position once, from the settings app, which Windows requires to be in front for its question (`Geolocator`):
  - Windows 11 version 24H2 and later ask the user the first time a desktop app wants the location, and **Let desktop apps access your location** must be on. When Windows refuses, the page says why, with **Open location settings** (`ms-settings:privacy-location`).
  - Windows' location service may use nearby Wi-Fi networks and Microsoft's online service to find the position. PC Companion itself makes no connection.
  - Windows has up to 30 seconds to answer, and may give a position up to 10 minutes old. The position is rounded to one decimal and saved as the coordinates, and Coordinates is chosen. PC Companion doesn't ask again unless the user selects the button again. A failure is logged with its error code only.
- The host never asks for the location: Windows can take seconds to answer, and its question needs a window in front.
- Only the rounded coordinates are kept, in the settings. The place found from the time zone isn't stored, and no location is logged.

## Pictures

- Each period has a **picture,** which shows every time the period starts, or a **folder**. A folder shows its next picture each time the period starts, in a shuffled order, every picture once before any repeats; a new order starts when the pictures in the folder change. Subfolders aren't included, and the first 10,000 pictures by name count.
- **Formats:** JPEG (`.jpg`, `.jpeg`, `.jfif`), PNG, BMP (`.bmp`, `.dib`) and TIFF (`.tif`, `.tiff`), which Windows shows as a background on every supported version. Windows keeps PNG pictures as PNG, and saves JPEG pictures again at its own quality, as for any background.
- **Where:** pictures on the PC's drives. A picture on a network location, or one that a cloud storage app keeps online only, isn't used, since Windows would wait for the network to read it; the page says so on its period.
- **Checks:** just before setting a picture, the host checks that it's still there, its extension and that it's on the PC. A picture that's gone, or a folder with none, leaves the current picture in place, and the page shows the problem on its period. Windows refuses a picture it can't read, which also leaves the current picture in place.
- **Fit:** **Fill**, **Fit**, **Stretch**, **Tile**, **Center** or **Span**, as in Windows Settings. Windows has one fit for every display (`IDesktopWallpaper::SetPosition`). **Span** shows one picture across all the displays, so **A picture for each display** is off with it.
- **First settings:** on Windows 11, Day and Light get Windows' own light picture and Night and Dark its dark one, the pictures of Windows' **Windows (light)** and **Windows (dark)** themes (`%WINDIR%\Web\Wallpaper\Windows\img0.jpg` and `img19.jpg`), so the feature works at once. On Windows 10, which has no dark picture, Day and Night get the current picture, or `img0.jpg` without one, and the page asks the user to choose. The fit is the one Windows uses.
- **No pictures ship** with PC Companion, so the installer stays the same size.

## Displays

- **Same picture on every display**, the default, sets each period's picture for all displays at once (`IDesktopWallpaper::SetWallpaper` without a display), which Windows also gives displays that are connected later.
- **A picture for each display** gives each display a group on the page, named from its EDID, the laptop's own as **Built-in display**, and numbered when identical displays have the same name, with a row for each period: a picture, a folder, or **Same as other displays**. The periods' own pictures are then for **Other displays**, any display without its own. While it's on, the page adds each connected display the settings don't have yet, up to 16. A display that's not connected stays, marked **Not connected**, with **Remove**, which removes its pictures too.
- Displays are recognized as [Display Profiles](../display/display-profiles.md#recognizing-displays) recognizes them: by model and serial number on any connection, then by model and connection, then by model alone when only one display of that model is connected. Only a hash of the serial number is kept. So a display keeps its pictures on another port or dock, which Windows' own pictures for each display don't.
- Each display's picture is set by its device path, which Windows' wallpaper function uses as the display's ID, as the spike confirmed. Windows also lists displays that were disconnected with a picture; PC Companion sets only the connected ones.
- When a display arrives or leaves (`WM_DISPLAYCHANGE` and `GUID_DEVINTERFACE_MONITOR`), PC Companion reads each connected display's picture and sets those that differ.

## When pictures change

- The host works out the next change and sets one timer for it, a thread pool timer with its time in UTC (`SetThreadpoolTimerEx`). A timer like that fires as soon as the PC wakes when its time passed during sleep, and fires at once when the clock is set past it, but not early when the clock is set back. It looks up to two days ahead; with no change in that time, as in a polar night, it looks again then.
- It works out the next change again when the clock or the time zone changes (`WM_TIMECHANGE`, after which .NET's time zone data is read again), when the PC wakes (`PBT_APMRESUMEAUTOMATIC`), when the settings change, and for **Windows mode**, when the mode changes (`WM_SETTINGCHANGE` with `ImmersiveColorSet`).
- At each change, and when the feature starts, the host reads the pictures Windows shows and sets only those that differ, so signing in or restarting the host changes nothing that's already right. Setting the same picture again would cost Explorer about 0.1 seconds.
- Changes happen on time while the PC is locked or the display is off, so the right picture shows when the user is back.
- Windows shows a change 300 to 700 ms after the call returns. It fades to the new picture where Windows animates; Windows Sandbox showed no fade, so this is tested by hand.
- In a Remote Desktop session, Windows can turn the background off. While it does (`DSS_DISABLED_BY_REMOTE_SESSION`), nothing is set.

## Working alongside Windows

### Turning on

- Before its first change, the host remembers the background Windows has, in `dynamic-wallpaper.json` in the data folder, which only the host writes: whether it's a picture, a solid color, a slideshow or Windows spotlight, each display's picture, the fit, the background color, and the slideshow's folder and options.
- Windows has no documented way to tell that Windows spotlight is on. The host reads Windows' own spotlight setting (`DesktopSpotlight\Settings\EnabledState` under the user's key) only to remember it, and only on the tested builds; see [Compatibility](#compatibility).

### While it's on

- **Someone else changes the background:** in Windows Settings, with a theme, with **Set as desktop background**, by turning on Windows spotlight or a slideshow, with another app, or through settings sync.
  - Windows' wallpaper function announces nothing, so the host watches the keys that every change writes, `Control Panel\Desktop` and `Control Panel\Colors` under the user's key (`RegistryWatch`). On a change, it reads the pictures and the background's state from Windows and compares them with what it set last.
  - When they differ, Dynamic Wallpaper **steps aside:** it stops changing the background and remembers the new one as the one to put back. The page says "You chose another background in Windows, so Dynamic Wallpaper stopped changing it", with **Resume**.
  - **Resume** remembers the background Windows has then, as when turning on, and sets the period's pictures.
  - Stepping aside lasts until **Resume**, or until the feature is turned off and on again, also across restarts. It covers every display.
- **Virtual desktops:** Windows 11 keeps a picture for each virtual desktop, and the wallpaper function changes the one in use; a solid color and a slideshow apply to all of them. So when the user switches desktops, the host sets the period's pictures on that desktop, where they differ, and Windows shows the desktop's old picture until then, about half a second.
  - A switch also writes the keys above, so the host tells a switch from someone else's change by Windows' record of the desktop in use (`CurrentVirtualDesktop` under `Explorer\VirtualDesktops` in the user's key), which it watches and reads only on the tested builds.
  - Desktops the user doesn't switch to keep their old picture until they're used. Windows 10 has one background for all desktops.
- **Explorer restarts:** the background stays, and the host connects to the wallpaper function again when Explorer's taskbar is back (`TaskbarCreated`), then sets what's missing.

### Turning off

- If Dynamic Wallpaper didn't step aside, it puts back the background it remembered: each display's picture and the fit, the solid color, or the slideshow with its folder and options. A remembered picture that's gone gives the background color.
- Windows spotlight can't be turned on again with a documented function, so the host puts back the last picture it showed, and the page says "To use Windows spotlight again, choose it in Windows' background settings", with **Open background settings** (`ms-settings:personalization-background`).
- If it stepped aside, nothing changes: the user's own choice stays.
- Then it deletes `dynamic-wallpaper.json`.
- Exiting PC Companion, or signing out, leaves the background as it is. Uninstalling leaves the last picture, as Windows keeps its own copy of it.

### When Windows doesn't allow it

- **Policies:** while the background is managed (`NoChangingWallPaper` under `Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop`, or `Wallpaper` under `Software\Microsoft\Windows\CurrentVersion\Policies\System`, in the user's or the machine's key, or `Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP` in the machine's key), PC Companion changes nothing, and the page says "Your organization manages the desktop background." It reads them before each change, and Windows' announcement of a policy change (`WM_SETTINGCHANGE` with `Policy`) starts one. Windows refusing a change (`S_FALSE`), or showing another picture than the one set, counts the same, with a warning in the log.
- **Windows that isn't activated** locks the background page in Windows Settings, but not the wallpaper function, as File Explorer's **Set as desktop background** shows. Dynamic Wallpaper works there too.

## The settings app and the host

- The page computes the periods itself, with the same code as the host, for **Today**.
- It needs what only the host knows: whether the feature stepped aside, a policy manages the background, a picture is missing or Windows didn't answer, and the period in use. The host gives it through a named pipe that only the user's own account and the system can open, and that rejects remote clients, as Display Profiles' pipe does. Its one other request is **Resume**.

## Threads

- **The host's UI thread** runs `Apply`, which reads and checks the section, and its hidden window's messages: `WM_TIMECHANGE`, `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, displays that arrive and leave, waking, `TaskbarCreated` and the session's unlock. These only pass work to the wallpaper thread, and set the timer for the next change once the wallpaper thread has worked it out.
- **The wallpaper thread,** an MTA thread of its own, does everything that waits for Explorer, the registry or the disk: the time zone, its main city, which loads Windows' time zone data the first time, and the Windows mode, and working out the next change from them, in well under a millisecond; Windows' wallpaper function (`IDesktopWallpaper`), reading and setting pictures, the fit, the color and the slideshow, reading the displays and their EDIDs, checking pictures and listing folders, the policy values, and `dynamic-wallpaper.json`. It does one job at a time, and a new job replaces any that's waiting, since only the latest matters.
- **Explorer:** every call to the wallpaper function goes to Explorer and waits for it, without a time limit.
  - The wallpaper thread allows its calls to be cancelled (`CoEnableCallCancellation`). When a call takes more than 10 seconds, a thread pool timer cancels it (`CoCancelCall`), and a warning is logged. Releasing the connection is a call to Explorer too, so the thread keeps it until Explorer answers a new connection, and releases it then. The thread tries again when Explorer's taskbar comes back or at the next change.
  - While Explorer isn't running, calls fail at once, and the thread waits for `TaskbarCreated`.
- **The thread pool** runs the timer and the registry watches, which pass their news to the UI thread, and the pipe, which asks the wallpaper thread.
- **The settings app** shows the Open dialog, reads Windows' thumbnails of the pictures on its thread pool, and asks for the location on its UI thread.
- **Stopping:** turning the feature off cancels the timer and the watches, closes the pipe, and has the wallpaper thread put back the background, then end. As the host exits, it puts back nothing, and waits up to 2 seconds for the thread.
- **Failures:** the wallpaper thread, the window's handlers and the pipe run inside the feature's error boundary. A failure there is logged and keeps the feature off until the host restarts, like a failing `Apply`. Windows refusing a change isn't such a failure.

## Settings

- **Feature page,** from the top:
  - the on/off switch, and notes when the feature stepped aside, with **Resume**, when a policy manages the background, when Explorer didn't answer, in a remote session that shows no background, and after turning off when Windows spotlight was on before, with **Open background settings**;
  - **Change with:** Sunrise and sunset, Times of day or Windows mode, with a note for Windows mode;
  - **Pictures:** a row for each period, with its picture's thumbnail, its file or folder name, why it can't be used, **In use** on the current period, and **Choose** with **Picture** and **Folder**; Sunrise and Sunset also have **None**. With Times of day, each row has its time and **Remove**, and the group has **Add time**. A note says when every period has the same picture, as Windows 10's first settings do;
  - **A picture for each display**, a switch, off with Span, and with it a group for each display; see [Displays](#displays);
  - **Fit**;
  - for Sunrise and sunset, **Location**: From your time zone, with the time zone's name, or Coordinates, with the latitude, the longitude and **Use my location**; and notes when Windows refuses the location, with **Open location settings**, can't find it, or there's no place;
  - **Today**, except with Windows mode: when each period starts and ends today, and that the sun doesn't rise or set;
  - a **Permissions & data** card.
- **First settings** (`FirstSettings`): Sunrise and sunset, From your time zone, the same picture on every display, the pictures and fit above, and the times 7:00 and 19:00.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Dynamic Wallpaper off with a warning in the log. An unknown `changeWith`, `location` or `fit` uses its default, with a warning. A period without a usable picture or folder, a time that's repeated or out of range, and coordinates out of range are shown as problems on the page and aren't used.

```json
"features": {
  "dynamicWallpaper": {
    "enabled": true,
    "changeWith": "Sun",
    "location": "Coordinates",
    "latitude": 23.8,
    "longitude": 90.4,
    "fit": "Fill",
    "eachDisplay": true,
    "displays": [
      {
        "model": "DELF11B",
        "serial": "5f1c0e9a2b7d4c61",
        "connection": "\\\\?\\DISPLAY#DELF11B#5&2a1f3e84&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
        "builtIn": false,
        "name": "DELL E2420H"
      }
    ],
    "sun": {
      "sunrise": null,
      "day": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
      "sunset": { "picture": "C:\\Users\\Ana\\Pictures\\Wallpapers\\Dusk.jpg" },
      "night": {
        "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg",
        "displays": [{ "folder": "C:\\Users\\Ana\\Pictures\\Night" }]
      }
    },
    "times": [
      { "at": "07:00", "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
      { "at": "19:00", "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg" }
    ],
    "windowsMode": {
      "light": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
      "dark": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg" }
    }
  }
}
```

- `changeWith`: `Sun`, `Times` or `WindowsMode`. `location`: `TimeZone` or `Coordinates`; `latitude` and `longitude` are there only with `Coordinates`, from -90 to 90 and -180 to 180, with one decimal. `fit`: `Fill`, `Fit`, `Stretch`, `Tile`, `Center` or `Span`.
- A period has a `picture` or a `folder`, both full paths on a drive letter, for every display or, with `eachDisplay`, for **Other displays**. Its `displays`, used only with `eachDisplay`, has an entry for each display in the top-level `displays`, in the same order: a `picture`, a `folder`, or `null` for the period's own. A time in `times` is a period with an `at`, from `00:00` to `23:59`.
- `displays` recognizes and names each display as in Display Profiles: `model` is the maker's and product code from the EDID, `serial` a hash of the serial number, empty without one, `connection` the device path when it was added, `builtIn` whether it's a laptop's own screen, and `name` the EDID's name, empty without one.

## Not included

- **The lock screen.** Windows has a function for it, which needs a new file name for each picture and competes with Windows spotlight on the lock screen; a later issue.
- **Pictures for each virtual desktop,** and setting desktops that aren't in use, which need undocumented interfaces that change with Windows builds.
- **macOS dynamic desktops** (multi-picture `.heic` files), and HEIC, WebP, AVIF and JPEG XR pictures: they need codecs that aren't on every PC, and Windows shows some of them only on recent builds; a later issue.
- **Blending** from one picture to the next over minutes: each step would make Explorer read and save a large picture again.
- **Pictures that ship** with PC Companion, and pictures drawn by it, such as sky colors.
- **A fit for each display:** Windows has one fit for all.
- **Changing the period's times** before or after sunrise and sunset, and other twilights.
- **Changing the Windows mode** itself, and following night light's schedule, which Windows doesn't let apps read.
- **Pictures from the internet,** and a slideshow within a period, which Windows' slideshow does.
- **Finding a place by name,** which needs a database of places, and updating **Coordinates** on its own on a trip; From your time zone follows the time zone.
- **A wallpaper for each display profile,** which belongs to profiles and context triggers ([#56](https://github.com/Lazyb0y/pc-companion/issues/56)).

## Compatibility

- Windows 10 version 2004 and later, and Windows 11, on x64 and ARM64.
- **Documented functions:** `IDesktopWallpaper` (Windows 8 and later), `CoEnableCallCancellation` and `CoCancelCall`, `SetThreadpoolTimerEx`, `GetUserDefaultGeoName` (Windows 10 version 1709 and later), `TimeZoneInfo.TryConvertWindowsIdToIanaId`, which uses the ICU library in Windows 10 version 1903 and later, `Geolocator`, `QueryDisplayConfig`, `DisplayMonitor`, `RegisterDeviceNotification` and `RegNotifyChangeKeyValue`.
- **Undocumented, read only:** Windows' records of the virtual desktop in use and of Windows spotlight aren't documented, so PC Companion reads them only on Windows 11 builds 22621 (version 22H2) to the newest tested one, now 26300. Build 26100 is tested; 26300, 22H2 and 23H2 are tested by hand. On other builds, the feature works without setting pictures as the user switches desktops, and without remembering Windows spotlight. They're listed in [Windows support](../../compatibility/windows-support.md) with the tested builds.
- Windows 10 has one background for all virtual desktops.

## Security and privacy

- **Changes:** the desktop background and its fit, and on turning off, the background the user had: a picture, a solid color, a slideshow, or Windows spotlight's last picture. Nothing else: not the theme, the Windows mode, the accent color, the lock screen or Windows' location settings.
- **Pictures:** the host never opens or reads a picture. It checks only that it's there, its extension, size and attributes, and that it's on the PC, and lists folders by name. Windows reads the picture, as when the user chooses it in Windows Settings. The settings app shows Windows' own thumbnails, the ones File Explorer shows, and doesn't read the pictures either.
- **Reads:** the pictures' and folders' paths, Windows' background settings and the pictures it shows, the connected displays, with their names, models, connections and serial numbers when **A picture for each display** is on, the time zone and **Country or region**, the policy values above, and with **Use my location**, one position from Windows.
- **Location:** only the coordinates rounded to one decimal, about 11 km, which the user chose or asked for, are kept, in `settings.json`. They're never logged or sent. The place found from the time zone isn't stored. The host never asks Windows for the location.
- **Serial numbers** are kept only as a hash, as in Display Profiles.
- **Stores:** `dynamic-wallpaper.json` in the data folder holds the remembered background, with its picture paths, the pictures set last, whether the feature stepped aside, and the order of each folder's pictures. It's deleted when the feature is turned off, and with the data folder when PC Companion is uninstalled.
- **Untrusted values:** a hand-edited settings file can hold anything, so every path must be a full path on a drive letter, with no device or network path and no control characters, with one of the extensions above, and times, coordinates and limits are checked. A changed file can only choose which of the user's pictures Windows shows, which any program running as the user can do itself.
- **Rights:** runs without admin rights. While PC Companion runs as administrator, Explorer, which runs without them, still reads the pictures.
- **The pipe:** only the user's own account and the system can open it, it rejects remote clients, and it answers only with the feature's state, or resumes it.
- **Network:** none. Windows' location service may use the network when the user selects **Use my location**. `PRIVACY.md` gets a line on Dynamic Wallpaper.
- **Logs:** Debug entries when the settings are applied, with the kind of schedule and how many periods and displays, when a period starts, by its name, when the feature steps aside, resumes or puts the background back, and when a display arrives or leaves, by number. Warnings when Windows refuses a change, a policy manages the background, a picture is missing, by its period, Explorer doesn't answer within 10 seconds, with the error code, and the settings can't be read. Never the location, the coordinates, the time zone's place, sunrise and sunset times, picture or folder paths and names, display names or serial numbers.
- **Failures:** a change Windows refuses leaves the background as it was. A failure on the wallpaper thread stops the feature until the host restarts, and the other features keep running.
- **Third-party data:** the time zone table comes from the time zone database, which is in the public domain. `NOTICE.md` and `ThirdPartyNotices.txt` name it.

## Performance

- **Between changes nothing runs:** one timer, three registry watches and the hidden window's messages; no polling.
- **A change** costs the host a few milliseconds on the wallpaper thread, and Explorer more: in the spike, 30 to 200 ms of processor time for a 1920 x 1080 picture, about 0.2 seconds for a 3840 x 2160 JPEG, 0.6 seconds for a 7680 x 4320 PNG and 1 second for a 7680 x 4320 JPEG. Sunrise and sunset makes up to four changes a day.
- Reading the displays takes about 0.1 ms; with **A picture for each display**, each display's EDID is read once for each device path, about 80 ms the first time.
- Working out the next change took 0.02 ms for Dhaka and 0.2 ms in a polar night, which looks two days ahead, and Today's periods 0.2 to 0.3 ms, on the PC.

## Accessibility and localization

- The page works with the keyboard alone, and keyboard focus stays on a period or a time when the list changes. Each row's name says its period, its picture or folder name and whether it's in use, such as "Night, Stars.jpg, in use", and its buttons carry the period's name, such as "Choose for Night".
- **Thumbnails** are pictures, not controls, so screen readers skip them: the row's name says the same. They're never mirrored in right-to-left languages, have a border in the theme's colors in high contrast, and show a placeholder for a picture that's gone.
- The page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages. Paths and coordinates are shown left to right in every language.
- Times follow the user's regional settings, 12-hour or 24-hour, and so do the coordinates' numbers, such as 23,8 in German; north and east are positive, as the boxes' descriptions say.
- The time zone's name is Windows' own, in the user's language.
- The strings are translated like the other pages', marked "Needs native review."
- The feature's icon is drawn as [Icons](../../design/icons.md) describes, and shown to the maintainer before it's used.

## Spike results

A spike with a .NET 10 helper, in Windows Sandbox (Windows 11 24H2, build 26100, Enterprise, not activated, one display), and read-only on a PC with Windows 11 build 26300 and two displays, confirmed:

- **Display IDs:** the IDs that Windows' wallpaper function gives each display matched the device paths from `QueryDisplayConfig` exactly, on the PC and in the Sandbox.
- **Setting a picture:** the call returned in 1 to 28 ms, and the picture showed 300 to 700 ms later. Setting the same picture again changed nothing and cost Explorer about 0.1 seconds. Explorer used 30 to 200 ms of processor time for a 1920 x 1080 picture, 0.2 seconds for a 3840 x 2160 JPEG, 0.6 seconds for a 7680 x 4320 PNG and 1 second for a 7680 x 4320 JPEG. Windows kept PNG pictures as PNG in its copy and saved JPEG pictures again. After a picture was set for one display, asking for the picture of all displays gave `S_FALSE`, even with one display.
- **Problems:** a missing file was refused with `0x80070002`, and a broken JPEG with `0x80070057`, and the picture stayed. A picture deleted after it was set kept showing until Windows drew the background again, as for a new fit, and then the background color showed.
- **Fit:** each fit applied about 270 ms after the call.
- **Windows spotlight** was on in the Sandbox at first; setting a picture turned it off.
- **Slideshow and solid color:** setting a slideshow again with the folder and options read before brought it back. Turning the picture off (`Enable(FALSE)`) showed the solid color, and the next picture turned it on again.
- **Announcements:** the wallpaper function announced nothing. `SystemParametersInfo` announced `WM_SETTINGCHANGE` with `SPI_SETDESKWALLPAPER`, and **Set as desktop background** `WM_SETTINGCHANGE` with `WindowsThemeElement`. Every change, from any of them, wrote `Control Panel\Desktop`, and so did every switch of virtual desktop.
- **Explorer ended:** calls through the old connection failed at once with `0x800706BA`, and connecting again with `0x80040154` until Explorer was back, about 0.3 seconds later; `TaskbarCreated` came after about 1.6 seconds. The background stayed, and a new connection worked.
- **Explorer not responding** (suspended): every call waited, also connecting. `CoCancelCall` from another thread ended them at once with `RPC_E_CALL_CANCELED`. `SystemParametersInfo`, which announces its change, waited until Explorer resumed.
- **Policies:** with `NoChangingWallPaper`, the wallpaper function refused with `S_FALSE` and nothing changed, while `SystemParametersInfo` still changed the picture. With the `Wallpaper` policy, a change showed the policy's picture instead, and after Explorer restarted it was refused with `S_FALSE`. With `PersonalizationCSP`, a change showed its picture instead, and the wallpaper function reported that picture.
- **Not activated:** Windows Settings' background page said Windows must be activated first, and the wallpaper function worked.
- **Remote session:** the Sandbox is a remote session, and the background showed there, with no `DSS_DISABLED_BY_REMOTE_SESSION`.
- **Virtual desktops:** a new desktop started with the current picture. Setting a picture on one desktop left the other's picture as it was, and Windows recorded a picture for each desktop. The record of the desktop in use changed within milliseconds of each switch.
- **Time:** a new time zone brought one `WM_TIMECHANGE`. A waitable timer with its time in UTC fired at once when the clock was set past it, and at its time, not earlier, when the clock was set back. The Sandbox sets its clock back from the PC's within seconds.
- **Fade:** none, also with animation effects turned on in the Sandbox, which is a remote session; tested by hand on a PC.
- **Not checked yet,** all by hand: the fade; two or more displays, also with several virtual desktops; displays on other ports and docks; sleep and Modern Standby; a locked session; Windows' location question; Windows 10; and Always run as administrator with UAC on.

## Testing

- **Unit tests,** with a fake wallpaper system, fake displays, fake Windows messages, a fake clock and fake time zones:
  - **the sun:** sunrise, sunset and the periods against NOAA's values for places in both hemispheres, near the date line and above the polar circles, in polar day and polar night, and with and without Sunrise and Sunset pictures;
  - **times of day:** the period in use and the next change, around midnight, at daylight saving time's changes in both directions, and after a change of time zone;
  - **the next change:** after waking, a clock set forward and back, and two days without a change;
  - **location:** time zones with and without a region, older zone names, a zone with no place, and rounding;
  - **pictures:** formats, network and online-only files, missing pictures and empty folders, and a folder's order;
  - **displays:** recognition, Other displays, Span, and displays that arrive and leave;
  - **working alongside Windows:** remembering and putting back each kind of background, stepping aside, Resume, a switch of virtual desktop that isn't a change, policies, `S_FALSE` and another picture shown, and the remote session;
  - **Explorer:** calls that fail at once, a call cancelled after 10 seconds, and a new connection after `TaskbarCreated`;
  - **the settings:** first settings on Windows 11 and Windows 10, the spec's example, unknown values, limits and a section that can't be read;
  - **the pipe:** its answers and Resume.
- **Sandbox checks** (`scripts/sandbox/groups/DynamicWallpaper.ps1`), with pictures the group makes, reading the background through the wallpaper function:
  - the Home switch turns the feature on with Sunrise and sunset, From your time zone and Windows' light and dark pictures, remembers the Sandbox's background and sets the period's picture; the page marks the period **In use**, shows the Sandbox's time zone and **Today**, and with **A picture for each display** lists the Sandbox's display as connected;
  - with Times of day set a minute or two ahead, the picture changes on time; a new time zone, set with `tzutil`, moves the times, and the Sandbox's own brings them back;
  - with Windows mode, the pictures follow the mode; restarting Explorer keeps the picture, and the next change works; with Explorer suspended for 20 seconds, the call is cancelled with a warning, the wallpaper thread gives up while Explorer is still suspended, and the next change after it answers sets the picture; and no feature holds the host's UI thread, as every group checks;
  - a folder shows each of its pictures once, and `dynamic-wallpaper.json` keeps its place; a missing picture leaves the picture in place, and the page shows the problem; a display's own picture is set for the Sandbox's display;
  - turning the feature off on the page puts back the Sandbox's background, deletes `dynamic-wallpaper.json`, and puts back a solid color;
  - another program changing the picture through the wallpaper function makes the feature step aside, the page shows the note with **Resume**, and **Resume** sets the period's picture again; turned off then, the other program's picture stays;
  - with `NoChangingWallPaper`, nothing changes and the page shows the note; without it, the picture changes again;
  - the logs hold no picture names; the page has screenshots, and the App and Languages groups check its layout, also right to left, as for the other pages.
- **What the Sandbox can't check:** the fade, two or more displays, recognizing displays on other ports, virtual desktops, which need the Windows key, putting back a slideshow and Windows spotlight's last picture, sleep, a locked session, Windows' location question, Windows 10 and Narrator. The unit tests cover putting back a slideshow and Windows spotlight's picture.
- **By hand:**
  - the fade, with animation effects on and off;
  - two and three displays, each with its own pictures, a display moved to another port and dock, and a laptop's own screen;
  - several virtual desktops, with one display and with two;
  - turning the feature off after a slideshow and after Windows spotlight;
  - sleep and Modern Standby across a change, and a change while locked;
  - **Use my location** with location on and off, and Windows' question;
  - Times of day across a daylight saving time change, by setting the time zone and the clock;
  - Windows Settings, a theme and Windows spotlight changing the background while the feature is on;
  - Always run as administrator with UAC on;
  - Windows 10, Narrator, high contrast and a right-to-left language.
