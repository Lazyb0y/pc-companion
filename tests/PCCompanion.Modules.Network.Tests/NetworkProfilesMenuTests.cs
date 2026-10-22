using System.Collections.Generic;
using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;
using Xunit;

namespace PCCompanion.Modules.Network.Tests;

public sealed class NetworkProfilesMenuTests
{
    private static readonly UsableProfile[] Profiles =
    [
        new(1, new NetworkProfile { Name = " Office " }),
        new(3, new NetworkProfile { Name = "Home" }),
    ];

    [Fact]
    public void WithoutProfilesThereIsNoSubmenu()
    {
        Assert.Empty(NetworkProfilesMenu.Build([], inUse: null, known: true, managed: false));
    }

    [Fact]
    public void TheProfileInUseHasTheRadioMark()
    {
        var submenu = Assert.Single(NetworkProfilesMenu.Build(Profiles, inUse: 3, known: true, managed: false));

        Assert.Equal(Strings.NetworkProfilesMenu, submenu.Text);
        Assert.Equal(
            ["1 Office", "3 Home (checked)", "-", $"{NetworkProfilesMenu.SettingsItem} {Strings.NetworkProfilesMenuSettings}"],
            submenu.Items!.Select(Describe));
    }

    [Fact]
    public void OtherSettingsAreNotedWhenNoProfileMatches()
    {
        var submenu = Assert.Single(NetworkProfilesMenu.Build(Profiles, inUse: null, known: true, managed: false));

        Assert.Equal(
            [$"0 {Strings.NetworkProfilesMenuOther} (dimmed)", "1 Office", "3 Home", "-", $"{NetworkProfilesMenu.SettingsItem} {Strings.NetworkProfilesMenuSettings}"],
            submenu.Items!.Select(Describe));
    }

    [Fact]
    public void BeforeTheFirstReadNothingIsNotedOrMarked()
    {
        var submenu = Assert.Single(NetworkProfilesMenu.Build(Profiles, inUse: null, known: false, managed: false));

        Assert.Equal(["1 Office", "3 Home", "-", $"{NetworkProfilesMenu.SettingsItem} {Strings.NetworkProfilesMenuSettings}"], submenu.Items!.Select(Describe));
    }

    [Fact]
    public void ManagedSettingsDimTheProfiles()
    {
        var submenu = Assert.Single(NetworkProfilesMenu.Build(Profiles, inUse: 1, known: true, managed: true));

        Assert.Equal(
            [$"0 {Strings.NetworkProfilesMenuManaged} (dimmed)", "1 Office (checked) (dimmed)", "3 Home (dimmed)", "-", $"{NetworkProfilesMenu.SettingsItem} {Strings.NetworkProfilesMenuSettings}"],
            submenu.Items!.Select(Describe));
    }

    internal static string Describe(HostMenuItem item) =>
        item.IsSeparator ? "-" : $"{item.Id} {item.Text}{(item.IsChecked ? " (checked)" : "")}{(item.IsEnabled ? "" : " (dimmed)")}";

    internal static IEnumerable<string> Describe(IReadOnlyList<HostMenuItem> items) =>
        items.Count == 0 ? [] : items[0].Items!.Select(Describe);
}
