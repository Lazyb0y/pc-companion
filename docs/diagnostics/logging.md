# Logging

- Logs are in `%LOCALAPPDATA%\PCCompanion\Logs`: `app-yyyyMMdd.log` for the settings app and `host-yyyyMMdd.log` for the host.
- While the host runs as administrator after a UAC prompt ([Always run as administrator](../features/general/run-as-administrator.md)), its logs are in `%ProgramData%\PCCompanion\Logs\<user SID>`, which only administrators can change; **Open host logs** on the General page opens them.
- A new file starts every day and at 5 MB. Files older than 14 days are deleted, and at most 30 are kept.
- Entries at Information level and above are written.
- **Detailed logging** on the General page adds Debug entries for 24 hours, then turns itself off. The settings store the end time (`diagnostics.detailedLoggingUntil`), so it also turns off on time after a restart; an end time more than 24 hours ahead counts as 24 hours. Both processes log when it turns on and off.
- Unhandled exceptions are logged as Critical before the process stops.
- Logs are in English, for every user interface language.

## Writing log entries

- Serilog through `Microsoft.Extensions.Logging`. Define messages with `[LoggerMessage]`; the analyzers require it.
- `AddFileLog` in `PCCompanion.Logging` sets up the files; pass it a `DetailedLogging` to let Debug entries be turned on.
- Use Debug for details that help find a problem, such as what the user did and what the app decided. They cost nothing while detailed logging is off.
- The user's profile folder in log text, including exception messages, is replaced with `%USERPROFILE%`, so paths don't show the user name.
- Never log passwords, tokens, secrets or file contents. Keep personal details and paths to what diagnosing a problem needs.
