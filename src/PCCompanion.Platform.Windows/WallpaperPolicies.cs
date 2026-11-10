using System;
using Microsoft.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Whether an organization manages the desktop background with a policy, so apps mustn't change it:
/// <c>NoChangingWallPaper</c> (Prevent changing desktop background), the <c>Wallpaper</c> policy
/// (Desktop Wallpaper), in the user's or the machine's key, or the machine's
/// <c>PersonalizationCSP</c> background, which device management sets. Windows announces a change
/// of policies with <c>WM_SETTINGCHANGE</c> and <c>Policy</c>.
/// </summary>
public static class WallpaperPolicies
{
    private const string ActiveDesktop = @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
    private const string System = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PersonalizationCsp = @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

    /// <summary>Whether a policy manages the background. It reads the registry, so call it off the UI thread.</summary>
    public static bool AreSet() => AreSet(Registry.CurrentUser, Registry.LocalMachine);

    /// <summary>Whether a policy manages the background, in a user's and a machine's keys, for tests.</summary>
    public static bool AreSet(RegistryKey user, RegistryKey machine)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(machine);

        foreach (var root in new[] { user, machine })
        {
            if (Value(root, ActiveDesktop, "NoChangingWallPaper") is int and not 0
                || Value(root, System, "Wallpaper") is string { Length: > 0 })
            {
                return true;
            }
        }

        return Value(machine, PersonalizationCsp, "DesktopImagePath") is string { Length: > 0 }
            || Value(machine, PersonalizationCsp, "DesktopImageUrl") is string { Length: > 0 }
            || Value(machine, PersonalizationCsp, "DesktopImageStatus") is int and not 0;
    }

    private static object? Value(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name);
    }
}
