# Windows support

- Windows 11 (all versions) and Windows 10 version 2004 (build 19041) or later.
- Minimum version `10.0.19041.0`, set as `TargetPlatformMinVersion`, `SupportedOSPlatformVersion` and the package manifest minimum.
- x64 and ARM64; no x86. ARM64 isn't tested on hardware yet.
- Guard newer APIs with version checks. Analyzer CA1416 flags unguarded calls.
- Undocumented APIs are allowed only behind version-specific implementations that disable the feature on unknown builds. Re-test them on each new Windows release.
- Handle missing OS features gracefully, and document each feature's OS requirements.
- Windows 10 has no Mica backdrop: the settings window uses desktop acrylic there, and the solid Mica fallback colors when neither is available.
