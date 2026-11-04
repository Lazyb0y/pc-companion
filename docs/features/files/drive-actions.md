# Drive Actions

Do something useful when a drive is connected: import a camera card's photos into Pictures, sorted by month, back up folders to a USB drive, or open a folder, and say when the drive can be removed. Nothing on the drive is ever run.

- Module: Files (`PCCompanion.Modules.Files`). Settings section: `features.driveActions`. Issue: [#46](https://github.com/Lazyb0y/pc-companion/issues/46).
- Inspired by device actions in Linux desktops and by backups that start when the backup drive is connected, in macOS and Linux.
- Off until the user turns it on.

## Behavior

- The user adds the drives PC Companion should know, and gives each a list of actions: **Import from the drive**, **Back up to the drive** and **Open a folder**, with **Eject when done**. **Camera cards** stands for any memory card or drive with photos from a camera.
- When a drive the user added is connected, PC Companion asks first, by a notification, or acts at once, as chosen for that drive. It never acts while the PC is locked, and asks first, whatever the choice, for a drive that was connected while the PC was locked or before PC Companion started.
- A drive is recognized by an ID that PC Companion writes on it and by the drive's own serial numbers, never by its name; see [Recognizing a drive](#recognizing-a-drive). Backups go only to a drive recognized this way.
- When the actions are done, a notification says what they did, and that the drive can be removed, or that it was ejected.
- Nothing on the drive is opened or run, and nothing there is deleted or changed, except PC Companion's own ID file, its backups and its unfinished copies.
- Everything the actions do goes into the [history](#history-and-undo), where imports can be undone.

## Compared with other systems

- **Windows AutoPlay** chooses what happens for each kind of media, such as removable drives or memory cards, not for each drive, and only opens an app or a folder. The Photos app's import is started by hand, imports everything again for some users, and its **Delete items after import** deletes from the device for good. File History backs up to an external drive every hour, not when it's connected, and finds its drive by its letter; Windows Backup backs up only to the cloud. Drive Actions acts for each drive, imports and backs up as soon as the drive is connected, imports each photo once, never deletes from the card, and checks that a drive is the user's before it copies files to it.
- **Windows stopped AutoRun** on USB drives in Windows 7, and in updates for older versions in 2009 to 2011, because nothing shows where a drive's `autorun.inf` came from. Drive Actions never reads it and never runs anything from a drive.
- **macOS:** Image Capture and Photos remember, for each device, which app opens. A card the camera formats counts as a new device, and their **Delete after import** has lost photos after imports that failed. Shortcuts in macOS Tahoe can run an automation when any drive or a chosen drive is connected, at once or after a confirmation, but every step is built by hand. Time Machine asks once whether to use a new drive, offers encryption, and warns when a drive that claims to be the backup drive isn't the same one, a check it gained after a spoofed network volume could receive backups (CVE-2011-3462). Drive Actions has its actions ready to choose, recognizes camera cards whatever their format, warns about a look-alike drive, and recommends BitLocker for backup drives.
- **Backup apps for macOS** that start when the drive is connected compare the drive's name and volume ID, and refuse two drives with the same ID; one wrote its backups to the startup disk, in a leftover folder where the drive used to be. Drive Actions checks, before each copy, that its destination is on the recognized drive.
- **GNOME, Xfce, Cinnamon and LXQt** choose by kind of media, such as photos or software, never by drive; GNOME's **Do nothing** for photos is ignored by its photo app, users report. They ask before running software found on a medium and ignore Windows' `autorun.inf`. **KDE Plasma** matches devices in detail, but its actions run only when chosen from its device list, a request open since 2010, and its device list opens over full-screen apps. Drive Actions acts on its own when the user wants it to, uses Windows' notifications, which follow Do not disturb, and runs nothing from the drive.
- **udev rules and backup tools for Linux** find the drive by its file system's UUID or label, which udisks' own documentation calls easy to fake. Photo importers for Linux remember imported files by name, size and time, never overwrite, and the most complete one deliberately never deletes from the card; others have deleted photos that were never imported.
- **ChromeOS** shows a notification and opens Files; its photo import was removed in 2022.
- **Sync and backup tools for Windows** that start when a drive is connected mostly find it by its label, so they would copy to any drive with that name; one documented recipe also runs a file kept on the drive. Photo importers for Windows open their import window when a card is detected, also for phones, users report, or copy only new photos, but don't show which count as new. Drive Actions never trusts a name, never runs a file from the drive, and shows what an import would copy, with files imported before unchecked.

## Drives

### Which drives count

- **Drives with a drive letter** that Windows lets the user eject: USB flash drives, memory cards in a card reader, and USB and Thunderbolt disks, which Windows calls fixed drives. They're recognized by Windows' removable flag on the drive (`DRIVE_REMOVABLE`) or on its device or a device it's connected through (`CM_DEVCAP_REMOVABLE`).
- **Virtual disks** (VHD and VHDX) that the user attaches, which Windows reports as file-backed virtual disks.
- **Not counted:**
  - the PC's own disks, including the Windows drive;
  - network drives and folders on network locations;
  - CDs, DVDs and RAM disks;
  - drives without a letter, such as those mounted in a folder;
  - phones and cameras that connect without a drive letter, through Windows Portable Devices (MTP or PTP), which need a different Windows API, the phone unlocked and **File transfer** or **Trust** chosen on it. Windows' Photos app imports from them.
- A drive whose letter appears and goes again within a second, as when Windows gives a new volume a letter before the user's tool gives it its own, is handled once it keeps one; see [Spike results](#spike-results).

### Adding a drive

- **Add a drive** lists the drives that are connected and count, each with its name in Windows (its label), letter, size and kind: **USB drive**, **Memory card**, **External disk** or **Virtual disk**. Drives already in the list, and drives PC Companion can't write to, such as a card with its lock switch on, say why they can't be added.
- Adding a drive writes PC Companion's ID file on it, and records its serial numbers; see [Recognizing a drive](#recognizing-a-drive). The drive's name in the list starts as its label, or as its kind when it has none, and can be changed: up to 60 characters, and no two drives with the same name, ignoring case.
- **Camera cards** is added from the same list, as its first item, and stands for any drive with a camera's photos, without an ID file; see [Camera cards](#camera-cards).
- Up to 10 drives, each with up to 10 actions. In a list edited by hand, the drives and actions after these are left out, with a warning in the log, and the page says so on each.

### Recognizing a drive

- A drive's name, its label, can be anything and is often the same on many drives, and every number the drive carries can be copied to another drive with free tools; see [Security and privacy](#security-and-privacy). So PC Companion recognizes a drive by three things, which must all match what it recorded when the drive was added:
  - **its ID file:** a hidden system file at the drive's root, `.pccompanion-drive`, with a random 128-bit ID that PC Companion wrote there;
  - **the volume's serial number**, which Windows gives the drive when it's formatted (`GetVolumeInformation`);
  - **the device's hardware serial number,** when it has one, read from the device (`IOCTL_STORAGE_QUERY_PROPERTY`); USB drives usually have one, virtual disks and some cheap drives don't, and a card reader reports its own, not the card's.
- **What this means:**
  - **A reformatted drive** has a new volume serial and no ID file, so it's a new drive and must be added again.
  - **On another PC,** the drive is recognized once it's added there too; that PC writes no second ID file, and records the same one.
  - **Two identical drives** have different IDs and serials.
  - **A look-alike drive** with the same name isn't recognized. To pass as the drive, another drive needs a copy of the ID file and the same serial numbers, which only someone who has had the drive in hand can make, and who could read the drive itself. Only BitLocker To Go keeps the files on a drive from someone who has it; see [Security and privacy](#security-and-privacy).
- **A drive with the ID file whose serials don't match**, such as a copy of the drive or the same card in another card reader, isn't recognized, nothing is copied to or from it, and a notification says so: "A drive with Backup drive's ID was connected, but it isn't the same drive. Nothing was copied." The page shows it with **Use this drive**, which records its serials after the user confirms.
- **Serial numbers are kept hashed,** as Display Profiles keeps a display's serial number: the first 16 hexadecimal digits of the SHA-256 of the number and the text `PCCompanion.DriveActions`. They're never shown or logged.
- **The ID file** holds only the ID. Deleting it, or formatting the drive, makes the drive unknown to PC Companion; the page then shows it as not recognized, with **Add again**.
- The volume's GUID path isn't used: it comes from data on the disk, which a copy of the disk has too, as the spike showed.

### Camera cards

- **Camera cards** matches any drive that counts and has a folder named `DCIM` at its root, the folder cameras and phones keep their photos in, as Windows' AutoPlay and GNOME recognize them. A `DCIM` that's a link doesn't count.
- Cameras give a card a new volume serial each time they format it, so a card can't be recognized from one time to the next; Camera cards doesn't try.
- Camera cards can only import from the drive and open a folder: files only come to the PC, so a look-alike card can't take any.
- A drive that's in the list by itself is handled as that drive, also when it has a `DCIM` folder.

## Actions

A drive's actions run in their order, one at a time. A drive with no action can still eject when done.

### Import from the drive

- **From:** a folder on the drive, `DCIM` by default, with its subfolders, such as `DCIM\100CANON` and `DCIM\101CANON`.
- **Types:** a list of extensions, as Folder Rules' **Type** condition, with its **Kind** presets and one more, **Camera files**, the default: `jpg`, `jpeg`, `heic`, `heif`, `png`, `dng`, `cr2`, `cr3`, `crw`, `nef`, `nrw`, `arw`, `srf`, `sr2`, `raf`, `orf`, `rw2`, `pef`, `srw`, `x3f`, `3fr`, `iiq`, `mp4`, `mov`, `m4v`, `avi`, `mts`, `m2ts`, `3gp` and `mpg`.
- **To:** a folder on the PC, which may sort the files into subfolders made from each file, with Folder Rules' parts in braces, such as `C:\Users\Ana\Pictures\{year}\{month}`, the default under the user's Pictures folder. The dates are the file's last modified date, which cameras set to when the photo was taken. The date a photo was taken, from the photo itself, is [#231](https://github.com/Lazyb0y/pc-companion/issues/231).
- **When a file with that name is there:** **Add a number**, the default, or **Skip**, as in Folder Rules. A file is never replaced.
- **Each file is imported once:** PC Companion remembers, for each drive in the list, the name, size and last modified time of each file it imported, and a file with the same name and size and a time less than an hour apart counts as imported before. FAT and exFAT drives, which most cards use, keep local times, which Windows reads an hour apart after a change to or from daylight saving time. A file imported before isn't imported again, also from another card, unless the user checks it in **Run now**.
- **Copies only:** nothing on the drive is moved, deleted or changed, so the card stays as it was until the camera formats it. A file's Mark of the Web, on a drive that keeps it, is copied with it.
- **Skipped:** links, hidden and system files and folders, and empty files.
- **Folders that Windows protects:** with Controlled folder access on, a copy into Pictures that Windows blocks is an entry in the history that says so, and the page explains how to allow PC Companion, as for Folder Rules.
- **Run now** lists the files the import would copy, with the destination of each, those imported before unchecked, as Folder Rules' Run now does.

### Back up to the drive

- **Folders:** one or more folders on the PC, up to 10, each copied with its subfolders into a folder of the same name inside the **destination** on the drive, `Backup` by default, such as `Backup\Documents`. Two folders with the same name can't share a destination.
- **New and changed files:** a file is copied when the drive has no copy, or when its size or last modified time differs from the copy PC Companion made last time. The files are compared with what PC Companion recorded about its copies, kept for each drive on the PC, without reading their contents.
- **Replacing a copy:**
  - A changed file replaces only a copy that PC Companion made and that's still as it left it, with the size and last modified time it recorded.
  - The new copy is written under a temporary name, `<name>.pccompanion-new`, hidden, and takes the old copy's place only once it's complete (`MoveFileEx` with `MOVEFILE_REPLACE_EXISTING`), so a drive ejected or pulled out meanwhile keeps the old copy.
  - A copy changed on the drive, a file PC Companion didn't put there, and a read-only copy, which Windows doesn't replace, stay as they are, with an entry in the history.
  - A file on the drive that's the same as the PC's, with the same size and last modified time, counts as PC Companion's own copy, so backups go on after PC Companion's records are lost, as when it's uninstalled and installed again.
- **Nothing is deleted on the drive:** files deleted or renamed on the PC keep their copies there. Drives have no Recycle Bin, and a mistake would delete for good.
- **Skipped,** each counted in the result with why:
  - links, hidden and system files and folders;
  - files kept online only by a cloud storage app, which reading would download;
  - files with a Mark of the Web, on a drive that can't keep it, such as an exFAT or FAT32 drive, as in Folder Rules: Windows would drop the mark without an error, and a file restored from the drive would open without Windows' warning. The page suggests formatting the drive as NTFS to back them up;
  - files over 4 GB, on a FAT32 drive, which can't hold them;
  - files another program has open for writing, which are tried again at the next backup.
- **Before each copy,** PC Companion checks that the destination is still on the recognized drive, by the volume serial of the folder it writes to.

### Open a folder

- Opens a folder in File Explorer, on the drive, such as `DCIM`, or on the PC, such as the import's destination, after the actions before it.
- It's opened as the user would open it: `explorer.exe` with the folder's full path, started through `IHostCommands.StartProgram`, never with admin rights. A folder that's a link isn't opened. File Explorer shows the folder's files and their icons and thumbnails as it does when the user opens the drive, as AutoPlay's **Open folder to view files** does; PC Companion never opens a file.

### Eject when done

- A switch for each drive, off by default. When the drive's actions are done, PC Companion asks Windows to eject the drive, as **Safely Remove Hardware** does (`CM_Request_Device_Eject` on the drive's device, or the device it's connected through), and the notification says that it can be removed.
- When a program has the drive open, Windows refuses, and the notification says so; the drive stays connected. Virtual disks can't be ejected this way.

### The notification when done

- Each run ends with a notification: the drive's name, what each action did, such as "Imported 132 photos" or "Backed up 45 files, 3 skipped", and "You can remove the drive", "The drive was ejected" or why the run stopped. Selecting it opens the history on the page.
- A run that acts at once also starts with one: "Backing up to Backup drive. Don't remove the drive."

## When a drive arrives

### Asking first or acting at once

- Each drive has **When connected:** **Ask first**, the default, **Act at once**, or **Do nothing**.
- **Ask first:** a notification, "Backup drive is connected. Select to back up Documents and Pictures." Selecting it runs the actions, once PC Companion has recognized the drive again; ignoring or dismissing it does nothing. Selecting it after the drive was removed does nothing.
- **Do nothing:** the drive stays in the list, and **Run actions** on the page and in the PC Companion icon's menu still run its actions.
- **Run actions** on the page and in the menu run a connected drive's actions at any time.

### Locked, signed out, or not running

- **While the PC is locked,** or the user's session is disconnected, as when another user signs in, nothing runs and nothing asks: a drive connected then could be a look-alike left by someone else.
- **When the user is back,** each drive with actions that's still connected gets the question, even one set to **Act at once**.
- **Drives already connected when PC Companion starts,** at sign-in, when the host restarts, when the feature is turned on, or when the drive is added, get the question the same way, once.
- Whether the session is locked comes from Windows' session messages (`WTSRegisterSessionNotification`), as System Meters reads it.

### AutoPlay

- PC Companion doesn't change AutoPlay. AutoPlay may also ask what to do with the drive, as it does for every drive.
- The page has a note about it, with **Open AutoPlay settings** (`ms-settings:autoplay`), where the user can set removable drives and memory cards to **Take no action**.
- PC Companion doesn't cancel AutoPlay's question: Windows sends the message that cancels it only to the window in front, and the other way needs a setting for all users that Microsoft says background programs shouldn't use. Offering PC Companion as one of AutoPlay's choices is left out; see [Not included](#not-included).

## While actions run

### One run at a time

- Runs wait in line, in the order the drives arrived. A run does its drive's actions in order, one file at a time, at background priority, so the disk stays available to the programs in front.
- **Stop**, on the page and in the PC Companion icon's menu, stops the run after the file it's copying, whose partial copy is deleted.

### Ejecting and pulling out

- PC Companion keeps a handle open on each recognized drive while it's connected, opened without access, so that Windows tells it when the drive is about to be ejected, is unlocked or is formatted. A drive formatted while connected is recognized again.
- **When Windows asks to eject a drive,** PC Companion closes its handles on the drive at once, stops the copy to or from it, and lets Windows eject it. It waits up to 200 ms for the copy to let go before it answers, so that its copy doesn't keep the drive from ejecting; in the spike, a copy let go within 60 ms. The run stops, and the history says the drive was ejected.
- **When a drive is pulled out** without ejecting, the copy fails, and the run stops the same way. A copy to the drive leaves its unfinished copy there, at its full size, under its temporary name; PC Companion deletes it the next time it backs up to the drive, before anything else. The old copy, if any, is untouched. An unfinished copy on the PC is deleted.
- A run that stopped isn't started again on its own. When the drive comes back, it's handled like any arrival, and a new run picks up where the last one stopped, since imports and backups skip what's done.

### A full drive

- **Before each import or backup,** PC Companion adds up the size of what it would copy and compares it with the free space on the destination's drive (`GetDiskFreeSpaceEx`). If it doesn't fit, nothing is copied, and the notification and the history say how much space it needs and how much is free.
- **If the drive fills up during a run,** as when another program writes to it, the run stops, and the file that didn't fit leaves nothing behind: Windows sets a copy's full size before it writes, and refuses it at once.

### Locked BitLocker drives

- A drive protected with BitLocker To Go arrives locked, unless Windows unlocks it on its own, and Windows asks the user for its password. PC Companion can't read it then, so it can't recognize it, and the page shows it as locked.
- When the user unlocks it, Windows tells PC Companion's handle on the drive, and PC Companion recognizes the drive and handles it like any arrival. A drive that's never unlocked gets nothing.

## What actions never do

- Open or run a file from a drive, read `autorun.inf`, or follow a link on a drive.
- Delete or change anything on a drive, except writing its ID file, writing backups, replacing its own unchanged copies, and deleting its own unfinished copies.
- Copy files to a drive it doesn't recognize, or act while the PC is locked.
- Replace a file on the PC, or delete one without the Recycle Bin.

## Running as administrator

- As for Folder Rules, no setting may make PC Companion read or write files as administrator; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs).
- So while the host runs as administrator after a UAC prompt, Drive Actions pauses, and the page says why. Drives that arrive meanwhile are asked about when it runs without admin rights again, if they're still connected.

## History and undo

- **The history** lists the runs, newest first: the time, the drive, each action's result, and why a run stopped. An import lists each file it copied, with where it went; a backup lists the files it skipped, with why, up to 100 per run.
- **Undo** on an import puts back what it copied: the copies on the PC go to the Recycle Bin, one file or the whole run, after PC Companion checks that each is still where it put it, unchanged. Windows asks first about a file too big for the Recycle Bin, as for Folder Rules. An undone file still counts as imported, as a file put back stays put in Folder Rules, so the next import doesn't copy it again; **Run now** can.
- **Backups have no Undo:** undoing them would delete from a drive that has no Recycle Bin, and they're only copies.
- **Kept:** the history for the last 30 days, up to 1,000 runs, in `drive-actions.json` in the data folder. What PC Companion imported from each drive and the copies it made on each are kept in the data folder too, until the drive is removed from the list. Only the host writes them. **Clear history** empties the history and keeps what was imported and backed up.

## The PC Companion icon's menu

- While a drive with actions is connected, the PC Companion icon's menu has a **Drive Actions** submenu, through `IHostMenuItems`: **Run actions for Backup drive**, and while a run is going, its drive, action and progress, with **Stop**.

## The settings app and the host

- The page needs what only the host knows, the connected drives, the runs and the history, and asks the host to act, so only the host writes on drives and knows what it wrote. The host serves it through a named pipe that only the user's own account and the system can open, and that rejects remote clients, as Folder Rules' pipe does. The requests: the drives connected now and what they are, **Add a drive** (which writes the ID file and gives back the ID and the hashed serials), **Use this drive**, the history, **Clear history**, the files an import would copy, **Run now**, **Stop** and **Undo**.
- The host checks every request: a drive must be connected, count, and be the one the request names; a run must be for a drive in its settings; an entry to undo must be in its history.
- **Notifications:** the host shows them from its icon, with `Shell_NotifyIcon`, as it shows the update notification, and tells the feature which one was selected. Features show notifications through a new host command in Core, so that each feature doesn't need its own icon.

## Threads

- **The host's UI thread** runs:
  - `Apply`, which reads the section, checks the drives and actions, and starts or stops the feature's thread and its hidden window, without touching the disk;
  - the hidden window's messages: drives that arrive and go (`WM_DEVICECHANGE` with `DBT_DEVTYP_VOLUME`, which Windows sends to top-level windows), and, for the handle on each recognized or locked drive, `DBT_DEVICEQUERYREMOVE`, `DBT_DEVICEREMOVECOMPLETE` and the custom events `GUID_IO_VOLUME_MOUNT` and `GUID_IO_VOLUME_FVE_STATUS_CHANGE`; the session's lock and unlock; and the selected notification. These only pass work to the feature's thread, except the request to eject, which closes the drive's handles at once, stops its copy, and waits up to 200 ms for the copy to let go;
  - the PC Companion icon's submenu, built from what the feature already knows;
  - showing notifications.
- **The feature's own thread,** at background priority, does everything that touches a drive or a file: reading a drive's details, its ID file and its serials, recognizing it, deleting unfinished copies, listing files, working out what to copy, copying with `CopyFile2`, which it cancels when the drive is ejected or the run is stopped, replacing copies with `MoveFileEx`, ejecting with `CM_Request_Device_Eject`, and the history and records. Copies run one at a time.
- **The Recycle Bin's thread,** an STA thread, as for Folder Rules, sends the copies to undo to the Recycle Bin.
- **The thread pool** reads the settings app's requests from the named pipe and passes them to the feature's thread, which answers them.
- **Stopping** the feature cancels the copy, closes the handles, and ends the thread after the file it's copying.
- **Failures:** the feature's thread, the window's handlers and the pipe run inside the feature's error boundary. A failure there is logged and keeps the feature off until the host restarts, like a failing `Apply`. A file that can't be copied isn't such a failure: it's an entry in the history.

## Spike results

A spike in Windows Sandbox (Windows 11 24H2, build 26100), with a .NET 10 helper, on virtual disks formatted NTFS, exFAT and FAT32, with MBR and GPT partitions, confirmed:

- **Reading a drive without admin rights:** a standard user read the label, file system, volume serial, volume GUID path, bus type and hardware serial (`IOCTL_STORAGE_QUERY_PROPERTY` through a handle opened without access), the partition's and the disk's IDs, and the disk's device and the devices it's connected through. Virtual disks reported a file-backed virtual bus, no hardware serial and no removable flag, and Windows called them fixed drives.
- **Identities:**
  - A quick format gave the volume a new serial, on NTFS and exFAT.
  - The volume GUID path came from data on the disk: on MBR disks, the disk's signature and the partition's offset; on GPT disks, the partition's GUID. It survived a quick format, and changed when the partition was made again.
  - A copy of a disk, attached next to the original, kept the disk's signature or GUID, the partition's GUID and the volume serial; only its volume GUID path was new.
- **Arrival:** volumes could be read within 11 ms of `DBT_DEVICEARRIVAL`. A new virtual disk's volume first got the letter D and lost it within 30 ms, before diskpart gave it its own letter.
- **A handle to the volume,** opened without access and registered with `RegisterDeviceNotification`, got custom events: a quick format sent `GUID_IO_VOLUME_LOCK`, `GUID_IO_VOLUME_DISMOUNT`, `GUID_IO_VOLUME_UNLOCK`, `GUID_IO_VOLUME_MOUNT` and `GUID_IO_VOLUME_CHANGE`. Detaching the disk sent lock, dismount and unlock, then `DBT_DEVICEREMOVECOMPLETE`, with no `DBT_DEVICEQUERYREMOVE`.
- **BitLocker:**
  - A locked volume failed `GetVolumeInformation` with `FVE_E_LOCKED_VOLUME` (0x80310000), also for a standard user, while its GUID path, partition and device could still be read.
  - Unlocking it sent `GUID_IO_VOLUME_FVE_STATUS_CHANGE` and `GUID_IO_VOLUME_MOUNT` to the handle, and it could be read 20 ms later. No second arrival came.
  - The Shell's `System.Volume.BitLockerProtection` property read 0 in every state, also on the encrypted volume.
- **Copies:**
  - `CopyFile2` sets a copy's full size before it writes. So a file that didn't fit, and a file over 4 GB on FAT32, failed at once with `ERROR_DISK_FULL` (112), not a code for a file too big, and left nothing.
  - Cancelling stopped a copy within 60 ms to 1.3 seconds with `ERROR_REQUEST_ABORTED`, and deleted the unfinished copy.
  - Detaching the disk during a copy failed it with `ERROR_NOT_READY` (21), and left the unfinished copy on the disk, at its full size. The volume wasn't marked dirty.
- **Removing during a copy:** with `CM_Query_And_Remove_SubTree`, which asks programs as an eject does, `DBT_DEVICEQUERYREMOVE` came for a handle to a folder on the disk, and the copy ended 60 ms after it was cancelled. Another handle in the Sandbox still kept the volume, and Windows refused the removal. Windows refused to eject a virtual disk with `CM_Request_Device_Eject` as not supported.
- **Replacing a copy:** `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` and `ReplaceFile` replaced a file on NTFS, exFAT and FAT32; neither replaced a read-only file. A hidden system file at the root could be written on all three.
- **Not checked yet,** all tested by hand: ejecting a USB drive during a copy; card readers, a card put into one, and their drive letters without a card; hardware serials of USB drives, also two identical ones; the Shell's BitLocker property on a PC; selecting a notification, which the Sandbox doesn't show; AutoPlay's question at the same time; and a really locked session.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Drives,** a list with **Add a drive**; for each drive its name and kind, whether it's connected, not connected, locked, not recognized or running, its problem, if any, **Run now**, **When connected**, its **Actions** with **Add action**, and for each action what it does, **Edit** and **More options** with **Move up**, **Move down** and **Remove**, **Eject when done**, and **More options** with **Rename** and **Remove**;
  - for a drive that receives backups and isn't known to be encrypted, a note: "Anyone who has this drive can read its backups. Turn on BitLocker To Go to protect them.";
  - a note about AutoPlay, with **Open AutoPlay settings**;
  - **History,** 50 runs at a time with **Show more**, with **Undo** on imports and their files, **Refresh**, **Open Recycle Bin** and **Clear history**;
  - a **Permissions & data** card.
- **The action editor** is a dialog: **Action**; for an import, **From**, with **Browse** on a connected drive, **Types** with **Add a kind**, **To** with **Browse**, and **When a file with that name is there**; for a backup, **Folders** with **Add folder** and **Remove**, and **Destination** on the drive; for **Open a folder**, the folder, on the drive or on the PC. Problems show below their box, and **Save** is available only when the action has none. Adding Camera cards adds an import with the defaults.
- **Run now** on a connected drive lists what its imports would copy, with checkboxes, and what its backups would copy, with how many files and how much space, then runs the actions with the files chosen. It's off while the drive isn't connected or recognized.
- **Remove**, **Clear history** and **Undo** of a whole run ask first. Removing a drive deletes what PC Companion recorded about it, and leaves its ID file and backups on the drive.
- When the feature is first turned on, there are no drives.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns the feature off with a warning in the log. A `null` in a list is left out. A drive or action with a value this version doesn't know, such as one from a newer version, keeps it when the settings are saved, and isn't used. A drive without an ID or a volume serial, other than Camera cards, is never recognized.

```json
"features": {
  "driveActions": {
    "enabled": true,
    "drives": [
      {
        "name": "Backup drive",
        "kind": "Drive",
        "id": "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f",
        "volumeSerial": "3f9a1c0e7b2d4a6c",
        "hardwareSerial": "a1b2c3d4e5f60718",
        "whenConnected": "Ask",
        "ejectWhenDone": true,
        "actions": [
          {
            "action": "BackUp",
            "folders": ["C:\\Users\\Ana\\Documents", "C:\\Users\\Ana\\Pictures"],
            "destination": "Backup"
          }
        ]
      },
      {
        "name": "Camera cards",
        "kind": "CameraCards",
        "whenConnected": "Act",
        "ejectWhenDone": false,
        "actions": [
          {
            "action": "Import",
            "from": "DCIM",
            "extensions": ["jpg", "heic", "cr3", "mp4"],
            "destination": "C:\\Users\\Ana\\Pictures\\{year}\\{month}",
            "ifExists": "AddNumber"
          },
          {
            "action": "OpenFolder",
            "folder": "C:\\Users\\Ana\\Pictures"
          }
        ]
      }
    ]
  }
}
```

- `kind`: `Drive` or `CameraCards`; there is one `CameraCards` at most. `whenConnected`: `Ask`, `Act` or `Nothing`.
- `id` is the ID in the drive's ID file; `volumeSerial` and `hardwareSerial` are hashes, `hardwareSerial` empty for a drive without one.
- `action`: `Import`, `BackUp` or `OpenFolder`. An import's `from` and a backup's `destination` are folders on the drive, without its letter; an import's `destination` and a backup's `folders` are on the PC. An `OpenFolder` folder without a letter is on the drive.
- `ifExists`: `AddNumber` or `Skip`.

## Not included

- **Phones and cameras without a drive letter** (MTP and PTP), which need Windows Portable Devices, a phone that's unlocked and set to **File transfer**, and **Trust** on an iPhone; a later issue.
- **Offering PC Companion as one of AutoPlay's choices,** with a registration for the current user, which Windows documents; a later issue.
- **Network drives and shares** as drives with actions: they don't arrive as drives do, and Windows can't eject them.
- **Moving or deleting files from the drive after an import:** the card is the copy that's left if something goes wrong, and drives have no Recycle Bin. The camera formats it.
- **Removing files from the drive that were deleted on the PC,** and keeping older copies of changed files.
- **The date a photo was taken** in imports' folder names: [#231](https://github.com/Lazyb0y/pc-companion/issues/231).
- **Running programs or scripts,** from the drive or the PC, and opening files.
- **CDs and DVDs,** and drives without a letter.
- **Encrypting backups:** BitLocker To Go does it, and PC Companion recommends it.
- **Changing the ID file after each run,** which would expose a copy of the drive once the real drive is used again, but would also stop drives copied on purpose, to use in turn, from being recognized.
- **Backups on a schedule** to a drive that stays connected, and actions when a drive is removed.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **Every API is documented** and in every supported version: `RegisterDeviceNotification` with `DBT_DEVTYP_VOLUME` and `DBT_DEVTYP_HANDLE`, the volume custom events in `ioevent.h`, `GetVolumeInformation`, `GetDiskFreeSpaceEx`, `DeviceIoControl` with `IOCTL_STORAGE_QUERY_PROPERTY` and `IOCTL_STORAGE_GET_DEVICE_NUMBER`, `CM_Get_Device_Interface_List`, `CM_Get_Parent`, `CM_Get_DevNode_Registry_Property`, `CM_Request_Device_Eject`, `CopyFile2`, `MoveFileEx`, `IFileOperation`, `Shell_NotifyIcon` and `WTSRegisterSessionNotification`.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Nothing from a drive runs:** PC Companion never opens or runs a file from a drive, never reads `autorun.inf`, skips links on drives, and checks the names of files from a drive before it uses them in a destination, as Folder Rules checks names, so a name can't add a folder or leave the destination.
- **Look-alike drives:** copying the user's files to another drive would give them to whoever has it.
  - A drive's name is never enough: a look-alike with the same name gets nothing.
  - Everything a plain drive carries can be copied, so the ID file and the serials keep out a drive made by someone who never had the user's drive, but not a copy made by someone who had it, and who could read the drive anyway.
  - Nothing runs while the PC is locked, and drives connected meanwhile are asked about, so a drive left at a locked PC gets nothing without the user.
  - A drive with the right ID file but other serials gets nothing, and a notification warns about it.
  - Only BitLocker To Go keeps the backups on a lost or copied drive from being read, so the page recommends it for every backup drive it doesn't know to be encrypted. It isn't required: Windows Home can unlock BitLocker drives but can't encrypt them.
- **Reads:**
  - for each drive that's connected and counts: its label, file system, size, free space, volume serial, hardware serial and kind, and its ID file;
  - for imports: the names, sizes, dates and attributes of the files in the folder on the drive;
  - for backups: those of the files in the chosen folders on the PC, and of the copies PC Companion made on the drive;
  - the contents of files only to copy them.
- **Changes:** on a recognized drive, its ID file, the backups in the destination and its own unfinished copies; on the PC, new files in the import's destination. Undo sends imported copies to the Recycle Bin. It never replaces a file on the PC, and never deletes a file there without the Recycle Bin, unless the user selects **Yes** in Windows' own question about a file too big for it.
- **Ejecting:** only after a drive's actions, with **Eject when done** on.
- **Settings:** the drives and actions are in `settings.json`, with the IDs and hashed serials. Any program running as the user can change that file, for example to add its own drive, but it could also copy the user's files itself, so this crosses no boundary while PC Companion runs as the user. While it runs as administrator, Drive Actions pauses.
- **The pipe:** only the user's own account and the system can open it, it rejects remote clients, and the host acts only on connected drives in its settings.
- **History and records:** file names, folders and drive names, which can be personal, kept on this PC: the history for 30 days at most, and what was imported and backed up until the drive is removed from the list. **Clear history** empties the history, removing a drive deletes its records, and uninstalling removes them with the rest of the data folder. `PRIVACY.md` gets a line on Drive Actions.
- **Network:** none. Network drives aren't counted.
- **Logs:** Debug entries when the settings are applied, with how many drives and actions can be used, when a drive arrives or leaves, by its number in the list, whether it was recognized and why not, when a run asks, starts, stops and ends, with counts, and for each request from the page. Warnings when a drive's handle or a copy fails with the error code, when Windows refuses to eject, when the settings can't be read, and when the pipe fails. Never file names, folder paths, drive names or labels, IDs or serials: the history has what the user needs.
- **Rights:** runs without admin rights, and never touches files with them.
- **Failures:** a file that can't be copied stays where it was, with an entry in the history.

## Performance

- **Between arrivals nothing runs:** no drive is polled. The host waits for Windows' messages, and keeps a handle open on each recognized or locked drive, which costs nothing.
- **An arrival** costs reading the drive's details and its ID file, a few milliseconds on the feature's thread, and nothing on the UI thread but passing the drive on.
- **Listing:** at an import, the folder on the drive with its subfolders, and at a backup, the chosen folders and the copies, reading only names, sizes, dates and attributes; in Folder Rules' spike, a folder of 10,000 files took 49 ms the first time and 5 to 7 ms after.
- **Copies** run at background priority, one file at a time.

## Accessibility and localization

- **The page and the action editor** work with the keyboard alone, and keyboard focus stays on a drive or an action when the list changes. Lists, boxes and buttons have labels for screen readers; buttons carry the drive's or the action's name, and each drive's state, such as "Backup drive, connected, backing up", is part of its name. A run's progress and result are announced, and problems below a box are announced.
- **Notifications** are Windows' own, which screen readers read and Do not disturb holds back. Everything a notification offers is also on the page and in the PC Companion icon's menu, with the keyboard.
- **Colors and layout:** the page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages. Paths, extensions and patterns are shown left to right in every language.
- **Dates** in the history follow the user's regional settings, and sizes are shown as File Explorer shows them. The parts in braces are the same in every language.
- **Strings:** the strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake drives, a fake file system, fake Windows messages and a fake clock:
  - **which drives count:** removable drives, external and virtual disks, the PC's own disks, network drives and drives without a letter;
  - **recognizing a drive:** the ID file and both serials matching, any of them different, a drive without a hardware serial, a reformatted drive, and a drive with the ID file and other serials, which gets the warning;
  - **Camera cards:** `DCIM` at the root, `DCIM` as a link, and a drive in the list that has `DCIM`;
  - **asking or acting:** each **When connected**, drives that arrived while locked or were connected at start, and a drive whose letter comes and goes;
  - **imports:** subfolders, types, destinations with parts, conflicts, files imported before with the hour's tolerance, Run now's list, and names from the drive that try to add a folder;
  - **backups:** new, changed and unchanged files, replacing only unchanged own copies, copies changed on the drive, files PC Companion didn't put there, read-only copies, nothing deleted, two folders with the same name, the skips and their reasons;
  - **space:** a run that doesn't fit, and files over 4 GB on FAT32;
  - **ejecting and pulling out:** stopping, unfinished copies deleted at the next arrival, and a run picking up where it stopped;
  - **history and undo:** entries, Undo checks, limits and **Clear history**;
  - **the pipe:** its messages and its checks of requests;
  - **the settings:** defaults, the spec's example, unknown values, limits and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/DriveActions.ps1`), on virtual disks the checks create with diskpart:
  - **adding:** **Add a drive** lists the disks with their kinds, writes the ID file and saves the drive;
  - **arrival:** a recognized disk that arrives gets the question, and selecting it, sent to the PC Companion icon as the Updates group sends it, runs its actions; **Act at once** runs them at once; **Do nothing** does nothing, and **Run actions** in the menu runs them;
  - **imports:** a disk with `DCIM\100TEST` and `DCIM\101TEST` is recognized as Camera cards, and its photos are copied into month folders, once; attached again, nothing is copied again; a conflict adds a number;
  - **look-alikes:** a disk with the same label, one with a copy of the ID file and another serial, and a reformatted disk get nothing, and the copy of the ID file brings the warning;
  - **backups:** new and changed files are copied; a copy changed on the disk and a read-only copy stay; files deleted on the PC stay on the disk; a file with a Mark of the Web isn't copied to an exFAT disk; a file over 4 GB, sparse on the PC, isn't copied to a FAT32 disk, with its reason; a disk too small for the backup gets nothing, and the history says how much it needs;
  - **pulling out:** detaching a disk during a backup stops the run, keeps the old copy, and the unfinished copy is deleted when the disk is attached again;
  - **locked:** with the session's lock and unlock sent to the feature's window as Windows sends them, as the System Meters group does, a disk attached while locked gets nothing until the unlock, then the question, also with **Act at once**; a disk attached before the host starts gets the question;
  - **BitLocker:** a disk encrypted with `manage-bde` and a recovery password, attached locked, gets nothing until it's unlocked, and is then recognized and handled;
  - **undo:** Undo of an imported file and of a run sends the copies to the Recycle Bin;
  - **the pipe** refuses requests for disks that aren't connected or in the settings;
  - **the page:** drives, actions and history, in English and Arabic, light and dark, with screenshots, and its changes saved with keyboard focus near what changed;
  - no feature holds the host's UI thread, as every group checks.
- **What the Sandbox can't check:** USB drives, card readers and their cards, hardware serials, ejecting, which Windows refuses for virtual disks, notifications, which the Sandbox doesn't show, AutoPlay, a really locked session, Narrator and Windows 10.
- **By hand:**
  - USB flash drives formatted FAT32, exFAT and NTFS: recognizing them on two USB ports and on another PC, two identical drives, and a drive renamed;
  - ejecting with **Safely Remove Hardware** while idle and during a copy, and pulling a drive out during a copy;
  - **Eject when done**, also with a program keeping the drive open;
  - a card reader with and without a card, a card put in while the reader is connected, and an SD card from a camera, formatted in it;
  - a USB disk, and a Thunderbolt or USB4 disk;
  - BitLocker To Go with a password and with automatic unlocking, and the Shell's BitLocker property on encrypted and plain drives;
  - notifications: selecting one from the toast and later from the notification center, two in a row, and with Do not disturb on;
  - AutoPlay set to ask, at the same arrival;
  - locking the PC, connecting a drive, and unlocking; switching users;
  - Controlled folder access with imports into Pictures;
  - Always run as administrator with UAC on, where Drive Actions pauses;
  - Narrator on the page and the notifications, a right-to-left language, and Windows 10.
