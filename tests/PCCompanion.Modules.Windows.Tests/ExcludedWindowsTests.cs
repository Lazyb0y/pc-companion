using System.Drawing;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ExcludedWindowsTests
{
    private static readonly ListedApp Browser = new() { Name = "Browser", AppId = "Browser.Profile1" };
    private static readonly ListedApp Editor = new() { Name = "Editor", Program = @"C:\Apps\Editor.exe" };

    private readonly FakeTitleBarWindows _windows = new();
    private readonly ExcludedWindows _excluded;

    public ExcludedWindowsTests()
    {
        _windows.Add(1, new Rectangle(0, 0, 100, 100));
        _windows.Add(2, new Rectangle(0, 0, 100, 100));
        _windows.Add(3, new Rectangle(0, 0, 100, 100));
        _windows.Apps[1] = new AppOfWindow("browser.profile1", null, @"C:\Apps\Browser.exe", 10);
        _windows.Apps[2] = new AppOfWindow(null, null, @"c:\apps\editor.exe", 20);
        _excluded = new ExcludedWindows(_windows);
    }

    [Fact]
    public void WithoutAppsNoWindowsAppIsRead()
    {
        Assert.False(_excluded.Contains(1));

        Assert.Empty(_windows.AppsRead);
    }

    [Fact]
    public void AnAppIsMatchedByItsAppIdOrItsProgramWhateverTheirCase()
    {
        _excluded.SetApps([Browser, Editor]);

        Assert.True(_excluded.Contains(1));
        Assert.True(_excluded.Contains(2));
        Assert.False(_excluded.Contains(3));
    }

    [Fact]
    public void EachWindowsAppIsReadOnce()
    {
        _excluded.SetApps([Editor]);

        _excluded.Contains(2);
        _excluded.Contains(2);
        _excluded.Contains(3);

        Assert.Equal([2, 3], _windows.AppsRead);
    }

    [Fact]
    public void AnotherListReadsTheWindowsAgainButTheSameListDoesnt()
    {
        _excluded.SetApps([Editor]);
        _excluded.Contains(2);

        Assert.False(_excluded.SetApps([Editor with { }]));
        Assert.True(_excluded.Contains(2));
        Assert.True(_excluded.SetApps([Browser]));

        Assert.False(_excluded.Contains(2));
        Assert.Equal([2, 2], _windows.AppsRead);
    }

    [Fact]
    public void ClosedWindowsAreForgottenOnceManyWereRead()
    {
        _excluded.SetApps([Editor]);
        _excluded.Contains(2);
        _windows.Remove(2);
        for (nint window = 100; window < 163; window++)
        {
            _windows.Add(window, new Rectangle(0, 0, 100, 100));
            _excluded.Contains(window);
        }

        // The next window read drops the closed one, so a new window with its handle is read again.
        _excluded.Contains(3);
        _windows.Add(2, new Rectangle(0, 0, 100, 100));
        _excluded.Contains(2);

        Assert.Equal(2, _windows.AppsRead[^1]);
        Assert.Equal(2, _windows.AppsRead.FindAll(window => window == 2).Count);
    }
}
