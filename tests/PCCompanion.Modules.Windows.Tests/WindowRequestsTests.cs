using System;
using System.Collections.Generic;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowRequestsTests
{
    private readonly WindowRequests _requests = new();

    [Fact]
    public void WithoutTheFeaturesTheRequestsAreRefused()
    {
        Assert.False(_requests.SendToTray(1));
        Assert.False(_requests.HideApp(1));
        Assert.False(_requests.AddToGroupBehind(1));
        Assert.False(_requests.TakeOutOfGroup(1));
        Assert.False(_requests.RollUp(1));
    }

    [Fact]
    public void WindowRulesHearsOfTheWheelsOpacityUntilItStops()
    {
        var heard = new List<(nint, int)>();
        _requests.OpacityChanged(1, 50);

        _requests.SetOpacityChanged((window, percent) => heard.Add((window, percent)));
        _requests.OpacityChanged(2, 60);
        _requests.SetOpacityChanged(null);
        _requests.OpacityChanged(3, 70);

        Assert.Equal([(2, 60)], heard);
    }

    [Fact]
    public void WindowTabsGroupsAWindowWithAnotherUntilItStops()
    {
        var grouped = new List<(nint, nint)>();
        Assert.False(_requests.GroupWith(1, 2));

        _requests.SetGroupWith((window, target) =>
        {
            grouped.Add((window, target));
            return true;
        });
        Assert.True(_requests.GroupWith(3, 4));
        _requests.SetGroupWith(null);

        Assert.False(_requests.GroupWith(5, 6));
        Assert.Equal([(3, 4)], grouped);
    }

    [Fact]
    public void ARunningFeatureTakesItsWindowsUntilItStops()
    {
        var tray = new List<nint>();
        var hidden = new List<nint>();
        var grouped = new List<nint>();
        var takenOut = new List<nint>();
        var rolledUp = new List<nint>();
        _requests.SetTray(Into(tray));
        _requests.SetHideApps(Into(hidden));
        _requests.SetWindowTabs(Into(grouped), Into(takenOut));
        _requests.SetRollUp(Into(rolledUp));

        Assert.True(_requests.SendToTray(1));
        Assert.True(_requests.HideApp(2));
        Assert.True(_requests.AddToGroupBehind(5));
        Assert.True(_requests.TakeOutOfGroup(6));
        Assert.True(_requests.RollUp(9));
        _requests.SetTray(null);
        _requests.SetHideApps(null);
        _requests.SetWindowTabs(null, null);
        _requests.SetRollUp(null);

        Assert.False(_requests.SendToTray(3));
        Assert.False(_requests.HideApp(4));
        Assert.False(_requests.AddToGroupBehind(7));
        Assert.False(_requests.TakeOutOfGroup(8));
        Assert.False(_requests.RollUp(10));
        Assert.Equal([1], tray);
        Assert.Equal([2], hidden);
        Assert.Equal([5], grouped);
        Assert.Equal([6], takenOut);
        Assert.Equal([9], rolledUp);
    }

    // Takes every window, into the list.
    private static Func<nint, bool> Into(List<nint> taken) => window =>
    {
        taken.Add(window);
        return true;
    };
}
