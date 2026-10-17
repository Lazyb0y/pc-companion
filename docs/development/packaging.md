# Packaging

```
dotnet build src/PCCompanion.Installer/PCCompanion.Installer.wixproj -c Release -p:Platform=x64
```

Output: `src/PCCompanion.Installer/bin/x64/Release/PCCompanion-x64.msi`. Platforms: `x64` and `ARM64`.

CI builds both installers on every pull request and push to `main`. Download them from the workflow run's artifacts (`PCCompanion-x64-msi`, `PCCompanion-ARM64-msi`), kept for 14 days.

- WiX 5 MSI in `src/PCCompanion.Installer`. The WiX SDK version is pinned in `global.json`.
- The installer publishes the settings app and the host self-contained, in Release with ReadyToRun, and installs both into one folder. A build step merges the two publish folders: the files they share are installed once and must be identical, or the build fails.
- Settings > Apps shows the app icon.
- One MSI for both install scopes. **Install** installs just for the current user; **Advanced** offers "Install just for you" or "Install for all users of this machine":

| Scope | Folder | Start menu shortcut | Admin rights |
|---|---|---|---|
| Just for you (default) | `%LOCALAPPDATA%\Programs\PCCompanion` | Current user | No |
| All users | `C:\Program Files\PCCompanion` | All users | Yes |

- Silent install: `msiexec /i PCCompanion-x64.msi /qn` installs just for the current user; add `ALLUSERS=2 MSIINSTALLPERUSER=""` to install for all users.
- The dialog's default folders are replaced in `Package.wxs` so both scopes use the folders above.
- The installer adds a startup entry (`Run` key, in HKCU for a per-user install and in HKLM for all users) that starts `PCCompanion.Host.exe --startup` at sign-in. The host exits at once unless the user turned on **Start with Windows**, so changing it needs no admin rights. Uninstalling removes the entry. Windows' Startup apps page lists PC Companion as on either way.
- Features that run elevated (always running as administrator, the elevated helper) require an "all users" install. The per-user folder is writable by the user, so an elevated process started from it could be replaced.
- Anything the app adds outside its install folder, such as startup entries or scheduled tasks, must be removed on uninstall. Windows Installer only removes what it installed.
- The app project sets `EnableMsixTooling` although the app is unpackaged: publishing then includes `PCCompanion.pri` with the compiled XAML embedded. Without it, the installed app crashes on start.
- WinUI resources for `gd-gb`, `mi-NZ` and `ug-CN` aren't installed: Windows Installer validation (ICE03) doesn't recognize their language IDs.
- The installer build shows four WIX1076 warnings (ICE03 string overflow) and doesn't fail on warnings: `Microsoft.ui.xaml.dll` and `Microsoft.UI.Xaml.Phone.dll` declare 86 languages, more than the MSI Language column holds.
- Build from a short path; WiX 5's native helper fails on very long paths.
- Installers are currently unsigned.

## Testing in Windows Sandbox

```
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1
```

- Builds the installer for this PC's architecture and runs `scripts\sandbox\runtime-checks.ps1` in Windows Sandbox with networking off, so this PC isn't changed. The Sandbox window closes when the checks finish.
- Checks a per-user install and uninstall, the startup entry and **Start with Windows**, that the settings app starts the host and opens once, the tray menu, Arabic (right to left, dark) and Bangla (light), that the host follows settings changes, detailed logging turning on from the General page and off by itself, and the logs.
- Writes the report, screenshots and logs to `TestResults\sandbox`, and exits with an error when a check fails.
- Needs the Windows Sandbox feature; only one Sandbox can run at a time. `-NoBuild` uses the installer already built.

## Inspecting an installer

```
powershell -ExecutionPolicy Bypass -File scripts\inspect-msi.ps1 src\PCCompanion.Installer\bin\x64\Release\PCCompanion-x64.msi
```

- Shows the main properties, the number and size of the files, the executables, the registry values and the shortcuts, without installing anything. `-Table <name>` returns every row of one Windows Installer table, such as `File` or `Registry`.
