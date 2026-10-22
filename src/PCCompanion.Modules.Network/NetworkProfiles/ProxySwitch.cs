using System;
using System.ComponentModel;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>How a switch to a profile ended.</summary>
public enum SwitchResult
{
    /// <summary>Windows uses the profile's settings now.</summary>
    Switched,

    /// <summary>Windows reported the change, but its settings don't match the profile.</summary>
    NotApplied,

    /// <summary>Group Policy manages the proxy settings, so nothing was changed.</summary>
    Managed,

    /// <summary>Windows refused the change.</summary>
    Refused,
}

/// <param name="After">Windows' settings after the switch, when they were read.</param>
/// <param name="Restrictions">The policies, for <see cref="SwitchResult.Managed"/>.</param>
/// <param name="Error">The Windows error, for <see cref="SwitchResult.Refused"/>.</param>
public sealed record SwitchOutcome(SwitchResult Result, ProxySettings? After = null, ProxyRestrictions Restrictions = ProxyRestrictions.None, int Error = 0);

/// <summary>
/// Switches Windows' proxy settings to a profile's, from the menu or the page alike. See
/// "Changing the settings" in docs/features/network/network-profiles.md.
/// </summary>
public static class ProxySwitch
{
    /// <summary>
    /// Reads the policies, and when none manages the settings, writes the profile's and reads them
    /// back, since Windows can report a change it didn't make. Reads and writes the registry, so
    /// call it on the thread pool.
    /// </summary>
    public static SwitchOutcome Apply(INetworkProfilesSystem system, ProxyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(profile);

        var restrictions = system.ReadRestrictions();
        if (restrictions != ProxyRestrictions.None)
        {
            return new SwitchOutcome(SwitchResult.Managed, Restrictions: restrictions);
        }

        try
        {
            system.WriteProxy(ProxyMatching.ToWindows(profile));
            var after = system.ReadProxy();
            return new SwitchOutcome(ProxyMatching.Matches(profile, after) ? SwitchResult.Switched : SwitchResult.NotApplied, after);
        }
        catch (Win32Exception exception)
        {
            return new SwitchOutcome(SwitchResult.Refused, Error: exception.NativeErrorCode);
        }
    }
}
