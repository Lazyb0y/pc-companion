# Architecture overview

## UI

- WinUI 3 on the Windows App SDK.
- The settings app is a packaged, single-project MSIX app. It depends on the Windows App Runtime framework package.
- The Windows App SDK version is set in `Directory.Packages.props`.

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
