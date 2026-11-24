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
    }

    [Fact]
    public void ARunningFeatureTakesItsWindowsUntilItStops()
    {
        var tray = new List<nint>();
        var hidden = new List<nint>();
        var grouped = new List<nint>();
        var takenOut = new List<nint>();
        _requests.SetTray(Into(tray));
        _requests.SetHideApps(Into(hidden));
        _requests.SetWindowTabs(Into(grouped), Into(takenOut));

        Assert.True(_requests.SendToTray(1));
        Assert.True(_requests.HideApp(2));
        Assert.True(_requests.AddToGroupBehind(5));
        Assert.True(_requests.TakeOutOfGroup(6));
        _requests.SetTray(null);
        _requests.SetHideApps(null);
        _requests.SetWindowTabs(null, null);

        Assert.False(_requests.SendToTray(3));
        Assert.False(_requests.HideApp(4));
        Assert.False(_requests.AddToGroupBehind(7));
        Assert.False(_requests.TakeOutOfGroup(8));
        Assert.Equal([1], tray);
        Assert.Equal([2], hidden);
        Assert.Equal([5], grouped);
        Assert.Equal([6], takenOut);
    }

    // Takes every window, into the list.
    private static Func<nint, bool> Into(List<nint> taken) => window =>
    {
        taken.Add(window);
        return true;
    };
}
