# Always run as administrator

Runs the background host as administrator, so features also work while an app running as administrator is in front.

- Setting on the General page. Off by default. Issue: [#16](https://github.com/Lazyb0y/pc-companion/issues/16).
- Windows keeps the input and windows of apps running as administrator away from programs that don't. Without this setting, Hot Corners don't trigger while such an app is in front.

## Availability

- Only when PC Companion is installed for all users, in Program Files, which only administrators can change.
- A per-user install lives in a folder any program running as the user can change. Running the host from there as administrator would let any such program replace it and gain admin rights at the next sign-in. For per-user installs, the switch is off and can't be turned on, with the reason: "Available when PC Companion is installed for all users."
- Before turning the setting on, PC Companion checks that its folder and the host can't be changed without admin rights. If they can, the setting stays off.

## How it works

- **Turning it on:** Windows asks for admin rights once, with a UAC prompt. The elevated helper (`PCCompanion.Elevated.exe`, started on demand for this one command) then creates a scheduled task for the current user, and the host restarts as administrator.
- **The task:** starts `PCCompanion.Host.exe --startup` from Program Files at the user's sign-in, with highest privileges. There's one task per user who turned the setting on, in a `\PCCompanion\` folder in Task Scheduler. The task can't be changed without admin rights.
- **At sign-in:** the Run entry and the task both start the host. Without admin rights, the host checks the setting and exits, so the copy started by the task runs. Both copies still exit unless **Start with Windows** is on.
- **The settings app:** always runs without admin rights. When it starts the host, it runs the task instead of starting the host directly, so no prompt appears. When the host opens the settings app from its tray icon, it starts it without admin rights through the Windows shell.
- **Turning it off:** Windows asks for admin rights again. The helper deletes the task, and the host restarts without them.
- **Uninstalling** removes the tasks of every user.

## Between the host and other programs

- Windows blocks messages from programs without admin rights to windows of programs with them. The host allows only its own messages through: the tray icon's events, the taskbar's restart message, and "open the settings app" from a second host launch.
- The host's single-instance lock must stay checkable by the settings app without admin rights.
- The settings file is written by the settings app without admin rights, and any program running as the user can change it. The host treats it as untrusted. No setting may make the host run code, or read or write files, as administrator. Run a program already does nothing while the host runs as administrator.
- While the host runs as administrator, its logs go to `%ProgramData%\PCCompanion\Logs\<user>`, which only administrators can change, and the user can read. Writing them to the user's data folder would let a program redirect the writes elsewhere with a link.

## Settings

- General page: **Always run as administrator**, with the description "Lets features work while an app running as administrator is in front. Windows asks for permission when you turn it on or off."
- When turned on, the Hot Corners page's note changes to say that corners work in front of apps running as administrator, and that Run a program doesn't run.
- If the user cancels the UAC prompt, the switch goes back and nothing changes.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11. Task Scheduler is part of every supported version.

## Security and privacy

- **Rights:** only the host runs as administrator, and only when the user turns this on in an all-users install. The settings app never does. The helper runs only to create or delete the task.
- **Untrusted input:** settings, messages and files from programs without admin rights never lead to running code or touching files as administrator.
- **Persistence:** the task is the only thing added. Turning the setting off or uninstalling removes it.
- **Privacy:** nothing new is read, stored or sent. Logs follow the existing rules; only their folder changes.

## Testing

- **Unit tests:** when the setting is available, the task definition, and the start decisions (which copy of the host exits at sign-in).
- **Sandbox checks:** UAC is off in Windows Sandbox and everything runs as administrator, so the checks cover:
  - creating and deleting the task;
  - the host started through the task;
  - the per-user install showing the setting as unavailable;
  - uninstalling removing the task.
- **By hand, on a PC with UAC on:**
  - the prompt when turning it on and off, and cancelling it;
  - signing out and in;
  - the tray icon opening the settings app without admin rights;
  - Hot Corners in front of an app running as administrator.
