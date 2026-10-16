# Architecture overview

## UI

- WinUI 3 on the Windows App SDK.
- The app is unpackaged and self-contained: the .NET and Windows App SDK runtimes ship with it, so it needs no separate runtime install.
- Only the Windows App SDK components the app uses are referenced: `Microsoft.WindowsAppSDK.WinUI` and its dependencies, not the full `Microsoft.WindowsAppSDK` package. Versions are set in `Directory.Packages.props`.

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
