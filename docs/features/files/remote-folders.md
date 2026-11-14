# Remote Folders

Open the files on an SFTP server from File Explorer, as if they were on the PC: each server gets a folder in File Explorer's navigation pane, a file downloads when it's opened, and changes go back to the server. Files that were opened stay usable when the server can't be reached. No driver and no admin rights are needed.

- Module: Files (`PCCompanion.Modules.Files`), with the files' work in its own process, `PCCompanion.RemoteFolders.exe`. Settings section: `features.remoteFolders`. Issue: [#47](https://github.com/Lazyb0y/pc-companion/issues/47).
- Inspired by remote folders in KDE Plasma and GNOME, and by online-only files in macOS.
- Off until the user turns it on.
- SFTP only in this version. WebDAV comes later, in its own issue.

## Behavior

- The user adds a server: its address, the user name, a password or an SSH key, and the folder on the server. Before saving, PC Companion connects once and shows the server's key, which the user confirms; see [Server keys](#server-keys).
- Each server gets a folder, `%USERPROFILE%\Remote Folders\<name>`, which File Explorer shows in its navigation pane under the server's name, as it shows a cloud storage app's folder.
- The folder shows what's on the server. A subfolder is listed from the server when it's first opened. A file takes no space on the PC until a program opens it: then it downloads, and the program waits until it's there.
- File Explorer's **Status** column shows each file's state: online only, on this PC, always kept on this PC, or with changes waiting for the server. Its menu has **Always keep on this device** and **Free up space**, which download the files or remove their copies on the PC.
- A file saved in the folder goes to the server once the program has closed it. New files and folders, renames, moves and deletes go to the server too.
- PC Companion never replaces or deletes a server file that changed since it last saw it. When a file changed both on the server and on the PC, both versions are kept; see [Conflicts](#conflicts).
- When the server can't be reached, files on the PC open and can be changed, and the changes wait until it's back. Online-only files and folders that were never opened can't be opened then, and Windows says the network is unavailable; see [Offline and reconnecting](#offline-and-reconnecting).
- Every downloaded file gets a Mark of the Web, so Windows and apps check it as they would a download from the internet.

## Compared with other systems

- **Windows** maps WebDAV folders through its WebClient service, which Microsoft deprecated in 2023 and doesn't start by default, with files of 50 MB at most. File Explorer has no SFTP. Remote Folders adds SFTP, with no limit on file size, and files that stay usable when the server is gone.
- **KDE Plasma** opens SFTP and WebDAV in Dolphin and in its own apps. Other apps get a temporary copy, and after a change a question whether to upload it. There's no offline copy, and KDE's documentation warns that an unreachable server can hang apps or lose data. A changed server key gives only a generic connection error. Remote Folders gives every app real files, uploads changes without a question, keeps opened files usable offline, and says plainly when a server's key changed.
- **GNOME** connects to servers from Files, and shows them to other apps through FUSE. Users report Files freezing when the server goes away, such as after suspend, and there's no offline copy. In Remote Folders, File Explorer waits for an unreachable server for 10 seconds at most, and then not again for at least 30 seconds: requests meanwhile fail at once.
- **macOS:** Finder's Connect to Server has no SFTP, and its WebDAV client is known to be slow and needs locking on the server to write. Apps that use the File Provider framework show online-only files, offer **Remove Download**, and keep a conflict copy when a file changed on both sides. Remote Folders follows that model with Windows' own equivalent, the Cloud Files API, for any SFTP server.
- **Tools for Windows that show servers as drives** mostly install a file-system driver: setup needs admin rights, antivirus can block the driver, and it has crashed Windows for some users. Users report drives that disappear after sleep or a restart, and Explorer slowing down while a server is unreachable. Several don't check the server's key unless told to, and most keep no offline copy. Remote Folders needs no driver and no admin rights, always checks the server's key, and keeps opened files usable offline.
- **Sync apps for one kind of server** use the same Windows API, but only with their own server. Remote Folders works with any SFTP server, such as a NAS, a Raspberry Pi or a hosting account.
- **File transfer apps for Windows** edit a remote file in a temporary copy and upload it when it's saved. Users report edits that were never uploaded. In Remote Folders, a change shows as waiting in File Explorer's Status column until the server has it, and stays waiting across restarts.

## Servers

- **Adding a server:**
  - **Address**, a host name or IP address, and **Port**, 22 by default.
  - **User name**.
  - **Sign in with:** **Password**, or **SSH key** with the key file, in OpenSSH or PuTTY format, and its passphrase if it has one. Ed25519, ECDSA and RSA keys work.
  - **Name:** the folder's name, the server's address by default. It must be a valid folder name and differ from the other servers'.
- **Connect** connects, signs in and shows the server's key; see [Server keys](#server-keys). It then fills in **Folder on the server** with the user's home folder, which the user can change, and checks that the folder can be read.
- **Save** is available once the key is confirmed and the folder can be read. The folder is created in `%USERPROFILE%\Remote Folders`, with a number added when a folder with that name is there, and appears in File Explorer.
- **Editing a server:** its name, which changes the name File Explorer shows, and how it signs in. The folder on the PC keeps its first name. A new address, port, user or folder on the server is another server, so the user removes it and adds the new one.
- **Removing a server** asks first. When changes are still waiting, it says how many, and offers to remove it anyway. PC Companion then disconnects the folder from the server:
  - online-only files disappear, as they're only on the server;
  - files on the PC stay, with waiting changes, in what's now an ordinary folder, which the page offers to open;
  - the password or passphrase is deleted from Credential Manager.
- Signing in with a password also answers a server's keyboard-interactive question when it's only the password. Servers that ask more, such as for a one-time code, aren't supported; see [Not included](#not-included).

## The folder in File Explorer

- **Where:** `%USERPROFILE%\Remote Folders\<name>`. Windows requires an NTFS drive, and doesn't allow such folders inside another cloud storage app's folder or on network drives. When the user profile is on another kind of drive, the page says so and no server can be added.
- **Navigation pane:** each server's folder, with its name and the feature's icon. Windows adds it when PC Companion registers the folder with `StorageProviderSyncRootManager`, and removes it when the registration is removed. This works without package identity and without admin rights, as the [spike](#spike-results) showed.
- **States**, shown by Windows in the **Status** column and the details pane:
  - online only: a placeholder with the file's name, size and dates, which takes no space;
  - on this PC: downloaded, which Windows may free up again when space runs low, if Storage Sense is on;
  - always kept: kept with **Always keep on this device**, and downloaded again after a change on the server;
  - waiting: changed on the PC and not yet on the server.
- **Folders** are listed from the server when they're first opened. Windows then asks again each time a program lists them, since PC Companion never tells it a folder is complete; see [Changes on the server](#changes-on-the-server).
- **Hidden files:** names that start with a dot, which Linux hides, get the hidden attribute, so File Explorer shows them only with **Hidden items** on.
- **Thumbnails:** File Explorer shows online-only files with their type's icon and doesn't download them for a thumbnail.
- **Names Windows can't show:** see [Names from the server](#names-from-the-server).

## Downloading

- A file downloads in full the first time a program reads it (Windows' `Full` hydration policy), and the program waits until then. For long downloads, Windows shows its own progress with **Cancel**. When a program in the background starts a download, Windows shows its own notification, and **Automatic file downloads** in Windows Settings can block that program.
- **Always keep on this device** on a file or folder downloads every file in it, one at a time, at background priority, and keeps them; files that change on the server are downloaded again. Links on the server to folders aren't followed for this.
- **Free up space** removes the copies on the PC, but never a copy with changes that aren't on the server yet; that file stays as it is.
- **Errors:**
  - When the server can't be reached, the download fails with Windows' message that the network is unavailable.
  - When no data arrives for 30 seconds, the download fails the same way. Windows itself cancels a request after 60 seconds without news, so PC Companion reports progress while data flows.
  - When the file on the server is shorter or longer than the placeholder says, because it changed meanwhile, the download fails, and the folder is listed again.

## Uploading

- **What counts as a change:** Windows marks a file as no longer in sync when a program writes to it, and PC Companion watches the folder for new files and folders. A file is uploaded once no program has it open and it hasn't changed for 2 seconds. Changes made while PC Companion wasn't running are found when it starts: a changed file's in-sync mark is still cleared, and a new file or folder has no placeholder.
- **How a file is uploaded:**
  1. PC Companion reads the file through a handle that steps aside when a program opens the file (`CfOpenFileWithOplock`), so it never blocks the user's programs; when that happens, it tries again later.
  2. It uploads it under a temporary name in the same folder on the server, `.pcc-upload-<random>`.
  3. Just before the rename, it checks that the file on the server is still the version the PC's copy came from, by its size and modification time; see [Conflicts](#conflicts).
  4. It renames the temporary file over the original with the `posix-rename@openssh.com` extension, so the file on the server is never half written. On a server without that extension, where a plain rename can't replace a file, it renames the original out of the way, renames the new file into place, then deletes the original, so a failure at any step leaves one whole version on the server.
  5. It marks the PC's copy as in sync, only if it didn't change during the upload (`CfSetInSyncState` with the USN read before), and remembers the new version.
- **New files** are uploaded the same way; **new folders** are created at once.
- **Renames and moves** inside the folder are made on the server before Windows completes them, without replacing a file there. **Deletes** delete the file on the server first. When the server refuses, Windows refuses too, and the program says the operation was unsuccessful.
- **Moving a file out of the folder** downloads it, then deletes it on the server, so it moves to the PC. **Copying** it out only downloads it.
- **The Recycle Bin:** Windows deletes an online-only file for good, without downloading it, and moves a downloaded file to the Recycle Bin. Either way, the file is deleted on the server. A file restored from the Recycle Bin is uploaded again, as a new file.
- **Files that stay on the PC:** names programs use for their temporary and lock files, `~$*`, `~*.tmp`, `.~lock.*#`, `Thumbs.db` and `desktop.ini`, are never uploaded. Windows shows them as excluded.
- **While the server can't be reached:** changes to files wait. Renames, moves, deletes and new folders are made on the PC and written to a journal, `remote-folders\<server ID>.json` in the data folder, and made on the server, in order, when it's back, with the same checks. One that can't be made, such as a rename to a name that's now taken, is skipped, and the page lists it.
- **While PC Companion isn't running:** Windows lets the user delete files without asking it, as the spike showed. A deleted file can't be told apart from one added on the server meanwhile, so it comes back at the next start; nothing is deleted on the server. If Windows allows renames and moves then too, a renamed or moved file is renamed on the server at the next start, since its placeholder still says where it came from.

## Changes on the server

- **When PC Companion looks:**
  - when a program lists a folder, if it wasn't listed in the last 30 seconds;
  - every 10 minutes, for folders kept on this device, while the server can be reached;
  - when the user selects **Sync now**, on the page or in the PC Companion icon's menu, for the folders listed in the last hour.
- **A listing** answers Windows with the new entries if the server answers within 5 seconds; otherwise Windows gets what's known, and the rest appears when the listing finishes. The first listing of a folder always waits for the server, for 10 seconds at most.
- **What it does:**
  - new files and folders get placeholders;
  - a changed file whose PC copy has no waiting changes is updated: its size and dates change, and the copy on the PC is removed, so the next open downloads the new version. A file kept on this device is downloaded again at once. The copy is removed only if it's still in sync (`CfUpdatePlaceholder` with `VERIFY_IN_SYNC`), so an edit made a moment before is never lost;
  - a changed file whose PC copy has waiting changes is a conflict, handled when the copy is uploaded;
  - a file deleted on the server is removed from the PC: an online-only file disappears, and a downloaded copy goes to the Recycle Bin. A copy with waiting changes stays, and is uploaded again as a new file.

## Conflicts

- A conflict is a file changed on the PC whose server version also changed since the PC's copy was downloaded. PC Companion keeps both:
  - the PC's version is uploaded next to it as `<name> (conflict <date> <time>).<extension>`, with the words in the user's language, and the PC's copy is renamed to match;
  - the server's version keeps the name, as an online-only file.
- A notification says which file it was. Selecting it opens the folder with the conflict copy selected. The page lists conflicts until the user opens the folder.
- The same rule holds for renames and deletes: a delete never deletes a server file that changed since PC Companion last saw it, so that file comes back; a rename moves it anyway, since the content stays.

## Server keys

- **Adding a server:** the dialog shows the server's key, its type and SHA-256 fingerprint as OpenSSH shows it, such as `ED25519 SHA256:cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI`, and explains how to check it: ask the server's administrator, or run `ssh-keygen -lf` on the server's key file. **Save** needs **I checked this key** to be checked. PC Companion keeps the key's type and fingerprint in the settings.
- **Every connection** checks the key. Later connections ask the server for that type of key only, so an update that prefers another type of key never brings up a key the user didn't confirm.
- **A changed key** stops the connection before anything is sent, including the password. The server's state becomes **Server key changed**, a notification says so, and nothing connects until the user acts. The page shows the old and new fingerprints, explains that the server may have been reinstalled or that someone may be in between, and offers **Trust the new key**, which asks first.
- **Algorithms:** only current ones, with SSH.NET's strict key exchange against the Terrapin attack:
  - key exchange: `mlkem768x25519-sha256`, `sntrup761x25519-sha512`, `curve25519-sha256`, ECDH on NIST curves, and Diffie-Hellman groups 14 and 16 with SHA-2;
  - server keys: Ed25519, ECDSA, and RSA with SHA-2 (`rsa-sha2-512`, `rsa-sha2-256`);
  - ciphers: ChaCha20-Poly1305 and AES-GCM first, then AES-CTR;
  - MACs: HMAC-SHA2-256 and HMAC-SHA2-512, encrypt-then-MAC first.

  SHA-1 signatures and MACs, DSA keys, CBC and 3DES ciphers are refused. A server that offers nothing else gets an error that names the reason.

## Names from the server

- Names from the server are untrusted. An entry isn't shown when its name:
  - is empty, `.` or `..`, or contains `/`, `\` or a character Windows doesn't allow in names (`<>:"|?*` and control characters);
  - is a name Windows reserves, such as `CON`, `NUL`, `COM1` or `LPT1`, with or without an extension, or ends with a dot or a space;
  - is longer than 255 characters, or isn't valid UTF-8;
  - differs only in case from an entry shown before it, since Windows treats them as one name;
  - starts with `.pcc-upload-`, PC Companion's own temporary files.
- The page says how many entries each server has that can't be shown, and why, without their names. Nothing is renamed: a renamed copy couldn't go back to the server under its own name.
- **Paths:** every entry is created by Windows inside the folder from its parent's placeholder and its own checked name, so no name can reach outside the folder. A folder with more than 100,000 entries shows the first 100,000, and the page says so.
- **Links on the server** are shown as what they point to, as `stat` reports it. A link that can't be followed isn't shown. Downloading a folder kept on this device doesn't follow links to folders, so a loop on the server can't fill the PC.
- **Links on the PC:** a symbolic link or junction created in the folder isn't uploaded, and PC Companion never follows one when it reads files for an upload.
- **Attributes and permissions** from the server aren't copied, except the hidden attribute for names that start with a dot. Times are the server's modification time.

## Offline and reconnecting

- **States** of each server, on the page and in the PC Companion icon's menu:
  - **Connected**;
  - **Not connected:** nothing to do. PC Companion closes a connection after 5 minutes without activity and connects again when needed, in about 200 ms;
  - **Can't reach the server**, with the time of the last try;
  - **Sign-in failed:** the password, key or passphrase was refused;
  - **Server key changed;**
  - with the number of changes waiting, conflicts and entries that can't be shown.
- **Unreachable:** a connection that isn't made within 10 seconds, or is refused, makes the server unreachable. Requests then fail at once. PC Companion tries again on the next request after 30 seconds, and on its own, while changes wait, after 30 seconds, then 1, 2 and every 5 minutes; and at once when Windows reports a network change or the PC wakes from sleep.
- **Sign-in failed** isn't retried on its own, so a server that locks accounts after failed sign-ins doesn't lock the user's. A notification says so, and the page offers **Sign in again**.
- While connected, SSH keepalives go every 30 seconds, so a dropped connection is noticed within about a minute.

## The PC Companion icon's menu

- A **Remote Folders** submenu lists the servers with their states, such as "Home server: 2 changes waiting", each with **Open folder**, and **Sync now** for all of them. The host builds it from the provider's last status, without asking the provider while the menu opens.
- The notifications, for conflicts, sign-ins that failed, changed server keys and a provider that keeps crashing, show from the PC Companion icon, as other features' do: the provider asks the host through the pipe.

## Credentials

- **Passwords and key passphrases** are stored in Windows Credential Manager, as generic credentials for this PC only (`CRED_PERSIST_LOCAL_MACHINE`), so they don't roam with the profile. The target name is `PCCompanion/RemoteFolders/<server ID>`, with the server's name as the comment. Windows protects them with the user's sign-in (DPAPI), and the user can see and remove them in Credential Manager.
- The settings app writes and deletes them; `PCCompanion.RemoteFolders.exe` only reads them, when it connects. They're never in `settings.json`, in the logs, or in any other file.
- **SSH keys** stay in the user's own files. The settings keep only the key file's path, and the file is read at each connection.
- A server added without saving the password isn't supported: files are uploaded in the background, when no one is there to type it.
- Removing a server deletes its credential, and uninstalling deletes them all; see [Installer](#installer).

## Threads and processes

- **Its own process:** `PCCompanion.RemoteFolders.exe`, the provider, as [Threads and processes](../../architecture/features.md#threads-and-processes) allows:
  - **Other rights:** it must run with the user's own rights, as File Explorer does, even while the host runs as administrator, and code that reads a server's data should never run elevated.
  - **Crashes an error boundary can't catch:** the SSH code parses whatever a server sends, and a stack overflow or a fatal error in it would end every feature in the host.
- **Who starts it:** the host, while Remote Folders is on, and never with admin rights: a host elevated through UAC starts it with the token of the Windows shell, as it starts programs for features; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs). One runs per user session. It exits when the feature is turned off, when the host asks it to, and when the host's process ends. When it ends unexpectedly, the host starts it again after 1, 10 and 60 seconds; after a fourth crash within an hour, it waits until the settings change, with a notification and the reason on the page.
- **While it isn't running,** folders stay registered: files on the PC open, online-only files fail with Windows' message that the provider isn't running, and folders never opened look empty. Turning the feature off says so.
- **In the host:**
  - **the UI thread** runs `Apply`, which starts or stops the process on the thread pool, and builds the icon's submenu from the last status;
  - **the thread pool** reads the provider's status and requests for notifications from its pipe.
- **In `PCCompanion.RemoteFolders.exe`:**
  - **the main thread** reads the settings, which it watches as the host does, registers and connects each server's folder (`CfConnectSyncRoot`), serves the pipe, and waits;
  - **Windows' callback threads** bring requests from File Explorer and programs: list a folder, download a file, a rename or a delete. In the spike they came on Windows' own threads, up to 3 at once, with no message loop. A callback only checks the request and hands it to the server's worker, then returns; the worker answers with `CfExecute` when it's done. Nothing waits for the network on a callback thread;
  - **each server's worker,** async on the thread pool, with up to 3 SSH connections: one for listings and changes, and two for downloads. SSH.NET reads each connection on a thread of its own;
  - **the upload queue,** one upload at a time per server, at background priority;
  - **the folder watch,** `FileSystemWatcher` on the thread pool, which only queues work;
  - **an STA thread** for moving downloaded copies to the Recycle Bin with `IFileOperation`.
- **The settings app** never connects to a server itself: **Connect** in the dialog, and **Trust the new key**, ask the provider through its pipe. So SSH.NET is loaded only in `PCCompanion.RemoteFolders.exe`.
- **The pipe:** `PCCompanion.RemoteFolders.<session ID>`, which only the user's own account and the system can open, and which rejects remote clients. It serves the status, the test of a server's details for the dialog, which may carry a password, never logged, and requests from the page: **Sync now**, **Sign in again**, **Trust the new key**. The provider checks each request: a server must be in the settings, and a new key is trusted only if it's the key the server shows at that moment.
- **Failures:** every callback and worker is guarded. A failure in one server's work logs a warning and puts that server in an error state; the others go on. Callbacks always get an answer, even when the work fails, so Windows never waits out its 60 seconds.

## Running as administrator

- The provider always runs with the user's own rights; see [Threads and processes](#threads-and-processes). So Remote Folders keeps working while the host runs as administrator, unlike features that pause then.
- With UAC off, every program of the user's runs with full rights, the provider too.

## Settings

- **Feature page:**
  - the on/off switch;
  - **Servers,** a list with **Add server**; for each server its name, `user@address:port`, the folder on the server, its state, its problem if any, with **Sign in again** or **Trust the new key** for those problems, the changes waiting, conflicts, changes that couldn't be made and entries that can't be shown, **Open folder**, **Sync now**, and **More options** with **Edit** and **Remove**;
  - where the folders are, with **Open Remote Folders**;
  - a note that files that are online only can't be opened while Remote Folders is off or PC Companion isn't running;
  - a **Permissions & data** card.
- **The server dialog:** **Name**, **Address**, **Port**, **User name**, **Sign in with**, **Password** or **Key file** with **Browse** and **Passphrase**, **Connect**, then the server's key with **I checked this key**, and **Folder on the server**. Problems show below their box, and **Save** is available only when there are none.
- **Add server**, **Connect** and **Trust the new key** are available while Remote Folders is on, since they ask the provider.
- When the feature is first turned on, there are no servers.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read turns the feature off with a warning in the log. A server with a value this version doesn't know keeps it when the settings are saved, and isn't used.

```json
"features": {
  "remoteFolders": {
    "enabled": true,
    "servers": [
      {
        "id": "7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e",
        "name": "Home server",
        "protocol": "Sftp",
        "host": "nas.example.lan",
        "port": 22,
        "userName": "ana",
        "signIn": "Key",
        "keyFile": "C:\\Users\\Ana\\.ssh\\id_ed25519",
        "remoteFolder": "/home/ana",
        "folder": "C:\\Users\\Ana\\Remote Folders\\Home server",
        "hostKey": { "type": "ssh-ed25519", "sha256": "cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI" }
      }
    ]
  }
}
```

- `protocol`: `Sftp`; WebDAV will add a value. `signIn`: `Password` or `Key`; `keyFile` is set only for `Key`.
- `folder` is set when the server is added, and never changes.
- `hostKey` is the confirmed key. A server without it isn't connected.
- The sync root's ID is `PCCompanion!<user SID>!<server id>`, as Windows requires.

## Network

- PC Companion connects only to the servers the user adds, with SSH, to the address and port given. Windows resolves the server's name. It connects when a folder is listed, a file is downloaded or uploaded, a change waits, a folder kept on this device is looked at again every 10 minutes, the user selects **Connect**, **Sync now** or **Sign in again**, or after a failure as in [Offline and reconnecting](#offline-and-reconnecting). It sends keepalives every 30 seconds while connected, and closes the connection after 5 minutes without activity.
- It sends the server the user name and the password or a signature with the key, the names and contents of the files the user saves there and the changes they make, and nothing about the PC. It makes no other connection for this feature.
- **`PRIVACY.md`** gets a line:
  - "**Remote Folders:** connects only to the servers you add, with SSH, when you open, save or change files in their folders, every 10 minutes for folders you keep on this device, and when you select **Sync now**, and checks each server's key against the one you confirmed. It sends the server only your sign-in and the files and changes you make there. Your servers' addresses, user names, folders and confirmed keys are in `settings.json`; passwords and key passphrases are in Windows Credential Manager, for this PC only; your key files are only read. Files you open are kept in your `Remote Folders` folder, and changes made while a server can't be reached wait in `remote-folders`, with the files' names, until the server has them. Every downloaded file gets a Mark of the Web, without the server's address. The logs hold no server addresses, user names, folders or file names."
  - and `remote-folders` in the list of files PC Companion stores.

## Installer

- **New files** in the install folder: `PCCompanion.RemoteFolders.exe`, with `Renci.SshNet.dll` and `BouncyCastle.Cryptography.dll`, both MIT. `ThirdPartyNotices.txt` and `NOTICE.md` list them.
- **Closing before an upgrade or uninstall:** the host's `--exit` stops the provider and waits for it, as it waits for the settings app, and `CloseApplication` covers `PCCompanion.RemoteFolders.exe` too.
- **Uninstalling** for the user who uninstalls: the host's `--remove-data`, which runs as the user, also disconnects and unregisters each of the user's PC Companion folders, as **Remove** does, and deletes the credentials. Online-only files disappear; files on the PC, with waiting changes, stay, in ordinary folders. With `KEEPSETTINGS=1`, the servers stay in the settings, and after a new install their folders are registered again and ask for their passwords again.
- **Other users of an all-users install:** their folders would stay registered without a provider, and Windows would refuse to open their online-only files. The elevated uninstall step, which runs as SYSTEM, also unregisters every sync root whose ID starts with `PCCompanion!`, for every user. Their credentials stay in their own vaults, which only they can open, like their data folders.
- Nothing is registered by the installer: each user's folders are registered by the app, per user, without admin rights.

## Packages and projects

- **SSH.NET** 2026.0.0 (`SSH.NET`, MIT), with its dependency **BouncyCastle.Cryptography** (MIT), approved on 2026-11-14. Only `PCCompanion.RemoteFolders` references them, behind an interface of PC Companion's own, so tests use a fake server and WebDAV can be added later.
- **`PCCompanion.RemoteFolders`,** an executable without a UI framework, with the sync logic; `PCCompanion.Modules.Files` has the feature, which starts and stops the process, and the settings record. Its tests are in `tests/PCCompanion.RemoteFolders.Tests`.
- The Cloud Files functions come from CsWin32, which the solution already uses, and `Windows.Storage.Provider` from the Windows SDK projection that the target framework gives.

## Not included

- **WebDAV,** with a client of PC Companion's own on `HttpClient`, Windows' certificate checks, and a self-signed certificate only after the user confirms its fingerprint: a later issue.
- **FTP and FTPS**, **SMB**, which Windows has, and cloud storage services.
- **The SSH agent** and **Pageant**, through the `SshNet.Agent` package: a later issue.
- **Sign-ins with more than a password**, such as one-time codes, and **jump hosts**.
- **Thumbnails of online-only files,** which would need a download or the server's help, and a **Search** handler.
- **PC Companion's own commands** at the top of the Windows 11 menu and **the cloud status flyout** in File Explorer, which need package identity and so a signing certificate ([#19](https://github.com/Lazyb0y/pc-companion/issues/19)). The full menu, **Show more options**, has Windows' own items.
- **Streaming large files** while they download (`Progressive` hydration), which media players would use.
- **Choosing where the folders are,** and **locking files** on the server while they're open.
- **Changes pushed by the server:** SFTP has none, so PC Companion looks when a folder is listed, and every 10 minutes for folders kept on this device.
- **Another zone** than Internet for the Mark of the Web, such as Local intranet for the user's own NAS.
- **Saving without the password,** with a question at each sign-in.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **Every API is documented** and in every supported version: the Cloud Files API (`cldapi.dll`, since Windows 10 version 1709) and `StorageProviderSyncRootManager` (since version 1709), `CredWrite`, `CredRead`, `CredDelete` and `CredEnumerate`, `ReadDirectoryChangesW` and `IFileOperation`.
- The user profile must be on an NTFS drive.
- The spike ran on Windows 11 24H2 only, so Windows 10 is tested by hand. ARM64 needs nothing else: SSH.NET and BouncyCastle are managed code.

## Security and privacy

- **What a malicious server can do:**
  - send any names: they're checked as in [Names from the server](#names-from-the-server), and Windows creates every entry inside the folder, so no name can reach another place on the PC;
  - send very long listings: 100,000 entries at most per folder;
  - send huge or never-ending files: a file downloads only when a program opens it, and only as many bytes as the placeholder says; Windows shows the download and the user can cancel it;
  - lie about sizes or change files during a download: the download fails, and nothing partial is kept as the file;
  - send links in loops: they're followed only one level at a time, when the user opens a folder, never when downloading a kept folder;
  - send malformed protocol data: SSH.NET parses it in `PCCompanion.RemoteFolders.exe`, which runs without admin rights and is started again if it crashes; the host and the other features keep running;
  - be slow: every wait has a limit, and callbacks never wait for the network;
  - serve harmful files: every downloaded file has a Mark of the Web, so SmartScreen, Office's Protected View and Windows' warnings apply; PC Companion never opens or runs a file;
  - see what the user saves there, which is the point of the feature.
- **Mark of the Web:** PC Companion writes `Zone.Identifier` with `ZoneId=3`, the Internet zone, on each file's placeholder when it's created. The spike showed that this doesn't download the file, doesn't change its in-sync state, and stays when the file is downloaded. It has no `HostUrl`, so the server's address isn't passed on, such as to SmartScreen. Files created on the PC keep whatever mark they have.
- **Server keys:** see [Server keys](#server-keys). The password is sent only after the server's key matches, so a server that pretends to be the user's gets nothing.
- **Credentials:** in Credential Manager for this PC only, never in files or logs. Key files are only read. The pipe carries a password only for **Connect** in the dialog, and only to the provider.
- **Other programs on the PC:** they run as the user and can read the user's files anyway; the folders are in the user's profile, which other users can't open. Any program running as the user can change `settings.json`, for example to add a server, but it could also send the files itself, so this crosses no boundary.
- **Files on the PC:** downloaded files are ordinary files in the user's profile. Anyone who can read the drive can read them, as with the user's other files.
- **Rights:** the provider never runs as administrator, and neither registering nor using the folders needs it. The uninstall step that runs as SYSTEM only asks Windows to unregister folders with PC Companion's IDs, and deletes no file itself.
- **Logs:** Debug entries when the settings are applied, with how many servers, when the provider starts and stops, and for each server, by its number in the list, its state changes, listings with how many entries were added, changed, removed and hidden, downloads and uploads with their size and time, conflicts, and the journal's length. Warnings with the error's kind and code: SSH.NET's exception type, the SFTP status, and Windows' `HRESULT`. Never server addresses, user names, folders on the server, file names, paths, key fingerprints, passwords or passphrases.
- **Data:** `remote-folders\<server ID>.json` holds the journal of changes made while the server couldn't be reached, with the files' names, until they're made on the server, and the conflicts and changes that couldn't be made, until the user opens them. Removing a server deletes its file, and uninstalling removes the folder with the rest of the data folder.

## Performance

- **Nothing runs while nothing happens:** no polling, except folders kept on this device, every 10 minutes while the server can be reached, and keepalives while a connection is open.
- **Callbacks** are answered in milliseconds, and listings in the time the server takes: in the spike, 1,000 entries took 65 to 83 ms on the same PC.
- **Downloads and uploads** ran at about 76 MB/s over one connection on the same PC; a 50 MB file opened through File Explorer's path took 0.95 s. Real networks are limited by their speed and latency, tested by hand.
- **The host** only starts the process and reads its status.

## Accessibility and localization

- **The page and the dialog** work with the keyboard alone, and keyboard focus stays on a server when the list changes. Each server's state is part of its name for screen readers, such as "Home server, connected, 2 changes waiting", and changes of state are announced. Problems below a box are announced.
- **The server's key** is shown with its type and fingerprint as text that can be selected and copied, in a monospaced font, left to right in every language.
- **Notifications** are Windows' own, which screen readers read and Do not disturb holds back. Everything a notification offers is also on the page.
- **Colors and layout:** the page follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages. Addresses, paths and fingerprints are shown left to right in every language.
- **Strings:** translated like the other pages', marked "Needs native review," including the conflict copies' names, whose date and time follow the user's regional settings, with characters Windows allows in names.

## Spike results

Two spikes in Windows Sandbox (Windows 11 24H2, build 26100, Cloud Files integration number `0x628`), with .NET 10 programs outside the repository.

- **Registering without package identity:**
  - `StorageProviderSyncRootManager.Register` from an unpackaged program worked as a standard user without admin rights. It wrote `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager\<ID>`, with the user's SID and folder under `UserSyncRoots`, and the navigation pane's entry in the user's own registry. `Unregister` removed all of it.
  - File Explorer showed the folder in its navigation pane with its name and icon, the Status column, and **Always keep on this device** and **Free up space** in the Windows 11 menu.
  - In Windows Sandbox, `Register` fails on the Sandbox's own C: drive with `ERROR_NOT_A_CLOUD_FILE` (376), and works on a virtual NTFS disk. `CfRegisterSyncRoot` works on both.
- **Callbacks** came on Windows' own threads, not .NET's thread pool, in the MTA, up to 3 at once, with no message loop, and named the program that asked.
- **Listings:**
  - File Explorer's window stays blank until the folder's listing is answered; it took 8 seconds when the provider waited 8 seconds.
  - A listing answered without marking the folder complete is asked again each time a program lists the folder; entries already there come back as `ERROR_ALREADY_EXISTS`, harmlessly, and a file added on the server meanwhile appeared.
  - A listing that fails with `STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE` gives programs an empty folder, and File Explorer the message "Location is not available … The cloud sync provider failed to perform the operation due to network being unavailable." Folders listed before keep their entries.
- **Downloads:** a download that fails with `STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE` gives the program error 388 with that message. A 5 MB file came in 6 transfers in 8 ms from a local folder, and a 50 MB file over SFTP in 51 transfers in 847 ms.
- **When the provider isn't connected:** an online-only file fails with error 362, "The cloud file provider is not running"; a folder never listed looks empty, without an error; new files can be created; and deleting an online-only file succeeds without telling the provider.
- **Changes on the PC:**
  - Writing to a downloaded file cleared its in-sync state and reported the changed bytes. `CfSetInSyncState`, `CfConvertToPlaceholder`, `CfHydratePlaceholder` and `CfDehydratePlaceholder` all worked with a handle opened only to read attributes.
  - **Always keep on this device** and **Free up space** only set the file's attributes: the provider downloads and removes the copies.
  - A rename inside the folder came with both paths, and the file showed as waiting until the provider marked it in sync. Moving a file out of the folder downloaded it, then came as a delete. The Recycle Bin deleted an online-only file for good without downloading it, and a downloaded one came as a delete, then a rename into `$RECYCLE.BIN`.
  - A rename or delete the provider refused gave the program error 389, "The cloud operation was unsuccessful".
- **Changes on the server:** `CfUpdatePlaceholder` with `VERIFY_IN_SYNC`, `MARK_IN_SYNC` and `DEHYDRATE`, on a handle from `CfOpenFileWithOplock`, removed the old copy of a file changed on the server, and the next read downloaded the new one. On a file with local changes it failed with `ERROR_CLOUD_FILE_NOT_IN_SYNC` (377) and kept them.
- **Mark of the Web:** `Zone.Identifier` written on an online-only placeholder didn't download it, kept it in sync, and stayed after the download.
- **Thumbnails:** File Explorer's large icons showed an online-only JPEG with the type's icon, without downloading it.
- **Unregistering** kept downloaded files as ordinary files and deleted online-only ones.
- **SSH.NET against OpenSSH for Windows 10.4p1:**
  - Key exchange `mlkem768x25519-sha256` and an Ed25519 server key; SSH.NET chose AES-CTR over AES-GCM and ChaCha20 by default, so PC Companion sets the order.
  - Signing in took about 200 ms with a password or a key, and 839 ms the first time, while the server, here Windows, created the user's profile. A wrong password failed in 90 ms; a key's wrong passphrase failed when loading it.
  - A server key that didn't match the confirmed one stopped the connection in 55 ms, before signing in.
  - Plain SFTP rename over an existing file fails; `posix-rename@openssh.com` replaces it.
  - A stopped server dropped the connection at once; connecting was refused after about 2 seconds, and connecting again after it was back took 142 ms.
- **Not checked yet,** in the implementation's own spike or by hand: Windows 10; the provider without admin rights while the host has them, with UAC on; registering again with a new name; renames and moves while the provider isn't connected; unregistering another user's folders as SYSTEM; a virtual disk mounted at the profile's `Remote Folders` folder for the Sandbox checks; File Explorer's question before deleting an online-only file; saves by Office and other apps; Storage Sense; Windows Search; long paths; real networks; Linux servers and NAS servers; and keyboard-interactive sign-in.

## Testing

- **Unit tests,** with a fake SFTP server behind PC Companion's interface, a fake Cloud Files layer and a fake clock:
  - **names:** every rule in [Names from the server](#names-from-the-server), case-only duplicates, invalid UTF-8, and the 100,000-entry limit;
  - **listings:** new, changed and deleted entries, the 30-second reuse, the 5- and 10-second waits, and links;
  - **downloads:** ranges, short and long files, a stalled server, cancellation, and progress reports;
  - **uploads:** the temporary name and rename, a server without `posix-rename`, a file changed during the upload, the files that stay on the PC, and locked files;
  - **conflicts:** a file changed on both sides, a delete of a changed server file, and the conflict copy's name in several languages;
  - **the journal:** order, the checks, skipped entries, and changes made while PC Companion wasn't running;
  - **states and retries:** unreachable, refused, sign-in failed, key changed, the backoff, and network changes;
  - **server keys:** the check, the pinned key type, and the refused algorithms;
  - **the pipe:** its messages and its checks of requests;
  - **the settings:** defaults, the spec's example, unknown values, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/RemoteFolders.ps1`), with OpenSSH for Windows installed in the Sandbox only, from a release pinned by version and SHA-256, which `sandbox-test.ps1` downloads once and keeps outside the repository; a test account with a password and a key; and a virtual NTFS disk mounted at `%USERPROFILE%\Remote Folders`, since Windows Sandbox can't register folders on its own drive. The connections stay inside the Sandbox, which keeps networking off:
  - **adding:** the dialog adds a server with a password and one with a key, shows the key's fingerprint, and saves only once it's confirmed; the folder appears in the navigation pane's registry and in File Explorer;
  - **files:** listing, opening, **Always keep on this device**, **Free up space**, saving a change, a new file and folder, a rename, a move, a delete, the Recycle Bin, and a move out of the folder, each checked on the server;
  - **server changes:** a file added, changed and deleted on the server shows on the next listing; a kept file is downloaded again;
  - **conflicts:** a file changed on both sides gives a conflict copy on the server and on the PC;
  - **offline:** with the server stopped, an online-only file fails with error 388, and the next request at once; an edit waits, a rename goes to the journal, and both reach the server when it's back;
  - **refusals:** a wrong password gives **Sign-in failed**, and a confirmed key changed in `settings.json` gives **Server key changed**, with nothing sent;
  - **the provider:** ending its process starts it again, and the host's **Exit** stops it;
  - **removing:** the folder is unregistered, files on the PC stay, and the credential is gone;
  - **Mark of the Web** on downloaded files, without `HostUrl`;
  - **logs** without the server's address, the user name or file names;
  - **uninstalling** unregisters the folders and deletes the credentials;
  - **the page** in English and Arabic, light and dark, with screenshots;
  - no feature holds the host's UI thread, as every group checks.
- **What the Sandbox can't check:** Windows 10, UAC on, real networks, Linux and NAS servers, sleep, and other users.
- **By hand:**
  - a Linux server with OpenSSH, and a NAS, over the local network and over the internet, with a password and a key;
  - large files of several GB, and a slow or lossy connection such as Wi-Fi or a VPN;
  - sleep and wake, switching networks, and pulling the network cable during a download and an upload;
  - saving from Word, Excel, Notepad, Paint and a code editor, and File Explorer's question before deleting;
  - Storage Sense, Windows Search, Microsoft Defender scans, and long paths;
  - Always run as administrator with UAC on, where the provider runs without admin rights;
  - two users on an all-users install, and uninstalling it;
  - Narrator on the page and the dialog, a right-to-left language, Windows 10 and ARM64.
