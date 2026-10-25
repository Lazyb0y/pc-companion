using System.Collections.Generic;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Made-up apps and windows; no window or process is read.
public sealed class AppOfWindowTests
{
    private const string Photos = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App";
    private const string Chrome = @"C:\Apps\Chrome\chrome.exe";

    [Fact]
    public void AnAppIdMatchesWhetherTheWindowSetsItOrItsPackageHasIt()
    {
        var set = new AppOfWindow(Photos, null, @"C:\Windows\System32\ApplicationFrameHost.exe", 1);
        var packaged = new AppOfWindow(null, Photos.ToUpperInvariant(), @"C:\Program Files\WindowsApps\Photos.exe", 2);

        Assert.True(set.IsSameAppAs(packaged));
        Assert.True(packaged.IsSameAppAs(set));
    }

    [Fact]
    public void TheAppIdAWindowSetsComesBeforeItsPackagesAndItsProgram()
    {
        var profile = new AppOfWindow("Chrome.Profile.1", null, Chrome, 1);
        var other = new AppOfWindow("Chrome.Profile.2", null, Chrome, 1);
        var plain = new AppOfWindow(null, null, Chrome, 1);

        Assert.Equal("Chrome.Profile.1", profile.AppId);
        Assert.False(profile.IsSameAppAs(other));
        Assert.False(profile.IsSameAppAs(plain));
        Assert.False(plain.IsSameAppAs(profile));
    }

    [Fact]
    public void WithoutAppIdsTheProgramDecidesWhateverItsCase()
    {
        var first = new AppOfWindow(null, null, Chrome, 1);
        var second = new AppOfWindow(null, null, Chrome.ToUpperInvariant(), 2);
        var notepad = new AppOfWindow(null, null, @"C:\Windows\System32\notepad.exe", 1);

        Assert.True(first.IsSameAppAs(second));
        Assert.False(first.IsSameAppAs(notepad));
    }

    [Fact]
    public void WithoutAnAppIdOrAProgramTheProcessDecides()
    {
        var unknown = new AppOfWindow(null, null, null, 7);

        Assert.True(unknown.IsSameAppAs(new AppOfWindow(null, null, null, 7)));
        Assert.False(unknown.IsSameAppAs(new AppOfWindow(null, null, null, 8)));
        Assert.False(unknown.IsSameAppAs(new AppOfWindow(null, null, Chrome, 7)));
    }

    [Fact]
    public void AListedAppIdMatchesEitherAppIdAndAListedProgramMatchesTheProgram()
    {
        var app = new AppOfWindow("Window.Id", Photos, Chrome, 1);

        Assert.True(app.HasAppId("window.id"));
        Assert.True(app.HasAppId(Photos));
        Assert.False(app.HasAppId("Other.Id"));
        Assert.True(app.HasProgram(Chrome.ToUpperInvariant()));
        Assert.False(app.HasProgram(@"C:\Windows\System32\notepad.exe"));
    }

    [Fact]
    public void TheReaderReadsEachProcessOnceAndEachWindowsAppId()
    {
        var reader = new FakeReader();
        reader.WindowAppIds[1] = "Chrome.Profile.1";
        reader.Programs[10] = Chrome;
        var read = new AppOfWindowReader(reader);

        var first = read.Of(Window(1, 10));
        var second = read.Of(Window(2, 10));

        Assert.Equal(new AppOfWindow("Chrome.Profile.1", null, Chrome, 10), first);
        Assert.Equal(new AppOfWindow(null, null, Chrome, 10), second);
        Assert.Equal(1, reader.ProcessReads[10]);
    }

    private static TopLevelWindow Window(nint handle, uint processId) =>
        new(handle, processId, IsMinimized: false, IsCloaked: false, IsToolWindow: false, IsAppWindow: false, IsNoActivate: false, IsTopmost: false, HasOwner: false, IsDesktop: false);

    private sealed class FakeReader : IAppIdentityReader
    {
        public Dictionary<nint, string> WindowAppIds { get; } = [];

        public Dictionary<uint, string> Programs { get; } = [];

        public Dictionary<uint, int> ProcessReads { get; } = [];

        public string? WindowAppId(nint window) => WindowAppIds.GetValueOrDefault(window);

        public string? PackageAppId(uint processId)
        {
            ProcessReads[processId] = ProcessReads.GetValueOrDefault(processId) + 1;
            return null;
        }

        public string? ProgramPath(uint processId) => Programs.GetValueOrDefault(processId);
    }
}
