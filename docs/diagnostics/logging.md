# Logging

- Logs are in `%LOCALAPPDATA%\PCCompanion\Logs`: `app-yyyyMMdd.log` for the settings app and `host-yyyyMMdd.log` for the host.
- A new file starts every day and at 5 MB. Files older than 14 days are deleted, and at most 30 are kept.
- Entries at Information level and above are written.
- Unhandled exceptions are logged as Critical before the process stops.
- Logs are in English, for every user interface language.

## Writing log entries

- Serilog through `Microsoft.Extensions.Logging`. Define messages with `[LoggerMessage]`; the analyzers require it.
- `AddFileLog` in `PCCompanion.Logging` sets up the files.
- The user's profile folder in log text, including exception messages, is replaced with `%USERPROFILE%`, so paths don't show the user name.
- Never log passwords, tokens, secrets or file contents. Keep personal details and paths to what diagnosing a problem needs.
