using System;
using Microsoft.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What Explorer records about the desktop that Windows has no documented way to read: the virtual
/// desktop in use, and whether Windows spotlight changes the background. They're read only, and only
/// on the Windows builds they were tested on; elsewhere they're unknown.
/// </summary>
public static class ShellRecords
{
    /// <summary>The newest Windows build the records were tested on. Raise it after testing a new release.</summary>
    public const int MaxTestedBuild = 26300;

    /// <summary>
    /// The key whose changes include a switch of virtual desktop, for a <see cref="RegistryWatch"/>
    /// under the user's key.
    /// </summary>
    public const string VirtualDesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    // Windows 11 version 22H2, the oldest build tested.
    private const int MinTestedBuild = 22621;

    private const string SpotlightKey = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";
    private const int GuidLength = 16;

    /// <summary>Whether the records can be read on this Windows build.</summary>
    public static bool AreReadable => IsTestedBuild(Environment.OSVersion.Version.Build);

    public static bool IsTestedBuild(int build) => build is >= MinTestedBuild and <= MaxTestedBuild;

    /// <summary>
    /// The virtual desktop in use; <see langword="null"/> when it's unknown, as with only one
    /// desktop or on an untested build. It reads the registry, so call it off the UI thread.
    /// </summary>
    public static Guid? CurrentVirtualDesktop() => AreReadable ? CurrentVirtualDesktop(Registry.CurrentUser) : null;

    /// <summary>The virtual desktop in use, as recorded in a user's key, for tests.</summary>
    public static Guid? CurrentVirtualDesktop(RegistryKey user)
    {
        ArgumentNullException.ThrowIfNull(user);

        using var key = user.OpenSubKey(VirtualDesktopsKey);
        return key?.GetValue("CurrentVirtualDesktop") is byte[] { Length: GuidLength } id ? new Guid(id) : null;
    }

    /// <summary>
    /// Whether Windows spotlight changes the desktop's background; <see langword="null"/> when
    /// it's unknown, as on an untested build. It reads the registry, so call it off the UI thread.
    /// </summary>
    public static bool? IsSpotlightOn() => AreReadable ? IsSpotlightOn(Registry.CurrentUser) : null;

    /// <summary>Whether Windows spotlight changes the background, as recorded in a user's key, for tests.</summary>
    public static bool IsSpotlightOn(RegistryKey user)
    {
        ArgumentNullException.ThrowIfNull(user);

        using var key = user.OpenSubKey(SpotlightKey);
        return key?.GetValue("EnabledState") is 1;
    }
}
