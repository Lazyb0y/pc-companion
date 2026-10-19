# Packaging

```
dotnet build src/PCCompanion.Installer/PCCompanion.Installer.wixproj -c Release -p:Platform=x64
```

Output: `src/PCCompanion.Installer/bin/x64/Release/PCCompanion-x64.msi`. Platforms: `x64` and `ARM64`.

CI builds both installers on every pull request and push to `main`. Download them from the workflow run's artifacts (`PCCompanion-x64-msi`, `PCCompanion-ARM64-msi`), kept for 14 days.

- WiX 5 MSI in `src/PCCompanion.Installer`. The WiX SDK version is pinned in `global.json`.
- The installer publishes the settings app, the host and the elevated helper self-contained, in Release with ReadyToRun, and installs them into one folder. A build step merges the publish folders: the files they share are installed once and must be identical, or the build fails.
- Settings > Apps shows the app icon, and the Start menu shortcut uses the same icon from the package instead of the one in `PCCompanion.exe`, which the taskbar could fail to show on the first start after an install.
- The settings window sets its icon again once it can be seen, so the taskbar redraws its button.
- The setup dialogs show PC Companion's images instead of WiX's: `Banner.bmp` (493x58, the top of most pages) and `Dialog.bmp` (493x312; its left 164 pixels show on the welcome, maintenance and finish pages). `scripts\installer-images.ps1` draws both from the app icon; run it when the icon changes and commit the images.
- The progress page's text for each step and setup's error messages come from WiX's `WixUI_ErrorProgressText`. Without it, the package has no `ActionText` and `Error` tables, and Windows Installer shows its own texts with their placeholders unfilled, such as "Copying new files File: [1], Directory: [9], Size: [6]", and errors as bare codes.
- Windows Installer dialogs are always light, so the images have a white background. They hold no text, so they need no translation. Above 100% display scale, Windows Installer stretches them to fit, so the icon in them looks softer.
- `ThirdPartyNotices.txt`, with the license texts of the third-party components, is installed next to the app. `PCCompanion.Architecture.Tests` fails when a package the app or the host ships isn't listed in it.
- One MSI for both install scopes. **Install** installs just for the current user; **Advanced** offers "Install just for you" or "Install for all users of this machine":

| Scope | Folder | Start menu shortcut | Admin rights |
|---|---|---|---|
| Just for you (default) | `%LOCALAPPDATA%\Programs\PCCompanion` | Current user | No |
| All users | `C:\Program Files\PCCompanion` | All users | Yes |

- Silent install: `msiexec /i PCCompanion-x64.msi /qn` installs just for the current user; add `ALLUSERS=2 MSIINSTALLPERUSER=""` to install for all users.
- The dialog's default folders are replaced in `Package.wxs` so both scopes use the folders above. The all-users folder comes from the `ProgramW6432` environment variable: the dialog starts per-user, when Windows Installer points `ProgramFiles64Folder` at `%LOCALAPPDATA%\Programs`.
- **Advanced** then shows the folder (all users only) and the Ready to install page. The installer has no optional features, so `Package.wxs` skips the dialog's feature page and sets `ARPNOMODIFY`: Settings > Apps offers no Modify, and running the MSI again offers only Repair and Remove.
- The last page offers to open PC Companion, with an **Open PC Companion** checkbox that's on by default. It shows only after an install or an upgrade, not after a repair or a removal, and silent installs open nothing. The pages run with the rights setup was started with, so the settings app opens without admin rights unless setup itself was started as administrator. The checkbox text is English only, like the rest of the installer's own text.
- The installer adds a startup entry (`Run` key, in HKCU for a per-user install and in HKLM for all users) that starts `PCCompanion.Host.exe --startup` at sign-in. The host exits at once unless the user turned on **Start with Windows**, so changing it needs no admin rights. Uninstalling removes the entry. Windows' Startup apps page lists PC Companion as on either way.
- Features that run elevated (always running as administrator, the elevated helper) require an "all users" install. The per-user folder is writable by the user, so an elevated process started from it could be replaced.
- Anything the app adds outside its install folder, such as startup entries or scheduled tasks, must be removed on uninstall. Windows Installer only removes what it installed.
- All-users installs create `%ProgramData%\PCCompanion\Logs`, which only SYSTEM and Administrators can change and users can read, for the host's logs while it runs as administrator. Uninstalling an all-users install runs `PCCompanion.Elevated.exe delete-all-startup-tasks` as SYSTEM to delete every user's [Always run as administrator](../features/general/run-as-administrator.md) tasks; an upgrade keeps them. ICE105 warns about both, as it validates the package as if it were per-user only; the warnings stay visible.
- Uninstalling also removes PC Companion's data; an upgrade keeps it:
  - The user who uninstalls loses their `%LOCALAPPDATA%\PCCompanion`, with the settings and logs, in both kinds of install. Other users' folders stay.
  - **Keep my settings**, or `KEEPSETTINGS=1`, keeps `settings.json` and removes the rest. It's on a page of our own between the maintenance page and Ready to remove, unchecked; WiX's own pages stay as they are. Uninstalling from Settings > Apps shows no setup pages, so it always removes them.
  - The host removes it: setup runs the installed `PCCompanion.Host.exe --remove-data`, with `--keep-settings` for `KEEPSETTINGS=1`, as the user, before it removes the host's file. Any program running as the user can change that folder, so with only the user's rights, a link planted in it leads only to what the user could remove anyway. The host removes links without what they lead to; WiX's `RemoveFolderEx` would follow a file link, as Windows Sandbox showed. When setup itself runs as administrator, the host runs again without admin rights, with the token of the user's shell, or leaves the folder when there's none. As this runs before the host's file is removed, an uninstall that is rolled back doesn't bring the data back.
  - An all-users install also removes `%ProgramData%\PCCompanion`, which only administrators can change, with `RemoveFolderEx` as administrator.
- The app project sets `EnableMsixTooling` although the app is unpackaged: publishing then includes `PCCompanion.pri` with the compiled XAML embedded. Without it, the installed app crashes on start.
- WinUI resources for `gd-gb`, `mi-NZ` and `ug-CN` aren't installed: Windows Installer validation (ICE03) doesn't recognize their language IDs.
- The installer build shows four WIX1076 warnings (ICE03 string overflow) and doesn't fail on warnings: `Microsoft.ui.xaml.dll` and `Microsoft.UI.Xaml.Phone.dll` declare 86 languages, more than the MSI Language column holds.
- Another build with the same version replaces the installed one, as an upgrade does (`AllowSameVersionUpgrades`): every build is 0.0.1 for now, each has its own product code, and Windows Installer compares only the first three parts of a version. Without it, the builds installed side by side, with two entries in Settings > Apps. ICE61 warns that the package removes its own version as well as older ones; the warning stays visible.
- Windows Installer looks for the installed version only in the scope being installed. With the setup pages, it looks before the scope is chosen, in the just-for-you scope, and the install doesn't look again: it skips `FindRelatedProducts` whenever the pages ran, even if they didn't run it. So the scope page's **Next** runs `FindRelatedProducts` again once it has set `ALLUSERS`, after forgetting what was found before; otherwise an install for all users through **Advanced** would go next to the installed one. `FindRelatedProducts` can't be moved after the pages (ICE27).
- One install per user: Windows Installer can't replace an install in the other scope, so setup stops with a message when PC Companion is installed for all users and the user installs just for them, or the other way round. The installed host's component, found in either scope, shows that PC Companion is installed; that no installed version was found in the chosen scope shows that it's in the other one. Builds from before the host's component had its fixed GUID aren't found, nor are other users' just-for-you installs.
- Setup closes the settings app and the host before it removes or replaces their files, so an uninstall or an upgrade asks nothing and needs no restart:
  - Before `InstallValidate`, where Windows Installer looks for files in use and would ask to close them, setup runs the installed `PCCompanion.Host.exe --exit` as the user. It closes the settings app and the running host, as **Exit** in the tray menu does, and returns once they've ended, after five seconds at most. Setup finds the host by its component's GUID, which only builds that know `--exit` have: an older build would ignore the argument and start, and setup would wait for it.
  - Right after `InstallInitialize`, the WiX Util extension's `CloseApplication` sends what still runs `WM_CLOSE` as the user, then, after five seconds, ends it, as administrator: that covers older builds and a host running as administrator, which a message from the user may not reach. `InstallExecute` runs it before an upgrade removes the installed version (`MajorUpgrade` scheduled `afterInstallExecute`): inside the install transaction, removing it must follow one of those two directly, or setup fails with error 2613. Ending a process needs the transaction, so this step can't run before `InstallValidate`. ICE105 also warns about it, as it runs as administrator.
  - The host also exits on `WM_CLOSE` or `WM_ENDSESSION`, so Windows Installer can close it when it does find it in use, as when upgrading from a build without `--exit`; the default choice of its prompt closes it.
- Build from a short path; WiX 5's native helper fails on very long paths.
- Installers are currently unsigned.
- [Testing](testing.md#testing-in-windows-sandbox) describes how the installer is checked in Windows Sandbox.

## Inspecting an installer

```
powershell -ExecutionPolicy Bypass -File scripts\inspect-msi.ps1 src\PCCompanion.Installer\bin\x64\Release\PCCompanion-x64.msi
```

- Shows the main properties, the number and size of the files, the executables, the registry values and the shortcuts, without installing anything. `-Table <name>` returns every row of one Windows Installer table, such as `File` or `Registry`.
