<!--
  Drafted by the release workflow from .github/release-notes.md. Before publishing, rewrite the
  generated list of pull requests at the end into the sections docs/development/releasing.md
  describes. Pushing the release branch again replaces this draft and its notes.
-->

## Download

| Installer | SHA-256 |
|---|---|
{{installers}}

Most PCs need the x64 installer. PCs with an Arm processor, such as a Snapdragon, need the ARM64 one: **Settings > System > About** shows which under **System type**. PC Companion runs on Windows 11 and on Windows 10 version 2004 or later.

### Check your download

The installers aren't signed yet, so Windows SmartScreen may say it doesn't recognize the app; select **More info**, then **Run anyway**. To check that your download is the one published here, compare its SHA-256 hash with the table above, ignoring capital letters:

```powershell
Get-FileHash .\PCCompanion-{{version}}-x64.msi
```

Each file also has a build attestation, which shows that this repository's release workflow built it. With the [GitHub CLI](https://cli.github.com):

```powershell
gh attestation verify .\PCCompanion-{{version}}-x64.msi -R Lazyb0y/pc-companion
```

## Install

- Run the installer and select **Install**. PC Companion installs just for you, without admin rights, in `%LOCALAPPDATA%\Programs\PCCompanion`.
- To install it for all users, in `C:\Program Files\PCCompanion`, select **Advanced**. **Always run as administrator** needs an install for all users.
- A newer version installs over an older one and keeps your settings.
- To switch between installing just for you and for all users, uninstall PC Companion first.
- To install silently, just for you: `msiexec /i PCCompanion-{{version}}-x64.msi /qn`. Add `ALLUSERS=2 MSIINSTALLPERUSER=""` to install for all users.

## Uninstall

Remove PC Companion in **Settings > Apps > Installed apps**. Uninstalling also removes your settings and logs. To keep your settings, run the installer, choose **Remove** and check **Keep my settings**.

## Your data

PC Companion keeps its settings and logs on your PC, in `%LOCALAPPDATA%\PCCompanion`. It has no telemetry or accounts. Once a day it asks GitHub whether a newer version is out; **General > Check for updates** turns that off. See [PRIVACY.md](https://github.com/Lazyb0y/pc-companion/blob/main/PRIVACY.md).

