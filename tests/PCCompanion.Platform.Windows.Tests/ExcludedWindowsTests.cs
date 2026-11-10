using System.Collections.Generic;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ExcludedWindowsTests
{
    private static readonly ListedApp Browser = new() { Name = "Browser", AppId = "Browser.Profile1" };
    private static readonly ListedApp Editor = new() { Name = "Editor", Program = @"C:\Apps\Editor.exe" };

    private readonly FakeWindows _windows = new();
    private readonly ExcludedWindows _excluded;

    public ExcludedWindowsTests()
    {
        _windows.Add(1);
        _windows.Add(2);
        _windows.Add(3);
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
            _windows.Add(window);
            _excluded.Contains(window);
        }

        // The next window read drops the closed one, so a new window with its handle is read again.
        _excluded.Contains(3);
        _windows.Add(2);
        _excluded.Contains(2);

        Assert.Equal(2, _windows.AppsRead[^1]);
        Assert.Equal(2, _windows.AppsRead.FindAll(window => window == 2).Count);
    }

    // Windows that exist until removed, with the apps the tests give them.
    private sealed class FakeWindows : IListedAppWindows
    {
        private readonly HashSet<nint> _windows = [];

        public Dictionary<nint, AppOfWindow> Apps { get; } = [];

        public List<nint> AppsRead { get; } = [];

        public void Add(nint window) => _windows.Add(window);

        public void Remove(nint window) => _windows.Remove(window);

        public AppOfWindow? AppOf(nint root)
        {
            AppsRead.Add(root);
            return !Exists(root) ? null : Apps.GetValueOrDefault(root) ?? new AppOfWindow(null, null, $@"C:\Apps\{root}.exe", (uint)root);
        }

        public bool Exists(nint window) => _windows.Contains(window);
    }
}
