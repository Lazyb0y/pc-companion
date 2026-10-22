# Network Profiles

Save proxy settings as named profiles, such as Office and Home, and switch between them from the PC Companion icon's menu.

- Module: Network (`PCCompanion.Modules.Network`). Settings section: `features.networkProfiles`. Issue: [#54](https://github.com/Lazyb0y/pc-companion/issues/54).
- Inspired by network locations in macOS, which switch a set of network settings from the Apple menu, and by NetworkManager and the proxy settings of KDE Plasma and GNOME.
- This first version switches the proxy settings, which need no admin rights. DNS and network adapter settings need the elevated helper ([#23](https://github.com/Lazyb0y/pc-companion/issues/23)) and come later, as more parts of a profile.
- Off until the user turns it on.

## Behavior

- The PC Companion icon's menu gets a **Network profile** submenu, above Settings and Exit, with the profiles in the order of the profile list. Choosing one applies its proxy settings to Windows.
- The profile whose settings Windows uses now has a check mark (a radio mark). When Windows' settings match none of the profiles, for example after a change in Windows Settings, no profile has it, and the submenu starts with a dimmed **Other proxy settings in use**.
- The submenu ends with **Network Profiles settings**, which opens the feature's page.
- The submenu shows only while the feature is on and has at least one profile.
- The page shows the same: **In use** on the matching profile, and a **Use** button on the others, available while the feature is on.
- Switching changes only Windows' proxy settings. PC Companion stores nothing about which profile is in use; it compares Windows' settings with the profiles each time they change.
- PC Companion never changes the proxy settings on its own: not when it starts, not when the feature is turned on or off, and not when another program changes them. Turning the feature off or uninstalling PC Companion leaves the settings as they are.

## Compared with other systems

- **Windows** has one set of proxy settings, which the user types again for each place. Profiles keep them and switch them in two clicks.
- **macOS network locations** switch every network setting at once, so a location made for a proxy also fixes the addresses and DNS, and they're set for each network service, so a proxy set for Wi-Fi doesn't apply on Ethernet. Profiles change only the proxy, for Ethernet and Wi-Fi alike, and the menu shows when the settings were changed elsewhere; a location stays checked after a manual change.
- **NetworkManager** ties the proxy to a connection, but only to a setup script or automatic detection, and only programs that ask its proxy service follow it. Profiles also hold a proxy server, and Windows' settings reach the browsers and most programs.
- **GNOME and KDE Plasma** have one proxy setting, like Windows, and GNOME keeps a proxy password in plain text. Profiles keep no passwords.
- **Network profile tools for Windows** usually need admin rights or a service of their own, and don't notice when the settings change after a switch. Profiles need neither, and show the change.
- **Browser proxy extensions** switch one browser. Profiles switch Windows' settings, which every browser and most programs use.
- **Programs that set the proxy while they run**, such as debugging proxies and some VPN tools, leave a proxy that no longer exists when they stop unexpectedly, and fight other programs over the settings. Profiles only write complete settings the user chose, never take them back, and keep Original settings as a way back.

## Profiles

A profile holds a name and the same proxy settings as Windows' own page, Settings > Network & internet > Proxy:

- **Automatically detect settings**, on or off.
- **Use setup script**, on or off, and the **script address**, an `http` or `https` address.
- **Use a proxy server**, on or off, with:
  - the **proxy server**, `address:port` such as `proxy.example.com:8080`, optionally after `http://`, as Windows Settings saves it, or a server for each protocol as Windows stores it, such as `http=proxy.example.com:8080;https=proxy.example.com:8443`. Windows Settings shows the latter in its address box, with the port box empty;
  - **exceptions**, addresses that don't use the proxy, separated by semicolons, with `*` as a wildcard, such as `*.example.com;10.*`;
  - **Don't use the proxy server for local (intranet) addresses**, which Windows stores as `<local>` at the end of the exceptions, and Windows Settings shows as its own box.
- A profile with everything off is "no proxy".
- Applying a profile writes the parts that are on. A part that's off is only turned off: Windows keeps its last proxy server and script address, as it does when the user turns them off in Windows Settings.
- A profile matches Windows' settings when the same parts are on and their values are the same, ignoring case, spaces at the ends, an `http://` before a server and the order of the exceptions. The values of parts that are off don't count. Windows Settings adds `http://` when it saves a server, so `proxy.example.com:8080` still matches after the user saves the same server there.
- When the feature is first turned on, the list starts with one profile, **Original settings**, made from Windows' current settings, so the user always has a way back. **Add from current settings** opens the profile editor with Windows' current settings, for a name.
- Up to 20 profiles. Names are up to 60 characters, and no two profiles have the same name, ignoring case. The proxy server, the exceptions and the script address are up to 2,048 characters each.
- A profile that can't be used, such as one with a proxy server that isn't in the formats above or a script address that isn't `http` or `https`, is left out of the menu, with a warning in the log that gives its number. The page shows the problem on the profile.

## Changing the settings

- PC Companion changes the current user's proxy settings for the network: the LAN settings, which Windows uses for Ethernet and Wi-Fi. It uses the documented WinINet functions, as Windows Settings does, and never writes the registry values itself:
  - it reads them with `InternetQueryOption` and `INTERNET_OPTION_PER_CONNECTION_OPTION` for the default connection, asking for `INTERNET_PER_CONN_FLAGS_UI` and falling back to `INTERNET_PER_CONN_FLAGS`;
  - it writes them with `InternetSetOption` and the same option, then tells programs with `INTERNET_OPTION_SETTINGS_CHANGED` and `INTERNET_OPTION_REFRESH`.
- VPN and dial-up connections have their own proxy settings in Windows Settings, and while one of them is connected, Windows uses its settings instead. Profiles don't change them; the page says so.
- Programs that follow the new settings:
  - Microsoft Edge, Google Chrome and other Chromium browsers, right away, unless a browser policy sets their own proxy;
  - Firefox with its default, **Use system proxy settings**, right away;
  - programs that use WinINet, or that ask WinHTTP for the user's settings, and .NET 9 and later programs for their new connections;
  - older .NET programs and some others only when they next start;
  - Linux distributions in WSL on Windows 11, when they start;
  - Windows Settings, the next time its Proxy page opens; a page that's already open keeps showing the old settings.
- Programs that don't: most command-line tools, such as curl and Git, which read environment variables or their own settings; Windows Update and other services, which first use the computer's own proxy setting (`netsh winhttp`), which needs admin rights. The page says this in a note.

## Managed settings

- An organization can manage the proxy settings with Group Policy. PC Companion reads the policies each time it reads the settings, and when either is set, switching isn't available: the profiles in the menu are dimmed, the submenu starts with a dimmed **Your organization manages the proxy settings**, and the page shows the same note and no **Use** buttons. The policies:
  - **Prevent changing proxy settings**: the value `Proxy` (or `Autoconfig`) set to 1 in `Software\Policies\Microsoft\Internet Explorer\Control Panel`, for the user or the computer. Only Windows Settings honors it: `InternetSetOption` changes the settings anyway, so PC Companion checks for it itself;
  - **Make proxy settings per-machine (rather than per-user)**: `ProxySettingsPerUser` set to 0 in `HKLM\Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings`. WinINet then keeps the settings for the computer, in HKLM. A program running as administrator changes them for every user of the PC; one without admin rights is told the change succeeded, and nothing changes. PC Companion makes no changes then, even while it runs as administrator.

## Threads

- Network Profiles has no thread of its own: it doesn't work all the time, but waits for Windows to signal a change, and switching is rare and short.
- **The host's UI thread** runs `Apply`, which reads the section, checks the profiles and starts or stops the watch, and builds the submenu each time the PC Companion icon's menu opens, from the state the feature already has, without reading anything. A chosen profile goes to the thread pool.
- **The thread pool** reads Windows' settings and the policies, writes a profile, and passes the results back to the UI thread with `MessageWindow.BeginInvoke`.
- **The watch:** `RegNotifyChangeKeyValue` on `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings` and its subkeys, where WinINet keeps the settings, as Chromium, Firefox and .NET watch them, and, to see the per-machine policy and the computer's settings, on the same key in HKLM and on `HKLM\Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings`. With `REG_NOTIFY_THREAD_AGNOSTIC`, a thread pool wait (`RegisterWaitForSingleObject`) serves each, so no thread waits. Each signal sets its watch again before the read, so a change during the read gets a signal of its own.
- Each change gives two signals about 1 ms apart, from PC Companion, another program or Windows Settings alike, so a read starts 50 ms after a signal, and signals that come while a read is waiting or running lead to one more read, not one each.
- **The settings app** reads, writes and watches the same way on its own thread pool while the page is open.
- **Stopping** removes the wait and closes the key; a read or write that's running finishes, and its result is dropped.
- **Failures:** the thread pool callbacks run inside the feature's error boundary. A failure there is logged, stops the watch and keeps the feature off until the host restarts, like a failing `Apply`.

## The PC Companion icon's menu

- A running feature adds a submenu through `IHostMenuItems` in Core: the host asks each feature for its items each time the menu opens, on the UI thread, and a feature answers from the state it already has, so the menu never waits. The chosen item goes back to its feature. See [Running in the host](../../architecture/features.md#running-in-the-host).
- `PopupMenu` gets submenus and radio check marks.

## Settings

- **Feature page:** the on/off switch; **Profiles**, a list with **Add profile** and **Add from current settings**, and for each profile **In use** or **Use**, **Edit**, **Move up**, **Move down** and **Delete**; **Open Windows proxy settings** (`ms-settings:network-proxy`); a note on VPN connections and the programs that don't follow the settings; and a **Permissions & data** card.
- **The profile editor** is a dialog laid out like Windows' Proxy page: **Name**; **Automatically detect settings**; **Use setup script** and **Script address**; **Use a proxy server**, **Proxy server**, **Exceptions** and **Don't use the proxy server for local (intranet) addresses**.
- Problems show below their box, as in Hot Corners, and **Save** is available only when the profile has none. **Delete** asks first.
- A profile keeps its values when a part is turned off, so turning it on again brings them back.
- The switch on Home changes only `enabled` and keeps the profiles. Turning the feature on for the first time adds Original settings.
- A section that can't be read, for example after a hand edit, turns Network Profiles off with a warning in the log.

```json
"features": {
  "networkProfiles": {
    "enabled": true,
    "profiles": [
      {
        "name": "Office",
        "proxy": {
          "autoDetect": false,
          "useScript": false,
          "scriptAddress": "",
          "useServer": true,
          "server": "proxy.example.com:8080",
          "exceptions": ["*.example.com", "10.*"],
          "bypassLocal": true
        }
      },
      {
        "name": "Home",
        "proxy": {
          "autoDetect": true,
          "useScript": false,
          "scriptAddress": "",
          "useServer": false,
          "server": "",
          "exceptions": [],
          "bypassLocal": false
        }
      }
    ]
  }
}
```

- `proxy` is one part of a profile; DNS and adapter settings would be parts of their own next to it.

## Not included

- DNS and network adapter settings, which need the elevated helper.
- The proxy settings of VPN and dial-up connections, and the computer's own proxy setting for services (`netsh winhttp`), which needs admin rights.
- Environment variables for command-line tools, such as `http_proxy` and `no_proxy`. Their formats differ from tool to tool and from Windows' exceptions, and a variable left behind overrides Windows' settings in .NET and Python programs.
- Switching on its own when the PC joins a network or Wi-Fi, which belongs to profiles and context triggers ([#56](https://github.com/Lazyb0y/pc-companion/issues/56)).
- A shortcut or a command line for switching.
- Keeping a profile in place when another program, such as a VPN or a security product, changes the settings. PC Companion shows the change instead of fighting it.
- Proxy user names and passwords: Windows' proxy settings have none, and programs ask for them when a proxy needs them.
- Editing setup scripts, and rules that send some sites or programs through a proxy.
- Importing and exporting profiles.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- Every API is documented and in every supported version: `InternetQueryOption` and `InternetSetOption` with the per-connection options, `RegNotifyChangeKeyValue` with `REG_NOTIFY_THREAD_AGNOSTIC` (Windows 8 and later) and `RegisterWaitForSingleObject`.
- Microsoft documents the same Proxy page for Windows 10 and Windows 11.

## Security and privacy

- **Changes:** the current user's proxy settings for the network, only when the user chooses a profile in the menu or selects **Use** on the page. Through a proxy go the user's web connections, so the profiles come only from the user's settings, and the page shows a profile's server before it's used.
- **Reads:** Windows' proxy settings and the proxy policies, when they change and when the feature starts.
- **Settings:** the profiles are in `settings.json`. Any program running as the user can change that file, but it could also change the proxy settings itself, with the same functions, so this crosses no boundary while PC Companion runs as the user. While it runs as administrator, it still changes only the user's own settings, and never the computer's, which a per-machine policy would make them; see [Managed settings](#managed-settings).
- **Untrusted values:** a hand-edited file can hold anything, so every value is checked before it's written: the formats above, the lengths, and no control characters.
- **Network:** PC Companion makes no connections. With automatic detection or a setup script, Windows and the programs that use the settings look for the proxy and download the script, as with Windows' own settings. `PRIVACY.md` gets a line on Network Profiles.
- **Logs:** Debug entries when a profile is chosen and applied, by number, when Windows' settings change and which profile they match, by number, and when a policy is found. Warnings when a profile can't be used and when Windows refuses a change, with its error code. Never profile names, proxy servers, exceptions or script addresses, which can name an employer or its internal sites.
- **Rights:** runs without admin rights and changes nothing that needs them.
- **Failures:** a profile that can't be applied leaves the settings as they were; the check mark stays where it was. After each write, PC Companion reads the settings back, since Windows can report success without changing them, and logs a warning when they don't match the profile. Settings are always complete Windows settings, never a temporary proxy of PC Companion's own, so if PC Companion stops, the connection keeps working as the last profile set it.

## Performance

- Between switches nothing runs but one thread pool wait for the registry key: no timer and no polling.
- Opening the menu reads nothing. A read takes about 0.6 ms, and 15 ms the first time, while Windows loads WinINet. A switch that changes the settings takes 20 to 45 ms, with the notification to other programs; all of it runs on the thread pool.
- The host loads `wininet.dll` and about 20 other system DLLs, such as `winhttp.dll`, once the feature first reads the settings. In a .NET 10 process, that added about 1 MB of private memory and 6 MB to the working set, mostly shared system code.

## Accessibility and localization

- The submenu is a Windows menu: it works with the keyboard, with screen readers, which read the radio mark as checked, and in high contrast, follows dark mode as the rest of the PC Companion icon's menu does, and is laid out right to left in right-to-left languages.
- A `&` in a profile's name shows as written, not as an access key.
- The page and the profile editor work with the keyboard alone. Their boxes, lists and buttons have labels for screen readers, the profile buttons carry the profile's name, such as "Use Office", **In use** is announced, and problems below a box are announced.
- Proxy servers, exceptions and addresses are shown left to right in every language.
- Original settings gets its name in the user's language when the feature is first turned on; after that it's a name like any other.
- The strings are translated like the other pages', marked "Needs native review."

## Spike results

A spike in Windows Sandbox (Windows 11 24H2), with a .NET 10 helper that reads and writes the settings through WinINet, confirmed:

- Reading and writing the LAN settings with a NULL handle worked from an ordinary process, with no `InternetOpen` first. A server for each protocol and exceptions with `<local>` read back exactly as written, and a part turned off kept its value.
- Windows Settings showed a server for each protocol whole in its address box, with the port box empty, and the exceptions without `<local>`, with its local addresses box checked. Checking that box and saving wrote `<local>` at the end of the exceptions, and saving also changed `single.test:3128` to `http://single.test:3128`.
- A Proxy page that was already open didn't show a switch; it showed it when opened again.
- Edge, already open, used the new proxy within 0.3 seconds of a switch, for its own requests and for a page opened then, with two fake proxies on the loopback address and no network.
- The watch got no signal while nothing changed, and two signals about 1 ms apart for each change: a switch through the API, one that changed only the parts that are on, and a toggle in Windows Settings.
- With Prevent changing proxy settings set for the user or the computer, for the proxy server or the setup script, `InternetSetOption` still succeeded and changed the settings.
- With per-machine settings, a write from a process started with a basic-user token was reported as successful and changed nothing; one running as administrator changed the computer's settings in HKLM. Windows Sandbox kept that process's admin rights, so a standard user is tested by hand.
- Times and memory, in [Performance](#performance).

## Testing

- **Unit tests** with a fake proxy store and a fake watch:
  - matching: the parts that are on, case, spaces at the ends, `http://` before a server, the order of the exceptions, `<local>`, values of parts that are off;
  - applying: only the parts that are on are written; a refused change, or one Windows reports but doesn't make, keeps the check mark where it was;
  - checks: server formats, port range, script addresses that aren't `http` or `https`, control characters, the lengths, duplicate names, the 20-profile limit; unusable profiles are left out with a warning;
  - policies: each one dims the profiles and blocks applying, also while running as administrator;
  - the menu: order, radio mark, Other proxy settings in use, the policy note, `&` in names, no submenu while off or with no profiles;
  - the watch: two signals 1 ms apart lead to one read; signals during a read lead to one more read; a result after stopping is dropped.
- **Sandbox checks** (`scripts/sandbox/groups/NetworkProfiles.ps1`): the Home switch turns the feature on and adds Original settings with Windows' current settings; the page adds, edits, moves and deletes profiles, and the editor refuses a bad server and script address. The submenu, read with UI Automation, lists the profiles; choosing one changes Windows' settings, read back with `InternetQueryOption`, and moves the radio mark. A change made by a helper with `InternetSetOption`, or saved in Windows Settings' Proxy page through UI Automation, moves the mark, or shows Other proxy settings in use. Edge, already open, uses the new proxy after a switch, with two fake proxies on the loopback address. Each policy value dims the profiles and leaves the settings unchanged. Turned off, the submenu is gone and the settings stay as they were. No network is needed.
- **By hand:**
  - Chrome and Firefox following a switch while open;
  - per-machine settings as a standard user, which Windows Sandbox can't run;
  - a VPN connection with and without its own proxy settings;
  - Always run as administrator with UAC on: switching changes the user's own settings;
  - a real proxy and a setup script on a company network;
  - a screen reader reading the submenu and the page, and a right-to-left language.
