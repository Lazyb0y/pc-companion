using System;
using Microsoft.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>Group Policy settings that manage the proxy settings.</summary>
[Flags]
public enum ProxyRestrictions
{
    None = 0,

    /// <summary>Prevent changing proxy settings, for the user or the computer.</summary>
    PreventChanges = 1,

    /// <summary>Prevent changing the automatic configuration (setup script) settings, for the user or the computer.</summary>
    PreventScriptChanges = 2,

    /// <summary>
    /// Make proxy settings per-machine (rather than per-user): the settings are the computer's,
    /// for every user of the PC, and only a program running as administrator changes them.
    /// </summary>
    PerMachine = 4,
}

/// <summary>
/// Reads the Group Policy settings that manage the proxy settings. Windows Settings honors them,
/// but WinINet doesn't, so a program that changes the settings must check them itself. Reads the
/// registry, so don't call it on the host's UI thread. See "Managed settings" in
/// docs/features/network/network-profiles.md.
/// </summary>
public static class ProxyPolicies
{
    /// <summary>Where "Prevent changing proxy settings" is, under the user's and the computer's keys.</summary>
    public const string ControlPanelPath = @"Software\Policies\Microsoft\Internet Explorer\Control Panel";

    /// <summary>Where "Make proxy settings per-machine" is, under the computer's key.</summary>
    public const string PerMachinePath = @"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <exception cref="System.IO.IOException">The registry refused.</exception>
    /// <exception cref="UnauthorizedAccessException">The registry refused.</exception>
    /// <exception cref="System.Security.SecurityException">The registry refused.</exception>
    public static ProxyRestrictions Read() => Read(Registry.CurrentUser, Registry.LocalMachine);

    /// <summary>Reads the policies under other keys than the user's and the computer's, as tests do.</summary>
    public static ProxyRestrictions Read(RegistryKey currentUser, RegistryKey localMachine)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(localMachine);

        var restrictions = ProxyRestrictions.None;
        if (ReadNumber(currentUser, ControlPanelPath, "Proxy") == 1 || ReadNumber(localMachine, ControlPanelPath, "Proxy") == 1)
        {
            restrictions |= ProxyRestrictions.PreventChanges;
        }

        if (ReadNumber(currentUser, ControlPanelPath, "Autoconfig") == 1 || ReadNumber(localMachine, ControlPanelPath, "Autoconfig") == 1)
        {
            restrictions |= ProxyRestrictions.PreventScriptChanges;
        }

        if (ReadNumber(localMachine, PerMachinePath, "ProxySettingsPerUser") == 0)
        {
            restrictions |= ProxyRestrictions.PerMachine;
        }

        return restrictions;
    }

    // A DWORD value, or null when there's none.
    private static int? ReadNumber(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) as int?;
    }
}
