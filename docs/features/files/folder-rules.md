# Folder Rules

Rules that act on files as they arrive in a folder, such as Downloads: move, copy or rename them, sort them into subfolders, or move them to the Recycle Bin, with a preview before a rule is trusted and an undo afterwards.

- Module: Files (`PCCompanion.Modules.Files`). Settings section: `features.folderRules`. Issue: [#43](https://github.com/Lazyb0y/pc-companion/issues/43).
- Inspired by macOS Folder Actions and folder automation tools for macOS, and by automation built on file change notifications in Linux.
- Off until the user turns it on.

## Behavior

- The user chooses folders to watch and gives each folder a list of rules, in order. A rule has a name, conditions and an action.
- When a file arrives in a watched folder, by a download, a save, a copy, a move or a rename, and has finished arriving, PC Companion goes through the folder's rules in order. The first rule whose conditions all match acts on the file, and the rules after it don't. A file that no rule matches stays where it is.
- Rules act on the files directly in the folder, not on subfolders, their files or the folders themselves; subfolders are [#224](https://github.com/Lazyb0y/pc-companion/issues/224).
- Adding a folder, turning a folder or a rule on, or saving a rule doesn't act on the files already there. **Run now** applies a folder's rules to the files there now, after a preview; see [Run now](#run-now).
- Files that arrive while PC Companion isn't running are handled when it next starts: the files whose change time is later than when PC Companion last looked at the folder.
- A folder's rules act on a file once. A file that a rule renamed isn't renamed again, and a file the user puts back, with **Undo** or by hand, stays where it is. Another folder's rules still handle it when it arrives there, so two folders whose rules move files to each other can't pass a file back and forth.
- Everything the rules do goes into the [history](#history-and-undo), where each action can be undone.

## Compared with other systems

- **Windows** has no folder rules. Storage Sense deletes files from Downloads that haven't been opened for a while, on a schedule, with no preview and no list of what it deleted, and on most large drives Windows doesn't record when a file was last opened. Folder Rules acts only as the user's rules say, shows the files a rule would act on before it's saved, and keeps a history with undo.
- **macOS Folder Actions** run a script for each new item, with no conditions, preview, log or undo, and often before a download or copy has finished. **Folder automations in macOS Tahoe's Shortcuts** trigger on new files, but every condition and action is built by hand. Folder Rules has its conditions and actions ready to choose, and waits until a file has finished arriving.
- **Folder automation tools for macOS** are the most complete: ordered rules, a live preview of each condition, and an undo for moves and renames. They're paid and for macOS only. Folder Rules brings the same model to Windows, undoes copies too, and its history also says why a file was skipped.
- **KDE Plasma and GNOME** have nothing built in. Users write scripts for file change tools or systemd path units, which don't wait for a file to finish or stop rules from acting on their own results.
- **Command-line organizers** run rules from a file, by hand or on a schedule, with a dry run, but don't watch folders, have no undo, and act on downloads that haven't finished.
- **File organizers for Windows** watch folders, with many conditions and actions, but most have no undo or preview, act on files still downloading or twice on the same file, delete without the Recycle Bin, or run programs on downloaded files. Folder Rules never deletes without the Recycle Bin, never replaces a file, never runs anything, acts on a file once, and keeps the Mark of the Web.
- **Where a file came from:** only tools for macOS have a condition on the site a file was downloaded from. Folder Rules reads it from Windows' Mark of the Web.
- **Cloud files:** most tools download files kept online only when they read or move them. Folder Rules leaves them alone.

## Rules

- A rule has a **name**, up to 60 characters, shown in the history; **conditions**; an **action**; and a switch, so a rule can be turned off without deleting it.
- A rule needs at least one condition, so that no rule acts on every file by mistake. All of a rule's conditions must match. To act on files that match either of two conditions, the user adds two rules.
- Up to 10 folders and 50 rules in all. In a list edited by hand, the folders and rules after these are left out, with a warning in the log, and the page says so on each.
- A rule that can't be used, such as one with a destination that isn't allowed or a name pattern that makes no valid file name, is left out, with a warning in the log that gives its folder's and its own number. The page shows the problem on the rule.

## Conditions

- **Name:** one or more patterns, separated by `;`, such as `invoice*;receipt*`, where `*` stands for any characters and `?` for one. The name matches when it matches any of them. The patterns match the whole name, extension included, and case is ignored.
- **Type:** a list of extensions, such as `pdf; docx`. **Kind** presets fill it in: Documents, Pictures, Videos, Music, Archives and Installers. The user sees and can change every extension a preset adds, so nothing depends on what the installed apps registered.
- **Size:** larger than, smaller than, or both, in KB, MB or GB.
- **Date:** the date the file was created or last modified, more than or less than a number of days ago. Not the date it was last opened, which Windows doesn't keep up to date on most large drives.
- **Downloaded from:** one or more site patterns, such as `*.example.com`, matched against the site in the file's Mark of the Web: the address it was downloaded from, or else the page it was downloaded on. Files without a mark, or whose mark names no site, such as downloads from a private window, don't match.

## Actions

- **Move to a folder** and **Copy to a folder:** the **destination** is a folder that exists, and may sort the file into subfolders made from the file, such as `D:\Invoices\{year}\{month}` or `D:\Downloads by type\{extension}`. The subfolders are created as needed. Optionally, the file gets a **new name** on the way, as with Rename.
- **Rename:** the **new name** is a pattern for the name without the extension, such as `{date} {name}`; the file keeps its extension and its folder.
- **Move to the Recycle Bin.**
- Parts in braces are replaced with the file's own values:
  - `{name}`, the name without the extension, and `{extension}`, the extension without the dot;
  - `{year}`, `{month}`, `{day}`, and `{date}` as `yyyy-MM-dd`, from the date the file was last modified.
  - Numbers are written with ASCII digits in every language, so names sort the same way everywhere.
  - A part that's empty for a file, such as `{extension}` for a file without one, leaves its subfolder out of the destination.
- **When a file with the same name is there:** **Add a number**, as File Explorer does, such as `report (2).pdf`, or **Skip**. A file is never replaced: the move or copy fails when a file of that name appears in the meantime, and the next number is tried.
- A Copy or Move whose destination is the rule's own folder isn't allowed; Rename does that. A destination may be another watched folder, whose rules then handle the file when it arrives there.

## Waiting until a file has arrived

- A file is handled only after it hasn't changed for a few seconds, a wait that starts again with each change, and when no other program has it open for writing: PC Companion opens it without letting others write to it, and when that fails, it tries again later, waiting longer each time. The spike sets the waiting time.
- Files that browsers and other programs are still writing are skipped, by their names: `.crdownload` (Chromium browsers), `.part` (Firefox), `.partial`, `.download`, `.opdownload` and `.tmp`, and Office's `~$` lock files. When the download is done, the browser renames the file to its final name, which is an arrival.
- Empty files are skipped too. While Firefox downloads, it keeps an empty file under the final name and writes to a `.part` file with another name, which takes the empty file's place when it's done.

## What rules never touch

- **Links:** symbolic links and junctions are skipped, and a watched folder or a destination can't be one, or be reached through one. A rule never acts on the file a link points to.
- **Files only in the cloud:** files that a cloud storage app, such as OneDrive, keeps online only are skipped, so PC Companion never downloads them.
- **Hidden and system files**, such as `desktop.ini`.
- **Files in use:** a file that another program has open, so it can't be moved, is tried again later, and after a few minutes left where it is, with an entry in the history.
- **Folders that Windows protects:** with Controlled folder access on in Windows Security, Windows blocks apps it doesn't trust from changing files in Documents, Pictures, Music, Videos and Favorites, and in folders the user added. It trusts apps by how widely they're used and their reputation, not by a signature alone. A move it blocks stays undone, the history says Windows blocked it, and the page explains how to allow PC Companion.

## Which folders

- Any folder whose files the user can change can be watched or be a destination: on the PC's own drives, including the root of a drive; on removable drives, such as USB drives and memory cards; and on network locations, by drive letter or by a path such as `\\server\share\Scans`. See [Network folders and removable drives](#network-folders-and-removable-drives).
- **Not allowed**, because rules there could only fail or break something:
  - the Windows folder and the Program Files folders, which only administrators can change, while Folder Rules never uses admin rights;
  - hidden and system folders, such as the Recycle Bin's own folders and `System Volume Information`;
  - PC Companion's data folder, `%LOCALAPPDATA%\PCCompanion`, where a rule could move PC Companion's own settings.
- **`AppData` and `ProgramData`** are allowed, with a warning on the page: programs keep their own files there, and moving one can break a program.
- A destination may be another watched folder, whose rules then handle the file when it arrives there.

## Network folders and removable drives

- **Network folders:** what Windows reports about changes in a network folder depends on the server: some report every change, some only part of them, and some none, and the watch ends without an error when the connection drops. So besides the watch, PC Companion looks at each network folder again every few minutes. When a folder can't be reached, it tries again every minute until it can, and then looks at the whole folder.
- **Removable drives:** a watch keeps the drive in use, so Windows couldn't eject it. When Windows asks to eject a drive, PC Companion lets go of its watches on it right away and stops a copy to or from it, which is tried again later; when the drive comes back, it watches it again and looks at the whole folder. A drive pulled out without ejecting ends the watch with an error, which is handled the same way. Folders are found by their path, so a drive that comes back with another letter isn't watched until the folder is chosen again, and the page says it can't be found.
- **No Recycle Bin:** network locations and most removable drives have none, and PC Companion never deletes without it. A rule that moves files to the Recycle Bin leaves files on such a drive where they are, with an entry in the history, and the rule editor says so when the folder is on one.
- **No Mark of the Web:** drives without NTFS or ReFS, such as most memory cards and USB drives, and some network servers, can't keep a file's Mark of the Web. A rule doesn't move or copy a file that has one to such a drive: the file stays, with an entry in the history that says why. Files without a mark move freely. Whether a drive keeps it is read from Windows (`GetVolumeInformation` and `FILE_NAMED_STREAMS`).
- **No lasting file IDs:** FAT and exFAT drives and some servers have none, so there PC Companion recognizes a file it already handled by its name, size and last modified time only, and a file that arrived while it wasn't running by the time it was created or last modified, since those drives keep no change time.

## Mark of the Web

- Windows marks files from the internet with their zone, so that Windows and apps warn before opening them. PC Companion keeps the mark: moves on the same drive keep the file as it is, copies and moves to another drive copy the mark with it, and a file with a mark never goes to a drive that can't keep it. It never removes or changes the mark, and never opens or runs the files it handles.

## Run now

- Each folder on the page has **Run now**, which lists the files there now that its rules would act on, with what each rule would do, such as "Move to D:\Invoices\2026", and a box for each file, all checked. Files the folder's rules already acted on aren't listed.
- **Run** applies the rules to the checked files. Each file is checked again just before, so a file that changed or went away in the meantime is skipped.
- The rule editor shows the files there now for the rule being edited, as the user changes it, before saving, so a rule can be tried on real files without changing anything. For each file, it shows whether each condition matches, with the file's own value, such as its size, so the user sees why a file is or isn't included, and the new name a rename would give it.

## History and undo

- The history lists what the rules did, newest first: the time, the rule, the file's name and what happened, such as moved to a folder, renamed, copied, moved to the Recycle Bin, or skipped, with why, such as a file with that name already there.
- **Undo** puts a file back: a moved or renamed file goes back to its old place and name, and a copy goes to the Recycle Bin. The host first checks that the file is still where the rule put it, unchanged, and that its old place is free; otherwise the page says why and nothing changes. A file moved to the Recycle Bin is put back with the Recycle Bin's **Restore**, which **Open Recycle Bin** opens.
- The history keeps the last 30 days, up to 1,000 entries, in `folder-rules.json` in the data folder, with when PC Companion last looked at each folder. Only the host writes it. **Clear history** empties it.
- The history and Undo are available while Folder Rules is on.

## Running as administrator

- No setting may make PC Companion read or write files as administrator; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs). A program without admin rights could otherwise change the settings file, or put a link in a watched folder, to have files moved where only administrators may write.
- So while the host runs as administrator after a UAC prompt, Folder Rules pauses, and the page says why. With UAC off, every program runs with the user's full rights, and Folder Rules works. Running it in a copy of the host without admin rights is [#225](https://github.com/Lazyb0y/pc-companion/issues/225).

## Threads

- **The host's UI thread** runs `Apply`, which reads the section, checks the folders and rules, and starts or stops the watches, without touching the disk: whether a folder exists and what its drive is like are read on the feature's thread.
- **The watch:** a `ReadDirectoryChangesW` watch for each folder, through .NET's `FileSystemWatcher`, whose events come on the thread pool. Its events only say that something changed; PC Companion then looks at the file itself. When Windows reports that changes were lost, such as after many at once, or that the watch failed, PC Companion starts the watch again and looks at the whole folder, as it does at start and when the PC wakes from sleep.
- A folder that can't be watched, such as one that was deleted or renamed, or one on a drive or network location that's gone, shows the problem on the page, with a warning in the log. PC Companion watches it again when it comes back: when a removable drive arrives, within a minute for a network folder, and otherwise when the settings change and when it next starts.
- **Drives and power:** the host's UI thread gets Windows' device and power messages through a hidden window of the feature's (`RegisterDeviceNotification` for each removable drive's volume). When Windows asks to eject a drive, it stops the watches on that drive at once and tells the feature's thread to stop a copy to or from it; when a drive arrives or the PC wakes from sleep, it tells the feature's thread to look at the folders again. The check of network folders every few minutes is a timer there too. None of these waits for the disk: they only pass work on.
- **The feature's own thread** handles the files one at a time, in the order they arrived, so two rules never act on the same file at once. It's an STA thread, since moving a file to the Recycle Bin needs one (`IFileOperation`). It runs at background priority, so the disk stays available to the programs in front.
- **The thread pool** serves the settings app's requests, through a named pipe, and passes Run now and Undo to the feature's thread; see [The settings app and the host](#the-settings-app-and-the-host).
- **Stopping** removes the watches and ends the thread after the file it's handling.
- **Failures:** the feature's thread and the watches' callbacks run inside the feature's error boundary. A failure there is logged and stops Folder Rules until the host restarts. A file that can't be moved isn't such a failure: it's an entry in the history.

## Moving and copying

- Files are moved with `MoveFileEx` and copied with `CopyFile2`, without replacing a file that's there, so a file that appears at the last moment is never overwritten.
- A move to another drive copies the file and deletes the original only once the copy is complete.
- Files go to the Recycle Bin with `IFileOperation`. Before Windows deletes a file, it tells PC Companion whether the file will go to the Recycle Bin (`IFileOperationProgressSink::PreDeleteItem` with `TSF_DELETE_RECYCLE_IF_POSSIBLE`); when it wouldn't, such as a file larger than the Recycle Bin holds, PC Companion stops the delete, and the file stays.
- New names are checked before they're used: no `\`, `/` or other characters Windows doesn't allow, no names Windows reserves, such as `CON` or `NUL`, no dot or space at the end, and at most 255 characters. Values from the file can't add a folder to a destination, and the final path must be inside the destination.

## The settings app and the host

- The page needs what only the host knows, the history, and asks the host to act, for Run now and Undo, so that only the host changes files and knows what it changed. The host serves both through a named pipe that only the user's own account and the system can open, as Hot Corners' recent results, and that rejects remote clients. Unlike that pipe, this one reads a request: the history, Run now for a folder with the files chosen, or Undo for a history entry.
- The host checks every request: the folder must be a watched folder in its settings, each file must be directly in it, and an entry to undo must be in its history, so a request can't make it touch other files. While the host runs as administrator, Folder Rules is paused and serves no pipe.
- The host recognizes a file its rules already handled when it comes back, by its file ID, or, after a move between drives, by its name, size and last modified time, and leaves it.

## Settings

- **Feature page:** the on/off switch; **Folders**, a list with **Add folder**, which offers Downloads, Desktop, Documents and Pictures and **Browse**, and for each folder its switch, **Run now**, **Rules** with **Add rule**, and **More options** with **Remove**; for each rule its switch, **Edit**, and **More options** with **Move up**, **Move down** and **Delete**; **History** with **Undo** on each entry, **Open Recycle Bin** and **Clear history**; and a **Permissions & data** card.
- **Add rule** starts from an empty rule or from a template, which opens in the rule editor with the files it would act on: **Sort by type**, into a subfolder for each extension; **Sort by month**, into `{year}\{month}`; **Installers to an Installers folder**; and **Documents to the Documents folder**.
- **The rule editor** is a dialog with **Name**, the conditions, each with a switch, **Action** and its destination or new name, **When a file with the same name is there**, and the files there now that the rule would act on.
- Problems show below their box, and **Save** is available only when the rule has none. **Delete** and **Remove** ask first.
- When the feature is first turned on, there are no folders.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns Folder Rules off with a warning in the log.

```json
"features": {
  "folderRules": {
    "enabled": true,
    "folders": [
      {
        "path": "C:\\Users\\Ana\\Downloads",
        "enabled": true,
        "rules": [
          {
            "name": "Invoices",
            "enabled": true,
            "names": ["invoice*", "receipt*"],
            "extensions": ["pdf"],
            "downloadedFrom": ["*.billing.example.com"],
            "action": "Move",
            "destination": "C:\\Users\\Ana\\Documents\\Invoices\\{year}",
            "newName": "{date} {name}",
            "ifExists": "AddNumber"
          },
          {
            "name": "Installers",
            "enabled": true,
            "extensions": ["exe", "msi", "msix"],
            "action": "Move",
            "destination": "C:\\Users\\Ana\\Downloads\\Installers",
            "ifExists": "Skip"
          }
        ]
      }
    ]
  }
}
```

## Not included

- Deleting files without the Recycle Bin.
- Running programs or scripts, and opening files, from a rule. A file downloaded by a web page could then run code without the user opening it.
- Rules for folders themselves.
- Later, each in its own issue:
  - rules on a schedule, such as a daily check that moves files to the Recycle Bin once they're 30 days old ([#223](https://github.com/Lazyb0y/pc-companion/issues/223)). A file that arrives isn't old yet, so for now a rule with a Date condition acts on the files already there through Run now;
  - subfolders of a watched folder ([#224](https://github.com/Lazyb0y/pc-companion/issues/224));
  - Folder Rules while the host runs as administrator after a UAC prompt ([#225](https://github.com/Lazyb0y/pc-companion/issues/225));
  - a mode that asks before each action ([#226](https://github.com/Lazyb0y/pc-companion/issues/226)); for now, the rule editor's list and Run now show what a rule does before it acts on its own;
  - extracting archives, passing the Mark of the Web on to the files ([#227](https://github.com/Lazyb0y/pc-companion/issues/227));
  - conditions on a file's contents, which would also download files kept online only ([#228](https://github.com/Lazyb0y/pc-companion/issues/228));
  - undoing a whole run, or everything in a period ([#229](https://github.com/Lazyb0y/pc-companion/issues/229));
  - replacing the file already there, or recycling a duplicate, when a name is taken ([#230](https://github.com/Lazyb0y/pc-companion/issues/230));
  - more parts in new names, such as a counter and the date a photo was taken ([#231](https://github.com/Lazyb0y/pc-companion/issues/231));
  - several actions in one rule ([#232](https://github.com/Lazyb0y/pc-companion/issues/232)).
- Starting when a drive or device is connected, which is [#46](https://github.com/Lazyb0y/pc-companion/issues/46). A watched folder on a removable drive is watched again when the drive comes back, but nothing runs from the drive.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- Every API is documented and in every supported version: `ReadDirectoryChangesW`, `MoveFileEx`, `CopyFile2`, `IFileOperation`, `GetFileInformationByHandleEx` for file IDs, `GetVolumeInformation`, and `RegisterDeviceNotification`.

## Security and privacy

- **Changes:** files in the watched folders and the destinations the user chose, only by the rules the user made or by Run now; nothing else. Files are never deleted without the Recycle Bin and never replaced.
- **Reads:** the names, sizes, dates and attributes of the files in the watched folders; never their contents. For rules with **Downloaded from**, the file's Mark of the Web, which holds the addresses it was downloaded from and on; they're compared with the rule's patterns and never stored, logged or shown.
- **Untrusted files:** anything can arrive in a watched folder, including files named by a web page, so names are checked before they're used, links are skipped, and PC Companion never opens or runs a file.
- **Settings:** the folders and rules are in `settings.json`. Any program running as the user can change that file, but it could also move the user's files itself, so this crosses no boundary while PC Companion runs as the user. While it runs as administrator, Folder Rules pauses.
- **The pipe:** only the user's own account and the system can open it, it rejects remote clients, and the host acts only on watched folders and the files directly in them.
- **History:** file names and folders, which can be personal, kept on this PC for 30 days at most. **Clear history** empties it, and uninstalling removes it with the rest of the data folder. `PRIVACY.md` gets a line on Folder Rules.
- **Network:** PC Companion connects to no service of its own. Watched folders and destinations on network locations, which the user chose, are read and changed through Windows' file sharing like any file there, and network folders are looked at every few minutes as well as after changes. It skips files kept online only, so it never has a cloud storage app download them.
- **Logs:** Debug entries when the settings are applied, with how many folders and rules can be used, and when a rule acts, by folder and rule number. Warnings when a folder can't be watched, a rule can't be used, or Windows refuses a move, with its error code. Never file names, folder paths or rule names: the history has them for the user.
- **Rights:** runs without admin rights, and never touches files with them.
- **Failures:** a file that can't be moved stays where it was, with an entry in the history.

## Performance

- Between arrivals nothing runs but the watches, and, only for network folders, a look at the folder every few minutes, or every minute while it can't be reached. Folders on the PC's own drives and removable drives aren't polled.
- At start, and after Windows reports lost changes, PC Companion lists each watched folder's files on the feature's thread, reading only names, sizes, dates and attributes. The spike measures how long a folder of 10,000 files takes.
- Files are handled at background priority, one at a time.

## Accessibility and localization

- The page and the rule editor work with the keyboard alone, and keyboard focus stays on a folder or rule when the list changes. Lists, boxes and buttons have labels for screen readers, buttons carry the folder's or rule's name, and problems below a box are announced.
- Paths and patterns are shown left to right in every language.
- Dates in the history follow the user's regional settings. The parts in braces are the same in every language.
- The strings are translated like the other pages', marked "Needs native review."

## Spike

A spike in Windows Sandbox checks, before the code:

- that moving a file into a folder updates its change time, for files that arrived while PC Companion wasn't running;
- downloads in Edge, and in Chrome and Firefox if they can be installed: the names they use while downloading, the renames, and that a file moved right after its download keeps its Mark of the Web;
- the waiting time before a file counts as arrived;
- the Recycle Bin: `PreDeleteItem` for a file too large for it and for a drive without one;
- what `FileSystemWatcher` reports for many files at once, and the time to list a folder of 10,000 files;
- the sites Edge writes in the Mark of the Web, in a normal and a private window;
- what a move blocked by Controlled folder access returns, and whether PC Companion can tell that it was the cause;
- a network folder through a share on the same PC (`\\localhost\…`): which changes the watch reports, what happens when the share goes away and comes back, and what the drive reports for streams and file IDs;
- an exFAT and a FAT32 drive, made from a virtual disk: file IDs and dates after moves, and that `FILE_NAMED_STREAMS` is missing;
- the eject request for a removable drive, which Windows Sandbox can't attach, is checked by hand.

## Testing

- **Unit tests** with a fake file system and a fake clock: conditions, patterns and the order of rules; the sites read from a Mark of the Web, including malformed ones; new names and destinations, with characters and names Windows doesn't allow, values that try to add a folder, and a rename that changes only the case; conflicts; waiting for a file to arrive, the skipped names and empty files; a file handled once, one put back, and two folders whose rules move files to each other; the limits; and the pipe's checks of requests.
- **Sandbox checks** (`scripts/sandbox/groups/FolderRules.ps1`), on folders made for the test: files written, copied, moved and renamed into a watched folder are moved, copied, renamed and sorted as the rules say; a file written slowly, one named `.crdownload` and renamed later, and one held open are handled only once done; a conflict adds a number or skips; a link is skipped; a file goes to the Recycle Bin; the Mark of the Web stays after a move and a copy; Run now and the rule editor list the files; Undo puts a file back and it stays; a download in Edge from a server on the loopback address arrives, matches **Downloaded from**, and is moved; a folder on a share on the same PC is watched, checked again after a change the watch missed, and watched again after the share comes back; on an exFAT virtual disk, a file with a Mark of the Web stays, one without moves, and a Recycle Bin rule leaves files there; the Windows folder, a hidden system folder and PC Companion's data folder are refused, and `AppData` shows its warning.
- **By hand:** OneDrive files kept online only; Chrome and Firefox downloads; a USB drive and a memory card: ejecting while watched and during a copy, pulling one out, and putting it back; a folder on a NAS or another PC; Always run as administrator with UAC on, where Folder Rules pauses; a screen reader and a right-to-left language on the page.
