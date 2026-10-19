# Testing

## Unit tests

```
dotnet test --solution PCCompanion.slnx -c Debug -p:Platform=x64
```

- xUnit v3 on Microsoft.Testing.Platform, which `global.json` selects for `dotnet test`. Test projects go in `tests/` and are included in the solution.
- `PCCompanion.Architecture.Tests` checks that every project is in the solution, that project references have no cycles, and that they follow the [dependency rules](../architecture/overview.md#dependencies).

## Checks before a pull request

```
powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
```

- Builds Debug and Release for x64 and ARM64 and fails on any warning, runs the tests of each build, and checks the lock files with a locked restore, as CI does. It lists lock files the build changed, which belong in the commit.
- `-Quick` builds only Debug x64; `-Installer` also builds the installers.

## Testing in Windows Sandbox

```
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Checks HotCorners
```

- Builds the installer for this PC's architecture and runs groups of checks in Windows Sandbox with networking off, so this PC isn't changed. The Sandbox window closes when the checks finish.
- `-Checks` runs only the groups named, such as `-Checks HotCorners` or `-Checks Setup,Dialogs`. Smoke always runs first, and the groups run in the order below, whatever the order given. Without `-Checks`, every group runs.
- Each group is a script in `scripts\sandbox\groups`, which `scripts\sandbox\run-checks.ps1` runs with the helpers of `common.ps1`.
- Each group starts with nothing of PC Companion running and without the user's settings and logs, and with PC Companion installed just for the current user, or not installed for Smoke, RunAsAdmin and Dialogs, so it doesn't depend on what another group left. Once a group is done, its logs are checked for errors and for the user name.
- Writes the report to `TestResults\sandbox`, with a folder for each group that holds its screenshots and logs, and exits with an error when a check fails. The report and the end of the output list each group's checks, failures and time.
- Needs the Windows Sandbox feature; only one Sandbox can run at a time. `-NoBuild` uses the installer already built.
- Windows Sandbox starts Smart App Control in evaluation mode, where, with networking off, every install waits about two minutes for its check before Windows Installer goes on. So the checks turn it off before they install anything. `-SmartAppControl Evaluation` leaves it as the Sandbox starts it, and `-SmartAppControl On` turns it on: PC Companion then installs, but an unsigned build is blocked when it starts. The report says which was used.
- Keep the Sandbox window visible and the mouse outside it while the checks run. A minimized Sandbox draws nothing, so its screenshots come out blank (the report notes this), and the Hot Corners checks can't move the pointer inside it.

### Groups

In the order they run, with the time each took on its own, including starting the Sandbox and Smoke, with `-NoBuild`:

- **Smoke**, always first (about 30 seconds): a per-user install, its files and startup entry, the host started at sign-in exiting while **Start with Windows** is off, and the settings app starting the host and showing Home.
- **App** (about 2 minutes): the General page; the page layout on Home, Hot Corners, App Windows and General at window widths from narrow to the full screen; the settings window out of sight until its page is there, and hidden before it's taken down; one settings app per user; the tray menu and its **Exit**, which also closes the settings app; selecting the tray icon, or starting the host again from the Run dialog, bringing the settings window to the front from minimized or from behind another window; and **Start with Windows**.
- **Languages** (about 1 minute 15 seconds): Arabic, right to left with the dark theme, with the page layout and the tray menu, and Bangla with the light theme; the host follows the settings changes.
- **HotCorners** (about 1 minute 50 seconds): the Home switch, its page, corners triggered by moving the pointer, a held mouse button or a missing key skipping a corner, Keep displays on, and Run a program.
- **AppWindows** (about 1 minute 20 seconds): the Home switch, its page, and switching File Explorer windows with the key above Tab, also after switching to the German layout in the same window.
- **Logging** (about 1 minute 15 seconds): detailed logging turning on from the General page and off by itself.
- **Setup** (about 1 minute 30 seconds): the host's `--exit`, which returns once the settings app and the host have ended; another build of the same version replacing the installed one, and an upgrade to the next version, both keeping the settings and logs; these and an uninstall with the reduced pages Settings > Apps shows closing the settings app and the host before Windows Installer looks for files in use, so they ask nothing and need no restart; a per-user uninstall removing the settings and logs, or with `KEEPSETTINGS=1` all but `settings.json`; setup stopping when PC Companion is installed in the other scope; and an all-users uninstall removing the user's folder, but not what links planted in it lead to. `sandbox-test.ps1` makes the other build and the next version's installer from the one it built, each with its own product and package codes.
- **RunAsAdmin** (about 1 minute 10 seconds): Always run as administrator in an all-users install: its tasks, the protected logs folder, the elevated helper's log and why it failed, and no **Open host logs** with UAC off; setup stopping when installing just for you over it; and uninstalling, which deletes the tasks and `%ProgramData%\PCCompanion`.
- **Dialogs** (about 2 minutes 50 seconds): the setup pages, clicked through **Advanced** with the mouse, just for you and for all users: the license, scope and folder pages, that no feature page opens, Back and Next, then the install folder, startup entry, Start menu shortcut and Settings > Apps entry, and the **Open PC Companion** checkbox: left on just for you, where Finish opens the settings app, and cleared for all users, where it opens nothing. For all users, another build installed through the pages replaces the first. Then it removes PC Companion through the maintenance dialog while the settings app and the host run, where Change is turned off, then the Keep my settings page, unchecked, which it leaves so just for you and checks for all users, and Back from Ready to remove. It checks that setup closed them without asking, that everything is gone, and that the user's settings stay only when kept. It also checks a silent install and uninstall for all users, and that a silent install opens nothing, and saves a screenshot of each page.

A run of every group takes about 9 and a half minutes, and building the installer about a minute more. With Smart App Control not off, every install waits about two minutes more.

### What to run for a change

- A feature: its group, such as `-Checks HotCorners`. Smoke runs with it.
- The settings app's window, navigation or pages, the tray icon or **Start with Windows**: App.
- Strings, translations or right-to-left layout: Languages, with the group of the feature whose page changed.
- Logging: Logging.
- The installer, or what setup runs, such as the host's `--exit` and `--remove-data`: Setup and Dialogs.
- Elevation, the elevated helper or all-users installs: RunAsAdmin and Setup.
- Code changes also need `verify.ps1`, with `-Installer` only when the installer changes; CI builds the installers.
- Every group, without `-Checks`: after a few big changes, and before a pull request or a release.

### Adding a group

- A feature with runtime behavior worth checking gets a group: `scripts\sandbox\groups\<Feature>.ps1`, and an entry in `$startStates` in `run-checks.ps1` with what it starts from. Groups that remove PC Companion or install it for all users go last.
- A group opens what it needs, such as the settings app, and doesn't rely on another group. Helpers that more than one group uses go in `common.ps1`.
