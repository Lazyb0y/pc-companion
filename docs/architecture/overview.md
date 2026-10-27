# Architecture overview

## Processes

- `PCCompanion.Host.exe` runs in the background with a tray icon. Selecting the icon, or **Settings** in its menu, opens the settings app, or brings it to the front, restored, when it's already open; **Exit** stops the host and closes the settings app.
- One host runs per user session. Starting it again asks the running host to open the settings app, and lets it bring the settings window to the front.
- `PCCompanion.exe` is the settings app. It opens on demand and exits when its window closes. It starts the host when the host isn't running, off its window's thread, since the first start after an install can take seconds; only one settings window opens per user session.
- **Exit** asks the settings app to close through a named event in the user session, `Local\PCCompanion.App.Exit`. Nothing else sends it, so the settings app stays open when the host stops any other way.
- Windows starts the host at every sign-in. It keeps running only for users who turned on **Start with Windows**.
- Only the settings app saves settings. The host watches the settings file and loads it again after each save: it reads the file off its UI thread and applies it there, and a read that ends after a later one isn't applied.
- The host gives the settings app what only it knows, such as Hot Corners' recent results and Folder Rules' history, through named pipes for the user's own account, never through files: a host running as administrator mustn't write where programs without admin rights can redirect it. Hot Corners' pipe only gives its results; Folder Rules' pipe reads a request and checks it before it acts. See [Hot Corners](../features/input/hot-corners.md#recent-results) and [Folder Rules](../features/files/folder-rules.md#the-settings-app-and-the-host).
- Both are self-contained and install into one folder; see [Packaging](../development/packaging.md).

## UI

- WinUI 3 on the Windows App SDK.
- The app is unpackaged and self-contained: the .NET and Windows App SDK runtimes ship with it, so it needs no separate runtime install.
- Only the Windows App SDK components the app uses are referenced: `Microsoft.WindowsAppSDK.WinUI` and its dependencies, not the full `Microsoft.WindowsAppSDK` package. Versions are set in `Directory.Packages.props`.
- The window has a navigation pane with **Home** and, at the bottom, **General**: language, app theme, start with Windows, logs, detailed logging, the update check and the version. Changes are saved right away.
- Layout follows Windows Settings. In windows at least 1,008 pixels wide, the pane is open beside the page; in narrower ones it folds away behind its button, and the page starts below the button's row.
- Each page's content is a column in the middle of the page, at most 1,000 pixels wide, with at least 36 pixels on each side; its title lines up with its cards. Every page takes the column's size and spacing from one style, `PageColumnStyle` in `App.xaml`. Pages scroll only up and down, and nothing in them may need more width than a narrow window has.
- Theme: light, dark or the Windows app mode, from the settings. The title bar follows it.
- Backdrop: Mica on Windows 11, desktop acrylic on Windows 10. Windows turns Mica off while the window isn't in focus.
- The window is cloaked, kept out of sight, until WinUI has drawn its content, about 160 ms after it's shown on a typical PC, and again as soon as it closes, so it's never seen empty. If its content isn't drawn within a second, it comes into sight anyway.
- The host's tray icon and menu use the Windows API directly, through CsWin32; the host has no UI framework.
- The tray menu follows the Windows mode, dark or light, as the taskbar does, on the Windows builds listed in [Windows support](../compatibility/windows-support.md). In high contrast, it keeps the high contrast colors.

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
| `updates.checkAutomatically` | `true` (default) or `false`; see [Update check](../features/general/update-check.md) |

## Identifiers

- Prefix `PCCompanion`. Two-letter acronyms stay uppercase, as in `System.IO`.
- `PCCompanion.exe`: the settings app (project `PCCompanion.App`), opened from the Start menu.
- `PCCompanion.Host.exe`: the background host.
- Data folder `%LOCALAPPDATA%\PCCompanion`: local, not roaming, because settings are machine-specific.
- User-visible name: PC Companion.

## Repository layout

- `PCCompanion.slnx`: the solution, at the repository root.
- `src/<Project>/`: projects.
- `tests/`: test projects, and `PCCompanion.Testing` with the test doubles and helpers they share.
- `docs/`: documentation.
- `website/`: the project website; see [Website](../development/website.md).
- `scripts/`: PowerShell scripts for development: checks before a pull request, Windows Sandbox tests, installer inspection and the website's screenshots. See [Build](../development/build.md), [Testing](../development/testing.md), [Packaging](../development/packaging.md) and [Website](../development/website.md#screenshots).

## Projects

| Project | Contents |
|---|---|
| `PCCompanion.Core` | The settings model and the feature contract; see [Features](features.md) |
| `PCCompanion.Infrastructure` | Data folder paths, the settings store, the settings watcher, the feature runner, the host's commands for features and the [update check](../features/general/update-check.md) |
| `PCCompanion.Logging` | File logging and path redaction; see [Logging](../diagnostics/logging.md) |
| `PCCompanion.Localization` | Strings, languages and plural rules; see [Localization](../localization/overview.md) |
| `PCCompanion.Platform.Windows` | Windows API: a hidden message window, the message loop, a feature's own message loop thread, a coalescable timer, tray icons, shortcut menus, the Windows mode and high contrast, the session and display state, mouse input, displays, key and pointer state, global keyboard shortcuts, keyboard layouts, the window in front and its text cursor, top-level windows and their app IDs, window events, the taskbar and its layout, layered windows, shell actions, command-line arguments, the Open dialog, signals between processes, named pipes that give a snapshot or answer a request, elevation, scheduled tasks, protected files, text and icons drawn from pixels, the processor, memory and network adapter readings, the clipboard's text, program files, and the details of files and drives, read without opening files, and moves and copies that never replace a file |
| `PCCompanion.Modules.Clipboard` | Clipboard & Text: [Clipboard Actions](../features/clipboard/clipboard-actions.md) |
| `PCCompanion.Modules.Desktop` | Desktop: [System Meters](../features/desktop/system-meters.md) |
| `PCCompanion.Modules.Files` | Files: [Folder Rules](../features/files/folder-rules.md) |
| `PCCompanion.Modules.Input` | Input & Triggers: [Hot Corners](../features/input/hot-corners.md) |
| `PCCompanion.Modules.Network` | Network: [Network Profiles](../features/network/network-profiles.md) |
| `PCCompanion.Modules.Windows` | Window Management: [App Windows](../features/windows/app-windows.md) |
| `PCCompanion.Host` | The background host |
| `PCCompanion.Elevated` | The elevated helper: creates and deletes the scheduled tasks of [Always run as administrator](../features/general/run-as-administrator.md), and logs to the user's protected logs folder |
| `PCCompanion.App` | The settings app |
| `PCCompanion.Installer` | The MSI; see [Packaging](../development/packaging.md) |

## Dependencies

Enforced by `PCCompanion.Architecture.Tests`.

| Project | May reference |
|---|---|
| `PCCompanion.Core`, `PCCompanion.Localization`, `PCCompanion.Platform.Windows` | No other project |
| `PCCompanion.Logging`, `PCCompanion.Infrastructure` | Core |
| `PCCompanion.Modules.<Name>` | Core, Platform.Windows, Localization; never another module |
| `PCCompanion.Elevated` | Core, Infrastructure, Logging, Platform.Windows |
| `PCCompanion.Host`, `PCCompanion.App` | The projects above; never each other |
| `*.Tests` | Any project except App and Host |
| `PCCompanion.Testing` | Core |

- Nothing references `PCCompanion.App`, `PCCompanion.Host` or a test project, and only test projects reference `PCCompanion.Testing`.
- Project references never form a cycle, and every project is in the solution.
- A new project needs a rule here and in the tests.
