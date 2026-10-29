using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Made-up windows and app identities; no window or process is read.
public sealed class RunningAppsTests
{
    private const string Settings = @"C:\Program Files\PC Companion\PCCompanion.exe";

    private readonly FakeAppIdentityReader _reader = new();

    [Fact]
    public void WindowsAreGroupedAsTheTaskbarGroupsThem()
    {
        _reader.WindowAppIds[1] = "Chrome.Profile.1";
        _reader.Packages[20] = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App";
        _reader.Programs[10] = @"C:\Apps\Chrome\chrome.exe";
        _reader.Programs[20] = @"C:\Windows\System32\ApplicationFrameHost.exe";
        _reader.Programs[30] = @"C:\Windows\System32\mstsc.exe";

        var apps = RunningApps.Of([Window(1, 10), Window(2, 10), Window(3, 20), Window(4, 30), Window(5, 30)], _reader, []);

        Assert.Equal(
            [
                new ListedApp { AppId = "Chrome.Profile.1" },
                new ListedApp { Program = @"C:\Apps\Chrome\chrome.exe" },
                new ListedApp { AppId = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" },
                new ListedApp { Program = @"C:\Windows\System32\mstsc.exe" },
            ],
            apps.Select(running => running.App));

        // An app with an app ID keeps its window's program for its name and icon.
        Assert.Equal(@"C:\Apps\Chrome\chrome.exe", apps[0].Program);
    }

    [Fact]
    public void EachProcessIsReadOnce()
    {
        _reader.Programs[30] = @"C:\Windows\System32\mstsc.exe";

        RunningApps.Of([Window(4, 30), Window(5, 30), Window(6, 30)], _reader, []);

        Assert.Equal(1, _reader.ProcessReads[30]);
    }

    [Fact]
    public void WindowsThatAltTabDoesntShowAreLeftOut()
    {
        _reader.Programs[30] = @"C:\Windows\System32\mstsc.exe";

        var apps = RunningApps.Of(
            [
                Window(1, 30) with { IsToolWindow = true },
                Window(2, 30) with { IsCloaked = true },
                Window(3, 30) with { HasOwner = true },
                Window(4, 30) with { IsNoActivate = true },
                Window(5, 30) with { IsDesktop = true },
                Window(6, 0),
            ],
            _reader,
            []);

        Assert.Empty(apps);
    }

    [Fact]
    public void MinimizedWindowsAndOwnedWindowsOnTheTaskbarCount()
    {
        _reader.Programs[30] = @"C:\Windows\System32\mstsc.exe";
        _reader.Programs[40] = @"C:\Windows\System32\charmap.exe";

        var apps = RunningApps.Of([Window(1, 30) with { IsMinimized = true }, Window(2, 40) with { HasOwner = true, IsAppWindow = true }], _reader, []);

        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void PCCompanionsOwnProgramsAreLeftOut()
    {
        _reader.Programs[50] = @"c:\program files\pc companion\pccompanion.exe";
        _reader.Programs[30] = @"C:\Windows\System32\mstsc.exe";

        var apps = RunningApps.Of([Window(1, 50), Window(2, 30)], _reader, [Settings]);

        Assert.Equal([new ListedApp { Program = @"C:\Windows\System32\mstsc.exe" }], apps.Select(running => running.App));
    }

    private static TopLevelWindow Window(nint handle, uint processId) =>
        new(handle, processId, IsMinimized: false, IsCloaked: false, IsToolWindow: false, IsAppWindow: false, IsNoActivate: false, IsTopmost: false, HasOwner: false, IsDesktop: false);

    private sealed class FakeAppIdentityReader : IAppIdentityReader
    {
        public Dictionary<nint, string> WindowAppIds { get; } = [];

        public Dictionary<uint, string> Packages { get; } = [];

        public Dictionary<uint, string> Programs { get; } = [];

        public Dictionary<uint, int> ProcessReads { get; } = [];

        public string? WindowAppId(nint window) => WindowAppIds.GetValueOrDefault(window);

        public string? PackageAppId(uint processId)
        {
            ProcessReads[processId] = ProcessReads.GetValueOrDefault(processId) + 1;
            return Packages.GetValueOrDefault(processId);
        }

        public string? ProgramPath(uint processId) => Programs.GetValueOrDefault(processId);
    }
}
