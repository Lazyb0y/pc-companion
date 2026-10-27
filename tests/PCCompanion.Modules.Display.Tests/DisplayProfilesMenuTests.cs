using System.Collections.Generic;
using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;
using Xunit;

namespace PCCompanion.Modules.Display.Tests;

public sealed class DisplayProfilesMenuTests
{
    private static readonly string Settings = $"{DisplayProfilesMenu.SettingsItem} {Strings.DisplayProfilesMenuSettings}";

    private static readonly MenuProfile[] Profiles =
    [
        new(1, new DisplayProfile { Name = " Desk " }, CanBeUsed: true),
        new(3, new DisplayProfile { Name = "TV & sofa" }, CanBeUsed: false),
    ];

    [Fact]
    public void WithoutProfilesThereIsNoSubmenu()
    {
        Assert.Empty(DisplayProfilesMenu.Build([], inUse: null, known: true, remote: false));
    }

    [Fact]
    public void TheProfileInUseHasTheRadioMarkAndOneWithoutItsDisplaysIsDimmed()
    {
        var submenu = Assert.Single(DisplayProfilesMenu.Build(Profiles, inUse: 1, known: true, remote: false));

        Assert.Equal(Strings.DisplayProfilesMenu, submenu.Text);
        Assert.Equal(["1 Desk (checked)", "3 TV & sofa (dimmed)", "-", Settings], submenu.Items!.Select(Describe));
    }

    [Fact]
    public void AnotherSetupIsNotedWhenNoProfileMatches()
    {
        var submenu = Assert.Single(DisplayProfilesMenu.Build(Profiles, inUse: null, known: true, remote: false));

        Assert.Equal([$"0 {Strings.DisplayProfilesMenuOther} (dimmed)", "1 Desk", "3 TV & sofa (dimmed)", "-", Settings], submenu.Items!.Select(Describe));
    }

    [Fact]
    public void BeforeTheFirstReadNothingIsNotedOrMarked()
    {
        var submenu = Assert.Single(DisplayProfilesMenu.Build(Profiles, inUse: null, known: false, remote: false));

        Assert.Equal(["1 Desk", "3 TV & sofa (dimmed)", "-", Settings], submenu.Items!.Select(Describe));
    }

    [Fact]
    public void InARemoteSessionEveryProfileIsDimmed()
    {
        var submenu = Assert.Single(DisplayProfilesMenu.Build(Profiles, inUse: 1, known: false, remote: true));

        Assert.Equal([$"0 {Strings.DisplayProfilesMenuRemote} (dimmed)", "1 Desk (dimmed)", "3 TV & sofa (dimmed)", "-", Settings], submenu.Items!.Select(Describe));
    }

    internal static string Describe(HostMenuItem item) =>
        item.IsSeparator ? "-" : $"{item.Id} {item.Text}{(item.IsChecked ? " (checked)" : "")}{(item.IsEnabled ? "" : " (dimmed)")}";

    internal static IEnumerable<string> Describe(IReadOnlyList<HostMenuItem> items) =>
        items.Count == 0 ? [] : items[0].Items!.Select(Describe);
}
