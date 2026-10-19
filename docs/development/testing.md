# Testing

## Tests

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
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1
```

- Builds the installer for this PC's architecture and runs the groups of checks in `scripts\sandbox\groups`, with `scripts\sandbox\run-checks.ps1`, in Windows Sandbox with networking off, so this PC isn't changed. The Sandbox window closes when the checks finish.
- `-Checks` runs only the groups named, such as `-Checks HotCorners` or `-Checks Setup,Dialogs`. Smoke, a short check that PC Companion installs and starts, always runs first, and the groups run in a fixed order. Without `-Checks`, every group runs, Dialogs included.
- Checks a per-user install and uninstall, the startup entry and **Start with Windows**, that the settings app starts the host and opens once, that its window is out of sight until its page is there and hidden before it's taken down, the page layout on Home, Hot Corners, App Windows and General at window widths from narrow to the full screen and right to left, the tray menu and its **Exit**, which also closes the settings app, selecting the tray icon, or starting the host again from the Run dialog, bringing the settings window to the front from minimized or from behind another window, Arabic (right to left, dark) and Bangla (light), that the host follows settings changes, detailed logging turning on from the General page and off by itself, Hot Corners (the Home switch, its page, corners triggered by moving the pointer, and Run a program), App Windows (the Home switch, its page, and switching File Explorer windows with the key above Tab, also after switching to the German layout in the same window), the logs, Always run as administrator in an all-users install (its tasks, the protected logs folder, the elevated helper's log and why it failed, no **Open host logs** with UAC off, and uninstalling), and what uninstalling removes: another build of the same version replaces the installed one and an upgrade to the next version does too, both keeping the settings and logs, and these and an uninstall with the reduced pages Settings > Apps shows close the settings app and the host before Windows Installer looks for files in use, so they ask nothing and need no restart, as does the host's `--exit`, which returns once both have ended, a per-user uninstall removes them, or with `KEEPSETTINGS=1` all but `settings.json`, and an all-users uninstall removes `%ProgramData%\PCCompanion` and the user's folder, but not what links planted in it lead to. `sandbox-test.ps1` makes another build of the same version and the next version's installer from the one it built, each with its own product and package codes.
- Each group starts with nothing of PC Companion running and without the user's settings and logs, and with PC Companion installed just for the current user, or not installed for Smoke, RunAsAdmin and Dialogs, so it doesn't depend on what another group left. Once a group is done, its logs are checked for errors and for the user name.
- Writes the report to `TestResults\sandbox`, with a folder for each group that holds its screenshots and logs, and exits with an error when a check fails.
- Needs the Windows Sandbox feature; only one Sandbox can run at a time. `-NoBuild` uses the installer already built.
- Windows Sandbox starts Smart App Control in evaluation mode, where, with networking off, every install waits about two minutes for its check before Windows Installer goes on. So the checks turn it off before they install anything. `-SmartAppControl Evaluation` leaves it as the Sandbox starts it, and `-SmartAppControl On` turns it on: PC Companion then installs, but an unsigned build is blocked when it starts. The report says which was used.
- Keep the Sandbox window visible and the mouse outside it while the checks run. A minimized Sandbox draws nothing, so its screenshots come out blank (the report notes this), and the Hot Corners checks can't move the pointer inside it.

```
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Checks Dialogs
```

- Checks the installer's dialogs, with the Dialogs group, `scripts\sandbox\groups\Dialogs.ps1`, after Smoke; run it when the installer changes. It takes about 3 minutes, or about 13 when Smart App Control isn't off.
- Clicks through **Advanced** with the mouse, just for you and for all users: the license, scope and folder pages, that no feature page opens, Back and Next, then the install folder, startup entry, Start menu shortcut and Settings > Apps entry, and the **Open PC Companion** checkbox: left on just for you, where Finish opens the settings app, and cleared for all users, where it opens nothing. Then it removes PC Companion through the maintenance dialog while the settings app and the host run, where Change is turned off, then the Keep my settings page, unchecked, which it leaves so just for you and checks for all users, and Back from Ready to remove. It checks that setup closed them without asking, that everything is gone, and that the user's settings stay only when kept.
- Also checks a silent install and uninstall for all users, and that a silent install opens nothing.
- Writes a screenshot of each page and the install logs to the Dialogs folder.
