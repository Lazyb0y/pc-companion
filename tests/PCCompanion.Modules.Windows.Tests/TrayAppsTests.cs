using System.Linq;
using PCCompanion.Modules.Windows.MinimizeToTray;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TrayAppsTests
{
    [Fact]
    public void AnAppGetsTheSameIconIdEveryTimeWhateverItsCase()
    {
        Assert.Equal(TrayApps.IdFor(@"C:\Apps\App.exe"), TrayApps.IdFor(@"c:\apps\APP.EXE"));
        Assert.NotEqual(TrayApps.IdFor(@"C:\Apps\App.exe"), TrayApps.IdFor(@"C:\Apps\Other.exe"));
    }

    [Fact]
    public void IconIdsFitIn16BitsAndLeaveTheFirstOnesFree()
    {
        Assert.All(Enumerable.Range(0, 1000).Select(at => TrayApps.IdFor("app" + at)), id => Assert.InRange(id, 0x100u, 0xFFFFu));
    }

    [Fact]
    public void TwoAppsWithTheSameIconIdGetDifferentOnes()
    {
        var keys = Enumerable.Range(0, 5000).Select(at => "app" + at).ToList();
        var (first, second) = keys
            .GroupBy(TrayApps.IdFor)
            .Where(group => group.Count() > 1)
            .Select(group => (group.First(), group.Skip(1).First()))
            .First();
        var tray = new TrayApps();

        var one = tray.Add(first, "One", new TrayWindow(1, 10, string.Empty));
        var two = tray.Add(second, "Two", new TrayWindow(2, 20, string.Empty));

        Assert.Equal(TrayApps.IdFor(first), one.Id);
        Assert.NotEqual(one.Id, two.Id);
    }

    [Fact]
    public void AnAppsWindowsAreKeptNewestFirstAndTheAppGoesWithItsLast()
    {
        var tray = new TrayApps();
        var app = tray.Add("app", "App", new TrayWindow(1, 10, "First"));
        Assert.Same(app, tray.Add("APP", "App", new TrayWindow(2, 10, "Second")));

        Assert.Equal([2, 1], app.Windows.Select(window => window.Handle));
        Assert.Equal(2, tray.WindowCount);

        tray.Remove(2);
        Assert.Single(tray.Apps);
        tray.Remove(1);

        Assert.Empty(tray.Apps);
        Assert.False(tray.HasProcess(10));
        Assert.Null(tray.Remove(1));
    }
}
