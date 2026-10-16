# Packaging

```
dotnet build src/PCCompanion.Installer/PCCompanion.Installer.wixproj -c Release -p:Platform=x64
```

Output: `src/PCCompanion.Installer/bin/x64/Release/PCCompanion-x64.msi`.

- WiX 5 MSI in `src/PCCompanion.Installer`. The WiX SDK version is pinned in `global.json`.
- The installer publishes the app self-contained (Release) and installs every published file.
- One MSI for both install scopes. **Install** installs just for the current user; **Advanced** offers "Install just for you" or "Install for all users of this machine":

| Scope | Folder | Start menu shortcut | Admin rights |
|---|---|---|---|
| Just for you (default) | `%LOCALAPPDATA%\Programs\PCCompanion` | Current user | No |
| All users | `C:\Program Files\PCCompanion` | All users | Yes |

- Silent install: `msiexec /i PCCompanion-x64.msi /qn` installs just for the current user; add `ALLUSERS=2 MSIINSTALLPERUSER=""` to install for all users.
- The dialog's default folders are replaced in `Package.wxs` so both scopes use the folders above.
- Features that run elevated (always running as administrator, the elevated helper) require an "all users" install. The per-user folder is writable by the user, so an elevated process started from it could be replaced.
- Anything the app adds outside its install folder, such as startup entries or scheduled tasks, must be removed on uninstall. Windows Installer only removes what it installed.
- The app project sets `EnableMsixTooling` although the app is unpackaged: publishing then includes `PCCompanion.pri` with the compiled XAML embedded. Without it, the installed app crashes on start.
- WinUI resources for `gd-gb`, `mi-NZ` and `ug-CN` aren't installed: Windows Installer validation (ICE03) doesn't recognize their language IDs.
- The installer build shows four WIX1076 warnings (ICE03 string overflow) and doesn't fail on warnings: `Microsoft.ui.xaml.dll` and `Microsoft.UI.Xaml.Phone.dll` declare 86 languages, more than the MSI Language column holds.
- Build from a short path; WiX 5's native helper fails on very long paths.
- Installers are currently unsigned.
