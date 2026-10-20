# Windows support

- Windows 11 (all versions) and Windows 10 version 2004 (build 19041) or later.
- Minimum version `10.0.19041.0`, set as `TargetPlatformMinVersion`, `SupportedOSPlatformVersion` and the package manifest minimum.
- x64 and ARM64; no x86. ARM64 isn't tested on hardware yet.
- Guard newer APIs with version checks. Analyzer CA1416 flags unguarded calls.
- Undocumented APIs are allowed only behind version-specific implementations that disable the feature on unknown builds. Re-test them on each new Windows release.
  - The tray menu: Windows has no documented way to draw Win32 menus dark, so `MenuTheme` calls uxtheme.dll's functions 135 (`SetPreferredAppMode`) and 136 (`FlushMenuThemes`), which it exports by number only. It calls them only on builds 19041 to `MenuTheme.MaxTestedBuild`, now 26300; on other builds, the menu stays light and the host logs why. After testing a new Windows release, raise `MaxTestedBuild`.
  - The System Meters strip: it finds room on the taskbar from the UI Automation IDs and class names of the taskbar's buttons (`StartButton`, `SearchButton`, `TaskViewButton`, `WidgetsButton` and `Taskbar.TaskListButtonAutomationPeer`) and its `TrayNotifyWnd` child window, none of which is documented. It runs only on builds 22621 to `MeterStrip.MaxTestedBuild`, now 26300, and on Windows 10 builds 19041 to 19045, where it finds room from the taskbar's `ReBarWindow32` and `MSTaskSwWClass` child windows instead, with the app buttons in the task band; on other builds, the meters show as notification icons and the host logs why. Tested on builds 26100 (Windows Sandbox) and 26300; Windows 10 isn't tested yet. After testing a new Windows release, raise `MaxTestedBuild`.
- Handle missing OS features gracefully, and document each feature's OS requirements.
- Windows 10 has no Mica backdrop: the settings window uses desktop acrylic there, and the solid Mica fallback colors when neither is available.
