# Architecture overview

## Processes

- `PCCompanion.Host.exe` runs in the background with a tray icon. Selecting the icon, or **Settings** in its menu, opens the settings app; **Exit** stops the host and closes the settings app.
- One host runs per user session. Starting it again asks the running host to open the settings app.
- `PCCompanion.exe` is the settings app. It opens on demand and exits when its window closes. It starts the host when the host isn't running, and only one settings window opens per user session.
- **Exit** asks the settings app to close through a named event in the user session, `Local\PCCompanion.App.Exit`. Nothing else sends it, so the settings app stays open when the host stops any other way.
- Windows starts the host at every sign-in. It keeps running only for users who turned on **Start with Windows**.
- Only the settings app saves settings. The host watches the settings file and loads it again after each save.
- Both are self-contained and install into one folder; see [Packaging](../development/packaging.md).

## UI

- WinUI 3 on the Windows App SDK.
- The app is unpackaged and self-contained: the .NET and Windows App SDK runtimes ship with it, so it needs no separate runtime install.
- Only the Windows App SDK components the app uses are referenced: `Microsoft.WindowsAppSDK.WinUI` and its dependencies, not the full `Microsoft.WindowsAppSDK` package. Versions are set in `Directory.Packages.props`.
- The window has a navigation pane with **Home** and, at the bottom, **General**: language, app theme, start with Windows, logs, detailed logging and the version. Changes are saved right away.
- Theme: light, dark or the Windows app mode, from the settings. The title bar follows it.
- Backdrop: Mica on Windows 11, desktop acrylic on Windows 10. Windows turns Mica off while the window isn't in focus.
- The host's tray icon and menu use the Windows API directly, through CsWin32; the host has no UI framework.

## Settings

- One file, `settings.json` in the data folder, with a `schemaVersion`.
- Saving writes `settings.json.tmp` and then replaces `settings.json` with it, so readers never see a partly written file.
- Without a file, the defaults are used. A file that can't be read is left as it is and the defaults are used; the next save moves it to `settings.corrupt.json`.
- Hand-edited files may contain comments and trailing commas. Unknown properties are ignored, and missing ones get their defaults.

| Setting | Values |
|---|---|
| `general.language` | A BCP-47 language tag, or `null` for the Windows display language |
| `general.theme` | `System`, `Light` or `Dark` |
| `general.startWithWindows` | `true` or `false` (default) |
| `diagnostics.detailedLoggingUntil` | When detailed logging turns off, as a date and time with an offset, or `null` (default) when it is off |

## Identifiers

- Prefix `PCCompanion`. Two-letter acronyms stay uppercase, as in `System.IO`.
- `PCCompanion.exe`: the settings app (project `PCCompanion.App`), opened from the Start menu.
- `PCCompanion.Host.exe`: the background host.
- Data folder `%LOCALAPPDATA%\PCCompanion`: local, not roaming, because settings are machine-specific.
- User-visible name: PC Companion.

## Repository layout

- `PCCompanion.slnx`: the solution, at the repository root.
- `src/<Project>/`: projects.
- `tests/`: test projects.
- `docs/`: documentation.
- `scripts/`: PowerShell scripts for development: checks before a pull request, Windows Sandbox tests and installer inspection. See [Build](../development/build.md) and [Packaging](../development/packaging.md).

## Projects

| Project | Contents |
|---|---|
| `PCCompanion.Core` | The settings model and the feature contract; see [Features](features.md) |
| `PCCompanion.Infrastructure` | Data folder paths, the settings store, the settings watcher and the feature runner |
| `PCCompanion.Logging` | File logging and path redaction; see [Logging](../diagnostics/logging.md) |
| `PCCompanion.Localization` | Strings, languages and plural rules; see [Localization](../localization/overview.md) |
| `PCCompanion.Platform.Windows` | Windows API: a hidden message window, the message loop, the tray icon, shortcut menus, mouse input, displays, key and pointer state, shell actions, command-line arguments, the Open dialog, signals between processes, elevation, scheduled tasks and protected files |
| `PCCompanion.Modules.Input` | Input & Triggers: [Hot Corners](../features/input/hot-corners.md) |
| `PCCompanion.Host` | The background host |
| `PCCompanion.Elevated` | The elevated helper: creates and deletes the scheduled tasks of [Always run as administrator](../features/general/run-as-administrator.md) |
| `PCCompanion.App` | The settings app |
| `PCCompanion.Installer` | The MSI; see [Packaging](../development/packaging.md) |

## Dependencies

Enforced by `PCCompanion.Architecture.Tests`.

| Project | May reference |
|---|---|
| `PCCompanion.Core`, `PCCompanion.Localization`, `PCCompanion.Platform.Windows` | No other project |
| `PCCompanion.Logging`, `PCCompanion.Infrastructure` | Core |
| `PCCompanion.Modules.<Name>` | Core, Platform.Windows, Localization; never another module |
| `PCCompanion.Elevated` | Core, Platform.Windows |
| `PCCompanion.Host`, `PCCompanion.App` | The projects above; never each other |
| `*.Tests` | Any project except App and Host |

- Nothing references `PCCompanion.App`, `PCCompanion.Host` or a test project.
- Project references never form a cycle, and every project is in the solution.
- A new project needs a rule here and in the tests.
