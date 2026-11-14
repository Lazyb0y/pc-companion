# Launcher Actions

Run PC Companion's actions from Start, Windows Search and other launchers, or with a command, such as "Use Office - Network Profiles" to switch to a network profile.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.launcherActions`. Issue: [#57](https://github.com/Lazyb0y/pc-companion/issues/57).
- Inspired by KDE Plasma's KRunner and GNOME's search, where apps add their actions to the desktop's search, and by app actions in Spotlight on macOS.
- Off until the user turns it on.
- Builds on the actions that features offer to [Shortcuts & Mouse Buttons](shortcuts-and-mouse-buttons.md#how-features-offer-actions). The issue waited for enough actions: eight features offer them now.

## Behavior

- **Actions in Start:** while the feature is on, each of its actions is an entry in Start, in a folder named **PC Companion actions**, such as "Use Office - Network Profiles". Windows Search finds the entries by name, and so do launchers that read Start, as most launchers for Windows do. The entries are in PC Companion's language and follow the settings: a new profile adds one, and renaming a profile renames it.
- **The command:** `PCCompanion.exe --run <feature> <action> [<target>]`, such as `PCCompanion.exe --run networkProfiles use Office`, runs an action from scripts, the Run box, scheduled tasks, desktop shortcuts, programmable keypads and launchers that run commands. Each Start entry runs this command. **Copy command** on the page copies an action's command.
- **The running host runs the action,** as its feature's own shortcut or menu item does, with its settings, checks and notes: Display Profiles asks whether to keep the new setup, and End App asks before it ends an app. The command only passes the request to the host, then exits.
- **An action that can't run now,** such as one whose feature was turned off, shows a notification that says why; selecting it opens the feature's page. The command's exit code says so too.
- **Only actions that don't act on the window in front** are offered: the window in front, when the action arrives, depends on the launcher (spike); see [Actions](#actions).
- **The user chooses which features' actions** show in Start, all of them by default, and can leave Start out and keep only the command.
- **Nothing runs while the user types:** launchers search the entries' names themselves, and only the chosen entry starts the command.

## Compared with other systems

- **Windows:** Start and Windows Search find apps, settings and files, but no actions inside apps. Windows' app actions need an app with package identity and Windows 11 version 24H2 or later, and act on a photo, text or file, in places such as Click to Do. Launcher Actions puts PC Companion's actions where Windows Search already looks, on Windows 10 and 11.
- **KDE Plasma:** KRunner asks each app's runner as the user types. Users report a slow runner that blocked the search ([452011](https://bugs.kde.org/show_bug.cgi?id=452011)), Enter running another result than the one shown while slow runners answered ([472164](https://bugs.kde.org/show_bug.cgi?id=472164)), and so many results that they turn runners off ([488676](https://bugs.kde.org/show_bug.cgi?id=488676)). Here nothing is asked while the user types, since the entries are already in Start, and each feature's entries can be left out.
- **GNOME:** its search asks each app's search provider as the user types. Users found providers unusable on slow PCs, and a search started an app store that then stayed in memory ([Launchpad 1616332](https://bugs.launchpad.net/ubuntu/+source/gnome-software/+bug/1616332)). Here typing starts nothing, and the host already runs.
- **macOS:** Spotlight in macOS 26 runs apps' actions, as Shortcuts does, and `networksetup -switchtolocation` switches network locations from the command line. Users report apps' actions that disappeared after updates, and Spotlight that showed them slowly. Launcher Actions' entries are plain Start entries, which the settings app writes again whenever they change.
- **Launchers for Windows and macOS** each have plugins or extensions of their own. Users report ones that broke when the app they work with changed, and ones that need that app running, and some stores make no promises about the extensions they list. Here no launcher needs a plugin: any launcher that reads Start or runs a command runs the actions, and the host checks every request.
- **Display and network profile tools for Windows** switch profiles from a command line, which users then add to their launchers themselves.
- **What this feature adds:** PC Companion's actions in the search the user already uses, in their language and without a plugin, a command for everything else, and each action's own checks and questions.

## Actions

| Feature | Action | Command | In Start |
|---|---|---|---|
| Network Profiles | Use *profile*, for each profile | `networkProfiles use <profile>` | Use Office - Network Profiles |
| Display Profiles | Use *profile*, for each profile | `displayProfiles use <profile>` | Use Desk - Display Profiles |
| Hide Apps | Show all hidden windows | `hideApps showAll` | Show all hidden windows - Hide Apps |
| End App | Choose an app to end | `endApp chooseApp` | Choose an app to end - End App |
| Drop-down Apps | Show or hide *app*, for each listed app | `dropDownApps showOrHide <app>` | Show or hide Terminal - Drop-down Apps |
| Clipboard Actions | Actions for the copied text | `clipboardActions copiedText` | Actions for the copied text - Clipboard Actions |
| Launcher Actions | Lock, Turn off displays, Start the screen saver | `launcherActions lock`, `turnOffDisplays`, `screenSaver` | Turn off displays - PC Companion |

- **Each action does what its feature does** from its own shortcut or menu: Display Profiles asks whether to keep the new setup, and goes back after 15 seconds without an answer; End App starts choosing, and asks before it ends the app; Clipboard Actions opens its menu.
- **Available only while the action's feature is on,** supported on this PC, and has what the action names: a profile with that name, or that app in Drop-down Apps' list. Start lists only those, and the host refuses the others.
- **Lock, Turn off displays and Start the screen saver** are this feature's own, as Shortcuts & Mouse Buttons has its own, so neither needs the other on. Turn off displays and Start the screen saver wait until every key and mouse button is released, then half a second, since any input turns the displays back on and ends the screen saver.
- **Not offered:**
  - **Actions on the window in front:** Hide app, Hide others, Send the window in front to the tray, Next and Previous window of the app, and Actions for the selected text. When an action arrives, the window in front is the app the user was in after a launcher that hides itself, the desktop after the Run box, and Start's own window after Start (spike), so these would act on the wrong window. Shortcuts & Mouse Buttons gives them a shortcut, and the page says so.
  - What Shortcuts & Mouse Buttons leaves out too: Keep displays on, and Title Bar Actions' and Middle-Click Paste's actions.
  - Turning features on or off, and opening their pages: the settings app does that, and `PCCompanion.exe --feature <id>` already opens a page.
- **IDs** are those of Shortcuts & Mouse Buttons' [format](shortcuts-and-mouse-buttons.md#the-format), and `launcherActions`: `lock`, `turnOffDisplays`, `screenSaver`.
- **Adding an action later** changes only the feature that offers it, as for shortcuts. It shows here unless it acts on the window in front.

## How the host knows the actions

```csharp
// PCCompanion.Core
public sealed record OfferedAction(FeatureAction Action, string Name)
{
    // Whether it acts on the window in front, as Hide app does. Launchers and the command don't offer it.
    public bool OnWindowInFront { get; init; }
}

public interface IFeatureActions
{
    // CanRun and Run as before.

    // Called on the host's UI thread. Whether the action acts on the window in front.
    bool OnWindowInFront(FeatureAction action) => false;
}

public interface IHostActions
{
    // Changed, CanRun and Run as before.

    // On the host's UI thread. Whether a running feature offers the action and can run it now,
    // and it doesn't act on the window in front.
    bool CanRunFromLauncher(FeatureAction action);
}
```

- **The settings app** reads `OnWindowInFront` from each catalog entry's `Actions`, and **the host** asks the running feature. A test checks that both agree for every feature.
- **The host gives `IHostActions` to this feature too,** so Shortcuts & Mouse Buttons is no longer the only one that takes it. Modules still reference only Core, Platform.Windows and Localization.
- **Why an action can't run:** the host tells a feature that isn't running, because it's off or not supported on this PC, from an action the running feature refuses now, for the notification.

## The command

- **`PCCompanion.exe --run <feature> <action> [<target>]`:** the feature's ID, the action's ID and, for an action on a profile or an app, the profile's name or the app's app ID or program, as Copy command writes it. IDs ignore case. A target is matched as its feature matches it: profile names ignore case and spaces around them.
- **`PCCompanion.exe` handles it first,** in `Program.Main`, before Windows App SDK and WinUI start. It opens no window, and doesn't open or show the settings app, also while the settings app is open.
- **Steps:**
  1. Checks the arguments: two or three after `--run`, IDs of ASCII letters and digits of up to 64 characters, and a target of up to 256 characters without control characters. Otherwise it exits with 2.
  2. Finds the host's window. With no host running, it starts one as the settings app does, also through Always run as administrator's task, and waits up to 10 seconds for the pipe.
  3. Lets the host bring a window to the front, with `AllowSetForegroundWindow` for the host's process, as a second host launch does. A program the user just started may do this, and End App, Drop-down Apps, the Clipboard Actions menu and Display Profiles' question need it (spike).
  4. Sends the request through the pipe, waits up to 5 seconds for the answer, and exits.
- **Exit codes:**
  - 0: the host started the action;
  - 1: the host couldn't run it now, and showed a notification that says why;
  - 2: the arguments aren't a command;
  - 3: PC Companion couldn't be reached, or Launcher Actions is off.
- **No console window:** `PCCompanion.exe` is a Windows program, so launchers show nothing while it runs. Scripts read the exit code by waiting for it, such as with `Start-Process -Wait -PassThru` in PowerShell or `start /wait` in Command Prompt.
- **While Launcher Actions is off,** nothing listens, so the command exits with 3 and nothing runs.

## Start entries

- **The settings app writes them:** a shortcut file for each action, in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\PC Companion actions`, which starts `PCCompanion.exe` from PC Companion's folder with the action's `--run` arguments.
- **When:** each time the settings app starts, and after each save that changes the list: this feature or another feature with actions turned on or off, **Show actions in Start** or a feature's switch below it, profiles and Drop-down Apps' apps added, renamed or removed, and PC Companion's language. It compares the list with the folder, so a save that changes nothing writes nothing.
- **Never with admin rights:** the settings app never runs as administrator, so writing in a folder of the user's can't be turned against admin rights. The host, which may run as administrator, never writes the entries.
- **Only that folder:** it writes the entries it lists, replacing older ones, and deletes any other file there, deleting links themselves rather than what they lead to. Turning off the feature or **Show actions in Start** deletes the folder.
- **Names:** "<action> - <feature>", such as "Use Office - Network Profiles", from a string that translations can reorder; this feature's own actions are "Turn off displays - PC Companion". The folder's name is translated too, and the folder is renamed with the language.
  - Characters that Windows doesn't allow in file names, which profile names may have, become spaces. A colon matters most: it saved the entry's name as a file without the `.lnk` extension, with the rest in a hidden part of the file (an alternate data stream), and nothing showed in Start (spike).
  - Names are cut to 80 characters, and a name that's already taken gets " (2)", " (3)" and so on.
- **Each entry** has the PC Companion icon, and its feature's name as its description, which launchers show under the name. Windows' mark that keeps an entry out of Start's **Recently added** (`System.AppUserModel.ExcludeFromShowInNewInstall`) keeps saves from filling it. The user can pin an entry to Start or the taskbar.
- **A stale entry** only remains while the settings app hasn't run since a change it didn't make, such as a hand edit of `settings.json`; running it shows the notification.
- **Uninstalling** deletes the folder of the user who uninstalls, as the host's `--remove-data` deletes that user's data. Other users' folders stay, as their data folders do; their entries then start nothing.

## The host

- **While the feature is on,** it serves a named pipe, `PCCompanion.Actions.<session ID>`, through `RequestPipe`. Only the user's own account and the system can open it, and it rejects programs on other PCs. Low-integrity processes and apps in app containers can't open it (spike).
- **A request** is one JSON message of up to 2 KB, such as `{"feature":"networkProfiles","action":"use","target":"Office"}`. The answer is `{"result":"ran"}`, `"refused"` or `"invalid"`.
- **Checks:** the rules of the command's first step, then, on the host's UI thread, `IHostActions.CanRunFromLauncher`, or this feature's own check for its own actions. Then it runs the action with `IHostActions.Run`, or itself.
- **A refused request** shows a notification, through `IHostNotifications`:
  - a feature that isn't running: "Network Profiles is off, so Use Office can't run.";
  - an action its feature refuses: "Use Office can't run now. Check it on the Network Profiles page.";
  - selecting it opens the feature's page. Windows shows only PC Companion's last notification, so many refusals in a row show one.
- **Requests run one at a time,** in the order they arrive.

## With the other features

- **Shortcuts & Mouse Buttons** gives actions shortcuts and mouse buttons; this feature gives them Start entries and a command, from the same list.
- **Network Profiles and Display Profiles:** the command covers the command parts of [#209](https://github.com/Lazyb0y/pc-companion/issues/209) and [#237](https://github.com/Lazyb0y/pc-companion/issues/237), with `--run` instead of options of their own. Their shortcut parts are Shortcuts & Mouse Buttons'.
- **`PCCompanion.exe --feature <id>`** still opens a feature's page; `--run` never opens a window.
- **Actions on the window in front** stay with Shortcuts & Mouse Buttons, whose page this one links to.

## Threads

- **The command** is a short process of its own: its main thread checks the arguments and waits for the pipe, at most about 15 seconds when it starts the host. It has no window.
- **The host's thread pool** runs the pipe, and reads and checks each request.
- **The host's UI thread** checks whether the action can run and runs it through its feature, as for a shortcut: microseconds, besides the action's own work, which its feature already keeps off the UI thread. It also shows the notification.
- **The wait before Turn off displays and Start the screen saver** is a timer on the host's UI thread that reads the keys' and buttons' state (`GetAsyncKeyState`) every 50 ms, for up to 10 seconds. Then it posts the request to its own window (`ShellActions`), which waits for nothing.
- **The settings app** writes the Start entries on the thread pool, never on its UI thread.
- **Nothing waits for another app:** no message is sent to another program's window.
- **Failures:** a pipe that stops working is logged, and the feature stops for the rest of the session, like a failing `Apply`; the other features keep running.

## Spike results

Two spikes ran in Windows Sandbox on Windows 11 24H2 (build 26100), with a test host that served a pipe like the one above and a command-line client published as PC Companion is, self-contained and ReadyToRun. Input was sent by `SendInput`. They took about 15 minutes in all.

- **Time:** from the client's start to the host's answer, about 60 to 190 ms; the first start was the slowest.
- **The window in front when the request arrived:**

  | Started from | In front |
  |---|---|
  | A test launcher that hid its window, then started the program, or the other way round | The app the user was in |
  | A launcher for Windows that reads Start | The app the user was in |
  | The Run box | The desktop |
  | Start | Start's own window |

- **Bringing a window to the front:** after the client's `AllowSetForegroundWindow`, the host could from every launcher, also 300 ms later from the test launcher, and also while it ran as administrator and the client didn't. Without it, Windows refused, except right after Start.
- **Integrity:** a low-integrity client was refused ("Access denied"), with the host running at medium and at high integrity. A medium-integrity client reached a host running at high integrity.
- **Start:** Start found new entries in a subfolder at once, with Windows Search's indexing off, and ran them with their arguments. So did the launcher that reads Start, which showed each entry's description under its name and found an entry by its description too, below its own result that runs what was typed as a command.
- **A colon in an entry's name** made a file without the `.lnk` extension, with the rest of the name in an alternate data stream, so no entry showed.
- **Not checked yet:** Windows 10; Windows Search with indexing on; how long the real `PCCompanion.exe` takes to start, with Windows App SDK; launchers that run commands; UAC on; Narrator.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Show actions in Start**, on by default, described as "Windows Search and launchers that read Start find them too.";
  - **Features in Start**, an expander with a switch for each feature that offers actions, all on by default, unavailable while **Show actions in Start** is off;
  - **Actions**, a list of the actions available now, each with its name and **Copy command**; with none, it says which features to turn on;
  - a note that actions on the window in front, such as Hide app, need a shortcut, with a button that opens Shortcuts & Mouse Buttons;
  - a **Permissions & data** card.
- **The icon** is drawn as [Icons](../../design/icons.md) says, and reviewed like the others.
- **When the feature is first turned on,** every feature's actions show in Start.
- **The switch on Home** changes only `enabled`, and the settings app then adds or deletes the entries.

### The format

- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log.

```json
"features": {
  "launcherActions": {
    "enabled": true,
    "showInStart": true,
    "hiddenFeatures": [ "endApp" ]
  }
}
```

- `showInStart`: whether the entries are in Start; optional, `true` by default.
- `hiddenFeatures`: the IDs of the features whose actions aren't in Start; optional. The command still runs their actions. Unknown IDs are kept and ignored.

## Not included

- **An extension for a particular launcher:** those that exist need package identity, which needs a signed package ([#19](https://github.com/Lazyb0y/pc-companion/issues/19)), and work in one launcher only.
- **A web address scheme,** such as `pccompanion://`: web pages could start actions, with only the browser's question in between.
- **Actions on the window in front;** see [Actions](#actions).
- **Results worked out as the user types,** such as profiles found by part of their name: launchers search the entries.
- **Turning features on or off,** opening their pages and checking for updates from Start.
- **Choosing single actions for Start;** only whole features.
- **Each feature's own icon** on its entries.
- **Listing the actions from the command line:** the page lists them, with Copy command.
- **Windows' app actions and agent connectors,** which need package identity.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version: named pipes, `AllowSetForegroundWindow`, `IShellLinkW` and `IPropertyStore` for the entries and their mark, and `GetAsyncKeyState`.
- **Start and Windows Search** find entries in Start's subfolders on both. The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Who can ask the host to run an action:** programs running as the user, on this PC and in this session, at medium integrity or higher, through the command or the pipe. Programs on other PCs, other users' programs, low-integrity processes such as browsers' sandboxed parts, and apps in app containers can't (spike).
- **What they can ask:** only an action from the list above, by its feature's and action's IDs, with the name of one of the user's own profiles or listed apps. Never a program, a path, a command line, text or a setting. A target is only compared with its feature's list. Requests are limited to 2 KB and checked again by the host.
- **No new rights:** a program that can ask could already change `settings.json` to give an action a shortcut, or do what the action does itself. Still, the feature is off until the user turns it on, and nothing listens while it's off.
- **A host running as administrator,** with Always run as administrator or with UAC off, takes requests from programs without admin rights. PC Companion asks other apps for text and accessibility information only at its own integrity level; here, it only takes requests, and each action is safe to run for such a program:
  - Network Profiles changes the user's own proxy settings, which any of the user's programs can change, and any program can lock the PC, turn off the displays or start the screen saver;
  - End App only starts choosing: the user clicks the app and confirms, as after its shortcut. While the host runs as administrator, that app may run as administrator too, as after its shortcut;
  - Display Profiles asks whether to keep the new setup and goes back without an answer, and Clipboard Actions opens a menu the user chooses from;
  - Drop-down Apps shows or hides an app the user listed and starts it never with admin rights, and Hide Apps brings back only the windows it minimized;
  - actions on the window in front, which could act on an app running as administrator that the asking program can't reach, aren't offered.
- **Destructive actions:** End App never ends an app without the user's click and confirmation. Lock, Turn off displays and Start the screen saver interrupt the user but lose nothing; any program could do the same.
- **Untrusted input:** the arguments and requests are checked as above and read by a source-generated JSON reader, and a target is never used as a path or passed to a shell.
- **Start entries** are written without admin rights, only in their own folder of the user's Start, deleting links rather than following them. Each starts PC Companion's own program with `--run`. Profile and app names show in Start and Windows Search, as they do in the PC Companion icon's menu; a feature's entries can be left out of Start.
- **Logs:**
  - **Debug entries:** an action ran from the command, by its feature and action IDs; a request was refused, and why.
  - **Never** profile names, app names or app IDs, paths, or which program asked.
  - **Warnings:** a request that isn't valid, and a pipe that stops working; in the settings app's log, an entry that couldn't be written.
- **Network:** none. `PRIVACY.md` gets a line on Launcher Actions.
- **Rights:** the command runs with the rights it was started with, and the entries never ask for admin rights. The host runs each action with its own rights. Turning the feature off stops the pipe and deletes the entries.

## Performance

- **Between requests,** nothing runs: the pipe waits on the thread pool.
- **A request** costs the command's start, 60 to 190 ms for the spike's smaller program, one pipe message, and the action's own work. The Sandbox checks time `PCCompanion.exe`.
- **The settings app** writes entries only when the list changes.

## Accessibility and localization

- **Names** in Start and on the page are in PC Companion's language. Translations can change the separator and the order, and right-to-left languages are laid out right to left.
- **Keyboard and screen readers:** the page's cards work as on the other pages. **Copy command** is named "Copy command for <action>", and announces that the command was copied.
- **Strings** are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests:**
  - **the command's arguments:** how many, IDs, targets, and their quoting in the entries;
  - **requests:** size, fields, unknown IDs, and each refusal with its notification;
  - **availability:** a feature off or not supported, no such profile or app, and actions on the window in front; every feature's `OfferedAction.OnWindowInFront` agrees with its `IFeatureActions`;
  - **Start entries:** the list from the settings; names with each character Windows doesn't allow, a colon in particular, long names, names already taken and right-to-left names; and what's written or deleted when the list changes;
  - **the wait** before Turn off displays and Start the screen saver;
  - **the settings:** defaults, the spec's example, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/LauncherActions.ps1`), with a test network profile and test windows:
  - **the entries:** turning the feature on writes them, with their names, arguments, description, icon and mark; switching a feature off deletes its entries; renaming the profile renames its entry; in Arabic, they're named in Arabic; turning the feature off deletes the folder;
  - **the command:** using the test profile changes the proxy settings and exits with 0; an unknown profile exits with 1 and shows the notification; wrong arguments exit with 2; with the feature off, it exits with 3; with no host running, it starts the host and runs; a low-integrity copy is refused, and a medium-integrity copy reaches the host, which runs as administrator in the Sandbox; the time from start to exit is logged;
  - **Start:** the Windows key, an entry's name and Enter run its action;
  - **the front:** End App's chooser comes to the front from Start and from the Run box, and Esc ends it;
  - **the page** in English and Arabic, light and dark, with screenshots, and the App group's layout checks;
  - no feature holds the host's UI thread, as every group checks;
  - **never** Lock, Turn off displays or Start the screen saver, which would stop the checks.
- **By hand:**
  - Windows 10, and Windows Search with indexing on;
  - launchers that read Start, and launchers that run commands;
  - UAC on, with Always run as administrator;
  - Lock, Turn off displays and Start the screen saver from Start;
  - that new entries don't show in **Recently added**;
  - a second user of an all-users install, and uninstalling;
  - Narrator on the page.
