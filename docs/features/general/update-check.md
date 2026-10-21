# Update check

Once a day, PC Companion asks GitHub whether a newer version is out, and **Check now** on the General page asks at once. When there is one, a notification and a banner on the General page open its release page, where the user downloads and installs it. Until the user opens the General page, a badge on its item in the navigation pane points there.

- Setting on the General page. On by default. Issues: [#83](https://github.com/Lazyb0y/pc-companion/issues/83), and [#113](https://github.com/Lazyb0y/pc-companion/issues/113) for **Check now**.
- Not included: downloading or installing updates. The user installs the new version by hand, from its release page.

## When it checks

- The host checks. The first check runs 2 minutes after the host starts, so it doesn't slow down signing in and the network has time to connect. After that, a timer looks once an hour whether a check is due, so sleep delays a check by an hour at most.
- A check is due 24 hours after GitHub last answered. A last check in the future, after the clock moved back, counts as due.
- Any answer from GitHub counts as the day's check: a release, no release (404), a rate limit or an error.
- A request that gets no answer doesn't count and is tried again at the next hourly look: no network, a name lookup, connection or TLS failure, or no answer within 30 seconds. GitHub received nothing, and a laptop that's offline when the user signs in doesn't skip the day.
- Turning the setting on runs a due check at once. Turning it off stops the timer and cancels a check under way.
- The host checks only while it runs: after sign-in with **Start with Windows** on, or once the settings app has started it. Each user's host checks for that user.

## The request

- `GET https://api.github.com/repos/Lazyb0y/pc-companion/releases/latest`: the most recent published release that isn't a draft or a pre-release.
- Two headers: `Accept: application/vnd.github+json`, and `User-Agent: PCCompanion`, which GitHub requires.
- Nothing that identifies the user or the PC: no version, Windows version or machine name in the User-Agent, no cookies, no `Authorization`, and no `If-None-Match`, since an ETag sent back could work like a cookie. The request is the same from everyone, every day.
- No `X-GitHub-Api-Version`. GitHub answers a version past its end of support with `410 Gone`, so a pinned version would end the check on the installs that are furthest out of date. Without the header, GitHub uses the oldest version it supports, and every version has `tag_name`.
- Uses the Windows proxy settings, without sending credentials to the proxy.
- An `HttpClient` is created for each check and disposed after it, so no connection stays open between checks.

## Versions

- Only `tag_name` is read from the answer. It must be `v` and three numbers, as in `v0.2.0`: ASCII digits only (`[0-9]`; `\d` in .NET also matches the digits of other scripts), no leading zeros, at most 9 digits each, and nothing after them. Any other tag, such as `v0.2.0-beta.1`, `0.2.0` or `v0.2`, is logged and means no update.
- It's compared with the first three parts of the running assembly's `Version`, which `Directory.Build.props` sets. Only a greater version is an update; the same or an older one isn't.
- No release (404), as before the first one is published, means no update.

## The result

- The host saves it to `update-check.json` in the data folder, writing `update-check.json.tmp` and then replacing the file, as the settings store does. The settings app saves it too, after **Check now** and when the General page shows a new version. `settings.json` stays the settings app's alone. The settings app reads the result for the banner, the badge and the update status.
- If the host and the settings app save at the same moment, one of the two saves fails and is logged; the next check saves again.
- Each save changes only its own values and reads the file again just before writing, so a check that waits for GitHub's answer doesn't undo a `seenVersion` saved in the meantime.
- `lastCheck` is when GitHub last answered, `latestVersion` the newest version it named, `notifiedVersion` the last version the notification showed, and `seenVersion` the last version the General page showed. An answer without a usable version keeps `latestVersion`; no release (404) clears it.
- A file that's missing, over 4 KB or can't be read counts as no check yet, and is replaced at the next save.
- A result that can't be saved still counts as the day's check until the host stops, so the host doesn't ask every hour.
- It's a cache: uninstalling removes it, also with **Keep my settings**.

```json
{
  "lastCheck": "2026-10-20T09:14:00+00:00",
  "latestVersion": "0.2.0",
  "notifiedVersion": "0.2.0",
  "seenVersion": "0.2.0"
}
```

## Notification

- When a check finds a version newer than the running one and than `notifiedVersion`, the host shows a notification: once per version, also across restarts.
- It's the tray icon's balloon (`Shell_NotifyIcon` with `NIF_INFO` and `NIIF_RESPECT_QUIET_TIME`), which Windows shows as a notification and keeps in the notification center. It needs no app ID registration, and the host still uses no UI framework. Windows' notification settings and **Do not disturb** apply to it.
- Title: "PC Companion 0.2.0 is available". Text: "Select to open its release page, where you can download it."
- Selecting it opens `https://github.com/Lazyb0y/pc-companion/releases/latest` in the default browser. The address is fixed, so nothing from GitHub's answer goes into it, and it always shows the newest release.
- The host's file description is "PC Companion", so Windows uses that name for the notification and in its notification settings.

## Banner

- While the check is on and `latestVersion` is newer than the running version, the General page shows a banner at the top: "PC Companion 0.2.0 is available. You have version 0.1.0.", with **Open release page**, which opens the same page. A newer version that **Check now** found shows it too, also with the check off, until the page closes.
- It can't be closed. It goes away once the new version is installed, or when the check is turned off.
- The page reads the result when it opens and watches the file while it's open, so the banner appears as soon as the host saves a newer version.

## Badge

- The banner is on a page the user seldom opens, so a badge on the General item in the navigation pane points to it: WinUI's dot `InfoBadge` in the accent color (`AttentionDotInfoBadgeStyle`), which Microsoft recommends for showing that updates are available.
- It shows while the check is on and `latestVersion` is newer than the running version and than `seenVersion`: a version the General page hasn't shown yet. With the check off it doesn't show, as the banner doesn't; a version that **Check now** finds is already on the open page.
- When the General page shows a version in its banner, as the page opens or as a newer version arrives while it's open, the settings app saves that version as `seenVersion` and the badge goes away. It comes back for the next version. The banner stays until the version is installed, or until the check is turned off.
- The window reads the result when it opens and watches the file while it's open, as the General page does, so the badge appears as soon as the host saves a newer version, and goes away when the check is turned off.
- When `seenVersion` can't be saved, or the settings app runs as administrator through UAC, where it never saves the result, the badge stays away until the window closes and shows again the next time it opens.
- While the window is narrower than 1,008 pixels, the pane folds away behind its button, so the badge is seen once the pane opens. WinUI has no badge for that button.
- macOS System Settings also puts a badge on General, but keeps it until the update is installed, which users complain about and hide with Terminal commands. As Microsoft's guidance for badges asks, this one goes away once seen, and the banner is what stays.

## Check now

- A card above **Check for updates**, as in Windows Update: its header says what the last check found, its description when that was, and its **Check now** button asks GitHub at once.
- It works whatever the setting, and whether or not a check is due, so a user who turned the daily check off can still check by hand.
- The settings app makes the request itself, off the window's thread: the same request as the host's, with the same headers, limits and 30 seconds for the answer. It saves the result as the host does, so it counts as the day's check: the host's next check is due 24 hours later.
- A newer version it finds is saved as the one the notification showed. The General page already shows it, so the daily check doesn't show the notification for it later.
- While a check runs, the header says "Checking for updates…" and a progress ring turns next to the button. Selecting the button again does nothing, so repeated clicks make one request; it stays enabled, so the keyboard focus stays on it.
- Leaving the page cancels a check under way.
- The settings app runs without admin rights. When it was started as administrator through UAC, for example by setup started as administrator, **Check now** is turned off and says why: the request and saving the result never run with admin rights. With UAC off, every program already has admin rights, so it checks, as the host does.

| Header | Description | When |
|---|---|---|
| Not checked for updates yet | | GitHub never answered a check. |
| You have the latest version | Last checked: 21/10/2026 10:14 | The last answer named no newer version. |
| PC Companion 0.3.0 is available | Last checked: 21/10/2026 10:14 | The last answer named a newer version. |
| Checking for updates… | The last check, as above | **Check now** is under way. |
| Can't reach GitHub | Check your internet connection, then try again. | **Check now** got no answer. |
| Can't check for updates right now | Try again later. | GitHub answered **Check now** with an error status, such as a rate limit, or with an answer that can't be read, or the check failed in another way. |

- The date and time follow the user's regional settings. The last two stay until the next **Check now**, or until the page opens again.

## While the host runs as administrator

- The request, reading the answer and the browser never run with admin rights, and the host never writes to the user's folder as administrator: a link planted there could redirect the write, which is why its logs go to a protected folder; see [Always run as administrator](run-as-administrator.md).
- So a host elevated through UAC runs a copy of itself without admin rights, with the token of the Windows shell, as for `--remove-data`:
  - `--check-for-updates` makes the request and saves the result, with the version the notification shows. It checks the setting and whether a check is due, so starting it by hand adds no request.
  - `--open-web-page` with the release page's address opens it, as it opens a feature's web page; it opens only `http` and `https` addresses.
- The elevated host then reads the result, as untrusted, and shows the notification itself.
- With UAC off, every program already has admin rights, so the host checks itself, as it writes its logs to the user's folder then.

## Settings

- General page: **Check for updates**, above the version, with the description "Once a day, ask GitHub whether a newer version of PC Companion is available."
- Stored in its own section, as `diagnostics` is: `updates.checkAutomatically`, `true` (default) or `false`. A missing value means on, so it's on after an upgrade too.
- The host applies it when it loads the settings, at startup and after every save.

```json
"updates": {
  "checkAutomatically": true
}
```

## Logging

- **Debug:** a check starting, when the next one is due, and that this version is up to date.
- **Information:** a newer version found, the notification shown and selected, and the first time in a run that GitHub can't be reached. Later ones are Debug, so an offline PC doesn't log every hour. In the settings app's log, **Check now** selected, and getting no answer.
- **Warning:** an answer with an error status, such as a rate limit, with its status code; an answer that can't be read; a result that can't be saved; and a check that fails in another way.
- **Never logged:** the answer's body (the release notes) and headers, cookies, the user's IP address and proxy credentials. Versions are logged as the parsed numbers, never as the raw tag.
- Nothing is logged as an error: a failed check changes nothing else.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- TLS 1.2, which GitHub requires, is on in every supported version, and Windows shows tray balloons as notifications in every supported version.

## Security and privacy

- **Sends:** one request a day, and one each time the user selects **Check now**, the same from everyone, with nothing about the user or the PC. As with any website, GitHub sees the IP address and the time. `PRIVACY.md` describes the request and how to turn it off.
- **Connection:** a fixed https address. Windows checks GitHub's certificate, and that check is never turned off or replaced; there's no pinning, since GitHub changes its certificates. .NET never follows a redirect from https to http, and whatever the answer, only a version comes from it.
- **Untrusted answer:** at most 1 MB, read with System.Text.Json source generation; only `tag_name` is used, with the strict pattern above. The version is rebuilt from its numbers before it's shown, saved or logged.
- **Opens:** only `https://github.com/Lazyb0y/pc-companion/releases/latest`. Nothing from the answer becomes an address, a path or a command line.
- **Rights:** the request, reading the answer and the browser never run with admin rights; see [While the host runs as administrator](#while-the-host-runs-as-administrator) and [Check now](#check-now).
- **The result file:** any program running as the user can change `update-check.json`, so it's read as untrusted, and at most 4 KB of it. A changed file can at most show a wrong version, or show or hide the badge; **Open release page** still opens the real page.
- **Failures:** the check runs on the thread pool and catches its errors, so a failed check never stops the host or its features.

## Performance

- Between checks, only the hourly timer runs. A check is one small request.
- **Check now** runs off the settings window's thread, so the window keeps answering while GitHub does.
- The badge adds no timer: the window reads the result, at most 4 KB, when it opens and when the file changes.

## Accessibility and localization

- The strings are in `Strings.resx` and translated like the others. The notification uses the host's language.
- Versions are shown as written, such as `0.2.0`, in every language.
- The banner's message and button are read by screen readers, and the switch is named after its header.
- The badge itself isn't read, so while it shows, the General item's status (`AutomationProperties.ItemStatus`) is "PC Companion 0.3.0 is available", which screen readers read with the item's name.
- The update status is a live region: screen readers announce it when it changes, such as when **Check now** finishes.

| Key | English | Where |
|---|---|---|
| `UpdateNotificationTitle` | PC Companion {0} is available | Notification title; {0} is the new version. At most 63 characters. |
| `UpdateNotificationMessage` | Select to open its release page, where you can download it. | Notification text. At most 255 characters. |
| `GeneralUpdateAvailable` | PC Companion {0} is available. You have version {1}. | Banner; {0} is the new version, {1} the running one. |
| `GeneralUpdateOpenReleasePage` | Open release page | Banner button |
| `GeneralUpdateCheckHeader` | Check for updates | Setting |
| `GeneralUpdateCheckDescription` | Once a day, ask GitHub whether a newer version of PC Companion is available. | Below the setting |
| `GeneralUpdateCheckNow` | Check now | Button |
| `GeneralUpdateStatusNotChecked` | Not checked for updates yet | Status |
| `GeneralUpdateStatusUpToDate` | You have the latest version | Status |
| `GeneralUpdateStatusAvailable` | PC Companion {0} is available | Status, and the General item's status while the badge shows; {0} is the new version. |
| `GeneralUpdateStatusChecking` | Checking for updates… | Status |
| `GeneralUpdateStatusNoAnswer` | Can't reach GitHub | Status |
| `GeneralUpdateStatusNoAnswerHint` | Check your internet connection, then try again. | Below the status |
| `GeneralUpdateStatusFailed` | Can't check for updates right now | Status |
| `GeneralUpdateStatusFailedHint` | Try again later. | Below the status |
| `GeneralUpdateLastCheck` | Last checked: {0} | Below the status; {0} is the date and time. |
| `GeneralUpdateCheckNowUnavailable` | Check now isn't available while this window runs as administrator. | Below the status |

## Testing

- **Unit tests** with a fake HTTP handler and a manual clock; none uses the network:
  - the request: its address and exactly its two headers;
  - answers: a newer, the same and an older version, no release, a rate limit (403 and 429), 410 and server errors, an answer over 1 MB, not JSON or without `tag_name`, and the tag formats above;
  - no answer: tried again an hour later, and logged once at Information;
  - the schedule: 2 minutes after start, 24 hours after an answer, a last check in the future, and turning the setting on and off;
  - the notification once per version, also after a restart;
  - the result file: missing, unreadable and over 4 KB;
  - when the banner and the badge show, and saving the version the banner shows as `seenVersion`;
  - a save keeping the values it doesn't change, such as a check that ends after `seenVersion` was saved;
  - when the host hands the check to a copy without admin rights;
  - **Check now**: asking when a check isn't due, what it found, saving a newer version as shown, and keeping the latest version after an error status.
- **Sandbox checks** (`scripts/sandbox/groups/Updates.ps1`): networking stays off. Through the hosts file, `api.github.com` leads to an HTTPS server in the Sandbox, with a certificate whose root is trusted only there. Starting with the check off, the group turns it on and checks: one request, with exactly the expected headers; the saved result; that Windows took the notification; the banner with both versions; selecting the notification starting a browser with the release page; and turning the check off hiding the banner. Then, with the check off, **Check now**: the status from the saved result; the same request, finding this version up to date; a newer version showing the banner without a notification; a rate limit; and no answer. Windows didn't show the notification in the Sandbox, likely because of quiet time for the new user it signs in, so whether it appears is checked by hand. The badge: the General item's status with a newer version saved, its going away once General opens and the version saved as seen, and its staying away after the settings app starts again.
- **By hand:** on a real network, behind a proxy, with **Do not disturb** on, and with Always run as administrator on a PC with UAC on, where Task Manager shows the browser running without admin rights. The notification appears under the name PC Companion, stays in the notification center, and selecting it there opens the release page. The badge in light, dark and high contrast themes, and Narrator reading the General item's status.
