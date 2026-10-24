# PC Companion

A free, open-source collection of utilities inspired by macOS and Linux, bringing useful features to Windows to enhance productivity, customization, and everyday workflows.

This project is in early development; its releases are previews.

Website: [lazyb0y.github.io/pc-companion](https://lazyb0y.github.io/pc-companion/)

## Features

Each feature is off until you turn it on in the settings app.

- **[Hot Corners](docs/features/input/hot-corners.md):** move the pointer into a corner of the screen to show all windows, show the desktop, lock the PC, start a program and more. **Keep displays on** stops the screen saver and the display timeout while the pointer rests in its corner.
- **[App Windows](docs/features/windows/app-windows.md):** press Alt and the key above Tab to switch between the windows of the app in front, such as File Explorer windows.
- **[System Meters](docs/features/desktop/system-meters.md):** see how busy your PC is, with live CPU, memory and network use on the taskbar, or with an icon for each meter in the notification area. Select the meters to open Task Manager.
- **General:** the language, light or dark theme, starting with Windows, and logs. [Always run as administrator](docs/features/general/run-as-administrator.md) lets features work in front of apps that run as administrator, and the [update check](docs/features/general/update-check.md) tells you when a new version is out.

PC Companion is in English, Arabic, Bangla, Chinese (Simplified), French, German and Spanish. The translations still need review by native speakers; corrections are welcome.

## Requirements

- Windows 11, or Windows 10 version 2004 or later.
- A PC with an x64 or ARM64 processor.

## Download and install

1. Open the latest release on [Releases](https://github.com/Lazyb0y/pc-companion/releases) and download `PCCompanion-<version>-x64.msi`. PCs with an Arm processor, such as a Snapdragon, need the ARM64 one: **Settings > System > About** shows which under **System type**.
2. Run it and select **Install**. PC Companion installs just for you, without admin rights. **Advanced** installs it for all users, which **Always run as administrator** needs.
3. The installers aren't signed yet, so Windows SmartScreen may say it doesn't recognize the app; select **More info**, then **Run anyway**. The release notes show how to check that your download is the one published, with its SHA-256 hash and build attestation.

To uninstall, remove PC Companion in **Settings > Apps > Installed apps**. This also removes your settings and logs; [PRIVACY.md](PRIVACY.md) says how to keep your settings.

## Updates

Once a day, PC Companion asks GitHub whether a newer version is out. When there is one, a notification and a banner on the **General** page open its release page. Download and install it as above: it installs over the old version and keeps your settings. **General > Check for updates** turns the check off, and **Check now** asks at once. Each release's notes say what changed.

## Getting help

See [SUPPORT.md](SUPPORT.md) for questions, bug reports and feature requests.

## Contributing

Contributions are welcome. Read the [contributing guide](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md). Documentation is in [docs](docs/README.md).

## Privacy and security

PC Companion collects no data, and connects only to GitHub, to check for updates; see [PRIVACY.md](PRIVACY.md). To report a vulnerability, follow [SECURITY.md](SECURITY.md).

## License

PC Companion is licensed under the [MIT License](LICENSE). Third-party components are listed in [NOTICE.md](NOTICE.md).
