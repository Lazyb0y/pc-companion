using System;
using System.Collections.Generic;
using PCCompanion.Core.Features;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>A profile the menu shows.</summary>
/// <param name="Number">Its number in the profile list, from 1, which is also its item's ID.</param>
/// <param name="CanBeUsed">Whether every display that's on in it is connected now.</param>
public sealed record MenuProfile(int Number, DisplayProfile Profile, bool CanBeUsed);

/// <summary>
/// The Display profile submenu of the PC Companion icon's menu. See "Behavior" in
/// docs/features/display/display-profiles.md.
/// </summary>
public static class DisplayProfilesMenu
{
    /// <summary>The ID of Display Profiles settings; profiles' IDs are their numbers.</summary>
    public const int SettingsItem = 1000;

    /// <summary>The submenu, or nothing when there are no profiles.</summary>
    /// <param name="inUse">The number of the profile whose setup is in use, if any.</param>
    /// <param name="known">Whether the displays have been read, so a missing match means another setup.</param>
    /// <param name="remote">Whether this is a remote session, where profiles can't be used.</param>
    public static IReadOnlyList<HostMenuItem> Build(IReadOnlyList<MenuProfile> profiles, int? inUse, bool known, bool remote)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return [];
        }

        var items = new List<HostMenuItem>();
        if (remote)
        {
            items.Add(new HostMenuItem(0, Strings.DisplayProfilesMenuRemote) { IsEnabled = false });
        }
        else if (known && inUse is null)
        {
            items.Add(new HostMenuItem(0, Strings.DisplayProfilesMenuOther) { IsEnabled = false });
        }

        foreach (var profile in profiles)
        {
            items.Add(new HostMenuItem(profile.Number, profile.Profile.Name.Trim())
            {
                IsChecked = !remote && profile.Number == inUse,
                IsEnabled = !remote && profile.CanBeUsed,
            });
        }

        items.Add(HostMenuItem.Separator);
        items.Add(new HostMenuItem(SettingsItem, Strings.DisplayProfilesMenuSettings));
        return [HostMenuItem.Submenu(Strings.DisplayProfilesMenu, items)];
    }
}
