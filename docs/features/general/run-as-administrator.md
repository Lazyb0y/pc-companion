# Always run as administrator

Runs the background host as administrator, so features also work while an app running as administrator is in front.

- Setting on the General page. Off by default. Issue: [#16](https://github.com/Lazyb0y/pc-companion/issues/16).
- Windows keeps the input and windows of apps running as administrator away from programs that don't. Without this setting, Hot Corners don't trigger while such an app is in front.

## Availability

- Only when PC Companion is installed for all users, in Program Files, which only administrators can change.
- A per-user install lives in a folder any program running as the user can change. Running the host from there as administrator would let any such program replace it and gain admin rights at the next sign-in. For per-user installs, the switch is off and can't be turned on, with the reason: "Available when PC Companion is installed for all users."
- Before turning the setting on, PC Companion checks that its folder and the host can't be changed without admin rights. If they can, the setting stays off.
- Only for administrator accounts. A standard account would need another account's password at the prompt, and the tasks would still run it without admin rights. For standard accounts, the switch says "Needs an administrator account."

## How it works

- **Turning it on:** Windows asks for admin rights once, with a UAC prompt. The elevated helper (`PCCompanion.Elevated.exe`, started on demand for this one command) then creates the scheduled tasks for the current user, and the host restarts as administrator.
- **The tasks:** two per user who turned the setting on, in a `\PCCompanion\` folder in Task Scheduler. One starts `PCCompanion.Host.exe --startup` from Program Files when the user signs in; the other starts the host, without arguments, on request. Both run with the highest rights the user has. Only administrators can change them; the user can read and run them.
- **At sign-in:** the Run entry and the sign-in task both start the host. Without admin rights, the host checks the setting and exits, so the copy started by the task runs. Both copies still exit unless **Start with Windows** is on.
- **Other starts:** a host started without admin rights, for example by the settings app, runs the on-request task, which needs no prompt, and exits. If the task doesn't start, it keeps running without admin rights and logs a warning.
- **The settings app:** always runs without admin rights. When the host opens it from its tray icon, it starts it with the token of the Windows shell, which runs without admin rights.
- **Turning it off:** Windows asks for admin rights again. The helper deletes the tasks, and the host restarts without admin rights.
- **Uninstalling** removes the tasks of every user.

## Between the host and other programs

- Windows blocks messages from programs without admin rights to windows of programs with them. The host allows only its own messages through: the tray icon's events, the taskbar's restart message, and "open the settings app" from a second host launch.
- The host's single-instance lock must stay checkable by the settings app without admin rights.
- The settings file is written by the settings app without admin rights, and any program running as the user can change it. The host treats it as untrusted. No setting may make the host run code, or read or write files, as administrator. Run a program already does nothing while the host runs as administrator.
- While the host runs as administrator after a UAC prompt, its logs go to `%ProgramData%\PCCompanion\Logs\<user SID>`, which only administrators can change, and the user can read. Writing them to the user's data folder would let a program redirect the writes elsewhere with a link. All-users installs create the `Logs` folder; when it's missing or others can change it, the host writes no log file. With UAC off, every program already has admin rights, so the logs stay in the user's folder.

## Settings

- General page: **Always run as administrator**, with the description "Lets features work while an app running as administrator is in front. Windows asks for permission when you turn it on or off."
- When turned on, the Hot Corners page's note changes to say that corners work in front of apps running as administrator, and that Run a program doesn't run.
- If the user cancels the UAC prompt, the switch goes back and nothing changes. If the change fails, the page says so.
- While it's on, and UAC is on, the Logs card also has **Open host logs**. With UAC off, the host's logs stay in the user's logs folder, which **Open logs folder** opens.
- The helper logs what it did, and why it failed, to `elevated-yyyyMMdd.log` in the user's protected logs folder: the settings app starts it through UAC, so it can't read its output.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11. Task Scheduler is part of every supported version.

## Security and privacy

- **Rights:** only the host runs as administrator, and only when the user turns this on in an all-users install. The settings app never does. The helper runs only to create or delete the tasks.
- **Untrusted input:** settings, messages and files from programs without admin rights never lead to running code or touching files as administrator.
- **Persistence:** the tasks are the only thing added. Turning the setting off or uninstalling removes them.
- **Privacy:** nothing new is read, stored or sent. Logs follow the existing rules; only their folder changes.

## Testing

- **Unit tests:** the protected-folder check behind the setting's availability, the task definitions, and the start decisions (which copy of the host exits at sign-in, and when it runs the on-request task).
- **Sandbox checks:** UAC is off in Windows Sandbox and everything runs as administrator, so the checks cover:
  - creating and deleting the tasks, and their definitions;
  - the host started through the on-request task;
  - the per-user install showing the setting as unavailable;
  - the protected logs folder;
  - uninstalling removing the tasks.
- **By hand, on a PC with UAC on:**
  - the prompt when turning it on and off, and cancelling it;
  - signing out and in, and the host starting as administrator from the settings app;
  - the tray icon opening the settings app without admin rights;
  - Hot Corners in front of an app running as administrator;
  - the host's logs in the protected folder, and **Open host logs**.
