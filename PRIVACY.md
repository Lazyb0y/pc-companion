# Privacy

- PC Companion doesn't collect, send or share any data. It has no telemetry, analytics, ads or accounts, uploads nothing, and makes no network connections.
- It stores these files on your PC, in `%LOCALAPPDATA%\PCCompanion`:
  - `settings.json`: your settings.
  - `Logs`: diagnostic logs, kept for 14 days. **Detailed logging**, if you turn it on, adds more detail for 24 hours. Your user profile folder in them is replaced with `%USERPROFILE%`, and they contain no passwords or file contents. They leave your PC only if you share them yourself, for example by attaching them to an issue.
- The installer adds a startup entry so the app can start when you sign in; it stays in the background only if you turn on **Start with Windows**.
- Uninstalling removes the app and its startup entry. Your settings and logs stay; delete `%LOCALAPPDATA%\PCCompanion` to remove them.
