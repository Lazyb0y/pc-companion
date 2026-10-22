using System;
using System.Collections.Generic;
using PCCompanion.Core.Features;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>A profile the menu shows.</summary>
/// <param name="Number">Its number in the profile list, from 1, which is also its item's ID.</param>
public sealed record UsableProfile(int Number, NetworkProfile Profile);

/// <summary>
/// The Network profile submenu of the PC Companion icon's menu. See "Behavior" in
/// docs/features/network/network-profiles.md.
/// </summary>
public static class NetworkProfilesMenu
{
    /// <summary>The ID of Network Profiles settings; profiles' IDs are their numbers.</summary>
    public const int SettingsItem = 1000;

    /// <summary>
    /// The submenu, or nothing when there are no profiles.
    /// </summary>
    /// <param name="inUse">The number of the profile whose settings Windows uses, if any.</param>
    /// <param name="known">Whether Windows' settings have been read, so a missing match means other settings.</param>
    /// <param name="managed">Whether Group Policy manages the proxy settings, so profiles can't be chosen.</param>
    public static IReadOnlyList<HostMenuItem> Build(IReadOnlyList<UsableProfile> profiles, int? inUse, bool known, bool managed)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return [];
        }

        var items = new List<HostMenuItem>();
        if (managed)
        {
            items.Add(new HostMenuItem(0, Strings.NetworkProfilesMenuManaged) { IsEnabled = false });
        }
        else if (known && inUse is null)
        {
            items.Add(new HostMenuItem(0, Strings.NetworkProfilesMenuOther) { IsEnabled = false });
        }

        foreach (var profile in profiles)
        {
            items.Add(new HostMenuItem(profile.Number, profile.Profile.Name.Trim()) { IsChecked = profile.Number == inUse, IsEnabled = !managed });
        }

        items.Add(HostMenuItem.Separator);
        items.Add(new HostMenuItem(SettingsItem, Strings.NetworkProfilesMenuSettings));
        return [HostMenuItem.Submenu(Strings.NetworkProfilesMenu, items)];
    }
}
