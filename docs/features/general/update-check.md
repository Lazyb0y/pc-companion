# Update check

Once a day, PC Companion asks GitHub whether a newer version is out. When there is one, a notification and a banner on the General page open its release page, where the user downloads and installs it.

- Setting on the General page. On by default. Issue: [#83](https://github.com/Lazyb0y/pc-companion/issues/83).
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

- The host saves it to `update-check.json` in the data folder, writing `update-check.json.tmp` and then replacing the file, as the settings store does. Only the host writes it; `settings.json` stays the settings app's alone. The settings app reads it for the banner.
- `lastCheck` is when GitHub last answered, `latestVersion` the newest version it named, and `notifiedVersion` the last version the notification showed. An answer without a usable version keeps `latestVersion`; no release (404) clears it.
- A file that's missing, over 4 KB or can't be read counts as no check yet, and is replaced at the next save.
- A result that can't be saved still counts as the day's check until the host stops, so the host doesn't ask every hour.
- It's a cache: uninstalling removes it, also with **Keep my settings**.

```json
{
  "lastCheck": "2026-10-20T09:14:00+00:00",
  "latestVersion": "0.2.0",
  "notifiedVersion": "0.2.0"
}
```

## Notification

- When a check finds a version newer than the running one and than `notifiedVersion`, the host shows a notification: once per version, also across restarts.
- It's the tray icon's balloon (`Shell_NotifyIcon` with `NIF_INFO` and `NIIF_RESPECT_QUIET_TIME`), which Windows shows as a notification and keeps in the notification center. It needs no app ID registration, and the host still uses no UI framework. Windows' notification settings and **Do not disturb** apply to it.
- Title: "PC Companion 0.2.0 is available". Text: "Select to open its release page, where you can download it."
- Selecting it opens `https://github.com/Lazyb0y/pc-companion/releases/latest` in the default browser. The address is fixed, so nothing from GitHub's answer goes into it, and it always shows the newest release.
- The host's file description is "PC Companion", so Windows uses that name for the notification and in its notification settings.

## Banner

- While the check is on and `latestVersion` is newer than the running version, the General page shows a banner at the top: "PC Companion 0.2.0 is available. You have version 0.1.0.", with **Open release page**, which opens the same page.
- It can't be closed. It goes away once the new version is installed, or when the check is turned off.
- The page reads the result when it opens and watches the file while it's open, so the banner appears as soon as the host saves a newer version.

## While the host runs as administrator

- The request, reading the answer and the browser never run with admin rights, and the host never writes to the user's folder as administrator: a link planted there could redirect the write, which is why its logs go to a protected folder; see [Always run as administrator](run-as-administrator.md).
- So a host elevated through UAC runs a copy of itself without admin rights, with the token of the Windows shell, as for `--remove-data`:
  - `--check-for-updates` makes the request and saves the result, with the version the notification shows. It checks the setting and whether a check is due, so starting it by hand adds no request.
  - `--open-release-page` opens the release page, and nothing else.
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
- **Information:** a newer version found, the notification shown and selected, and the first time in a run that GitHub can't be reached. Later ones are Debug, so an offline PC doesn't log every hour.
- **Warning:** an answer with an error status, such as a rate limit, with its status code; an answer that can't be read; a result that can't be saved; and a check that fails in another way.
- **Never logged:** the answer's body (the release notes) and headers, cookies, the user's IP address and proxy credentials. Versions are logged as the parsed numbers, never as the raw tag.
- Nothing is logged as an error: a failed check changes nothing else.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- TLS 1.2, which GitHub requires, is on in every supported version, and Windows shows tray balloons as notifications in every supported version.

## Security and privacy

- **Sends:** one request a day, the same from everyone, with nothing about the user or the PC. As with any website, GitHub sees the IP address and the time. `PRIVACY.md` describes the request and how to turn it off.
- **Connection:** a fixed https address. Windows checks GitHub's certificate, and that check is never turned off or replaced; there's no pinning, since GitHub changes its certificates. .NET never follows a redirect from https to http, and whatever the answer, only a version comes from it.
- **Untrusted answer:** at most 1 MB, read with System.Text.Json source generation; only `tag_name` is used, with the strict pattern above. The version is rebuilt from its numbers before it's shown, saved or logged.
- **Opens:** only `https://github.com/Lazyb0y/pc-companion/releases/latest`. Nothing from the answer becomes an address, a path or a command line.
- **Rights:** the request, reading the answer and the browser never run with admin rights; see [While the host runs as administrator](#while-the-host-runs-as-administrator).
- **The result file:** any program running as the user can change `update-check.json`, so it's read as untrusted, and at most 4 KB of it. A changed file can at most show a wrong version; **Open release page** still opens the real page.
- **Failures:** the check runs on the thread pool and catches its errors, so a failed check never stops the host or its features.

## Performance

- Between checks, only the hourly timer runs. A check is one small request.

## Accessibility and localization

- The strings are in `Strings.resx` and translated like the others. The notification uses the host's language.
- Versions are shown as written, such as `0.2.0`, in every language.
- The banner's message and button are read by screen readers, and the switch is named after its header.

| Key | English | Where |
|---|---|---|
| `UpdateNotificationTitle` | PC Companion {0} is available | Notification title; {0} is the new version. At most 63 characters. |
| `UpdateNotificationMessage` | Select to open its release page, where you can download it. | Notification text. At most 255 characters. |
| `GeneralUpdateAvailable` | PC Companion {0} is available. You have version {1}. | Banner; {0} is the new version, {1} the running one. |
| `GeneralUpdateOpenReleasePage` | Open release page | Banner button |
| `GeneralUpdateCheckHeader` | Check for updates | Setting |
| `GeneralUpdateCheckDescription` | Once a day, ask GitHub whether a newer version of PC Companion is available. | Below the setting |

## Testing

- **Unit tests** with a fake HTTP handler and a manual clock; none uses the network:
  - the request: its address and exactly its two headers;
  - answers: a newer, the same and an older version, no release, a rate limit (403 and 429), 410 and server errors, an answer over 1 MB, not JSON or without `tag_name`, and the tag formats above;
  - no answer: tried again an hour later, and logged once at Information;
  - the schedule: 2 minutes after start, 24 hours after an answer, a last check in the future, and turning the setting on and off;
  - the notification once per version, also after a restart;
  - the result file: missing, unreadable and over 4 KB;
  - when the banner shows;
  - when the host hands the check to a copy without admin rights.
- **Sandbox checks** (`scripts/sandbox/groups/Updates.ps1`): networking stays off. Through the hosts file, `api.github.com` leads to an HTTPS server in the Sandbox, with a certificate whose root is trusted only there. Starting with the check off, the group turns it on and checks: one request, with exactly the expected headers; the notification; the banner with both versions; selecting the notification starting a browser with the release page; and turning the check off hiding the banner.
- **By hand:** on a real network, behind a proxy, with **Do not disturb** on, and with Always run as administrator on a PC with UAC on, where Task Manager shows the browser running without admin rights.
