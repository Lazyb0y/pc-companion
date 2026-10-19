# Privacy

- PC Companion doesn't collect, send or share any data. It has no telemetry, analytics, ads or accounts, uploads nothing, and makes no network connections.
- It stores these files on your PC, in `%LOCALAPPDATA%\PCCompanion`:
  - `settings.json`: your settings.
  - `Logs`: diagnostic logs, kept for 14 days. **Detailed logging**, if you turn it on, adds more detail for 24 hours. Your user profile folder in them is replaced with `%USERPROFILE%`, and they contain no passwords or file contents. They leave your PC only if you share them yourself, for example by attaching them to an issue.
- The installer adds a startup entry so the app can start when you sign in; it stays in the background only if you turn on **Start with Windows**.
- Uninstalling removes the app, its startup entry, and your settings and logs in `%LOCALAPPDATA%\PCCompanion`. To keep your settings, run the installer again, choose **Remove** and check **Keep my settings**, or uninstall with `KEEPSETTINGS=1`, for example `msiexec /x PCCompanion-x64.msi KEEPSETTINGS=1`. If others use PC Companion on the same PC, their settings and logs stay until they delete their own `%LOCALAPPDATA%\PCCompanion`.
