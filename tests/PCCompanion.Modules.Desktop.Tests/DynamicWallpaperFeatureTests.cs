using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class DynamicWallpaperFeatureTests : IDisposable
{
    // Noon in London at midsummer: Day, until sunset at about 20:21 UTC.
    private static readonly DateTimeOffset Noon = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDynamicWallpaperSystem _system = new();
    private readonly ManualTimeProvider _time = new() { Now = Noon };
    private readonly RecordingLogger<DynamicWallpaperFeature> _logger = new();
    private readonly DynamicWallpaperFeature _feature;

    public DynamicWallpaperFeatureTests()
    {
        _feature = new DynamicWallpaperFeature(_system, _time, _logger);
    }

    public void Dispose()
    {
        _feature.Dispose();
        _system.Dispose();
    }

    [Fact]
    public void TheFeatureIsInTheDesktopModule()
    {
        Assert.Equal(("dynamicWallpaper", "desktop"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_system.Wallpaper.Set);
    }

    [Fact]
    public void TurningOnSetsThePeriodsPictureAndFitForEveryDisplay()
    {
        _feature.Apply(On(fit: "Center"));

        Assert.True(_feature.IsRunning);
        Assert.Equal([(null, @"C:\Day.jpg")], _system.Wallpaper.Set);
        Assert.Equal(WallpaperPosition.Center, _system.Wallpaper.Fit);
        Assert.Equal("Sun.Day", _feature.Report!.Period);
        Assert.Equal(WallpaperBlock.None, _feature.Report.Block);
    }

    [Fact]
    public void ThePictureChangesWhenTheNextPeriodStarts()
    {
        _feature.Apply(On());
        var sunset = _system.TimerAt!.Value;
        Assert.InRange(sunset, Noon.AddHours(8), Noon.AddHours(8.6));

        _time.Now = sunset;
        _system.FireTimer();

        Assert.Equal(@"C:\Night.jpg", _system.Wallpaper.All);
        Assert.Equal("Sun.Night", _feature.Report!.Period);
        Assert.True(_system.TimerAt > sunset);
    }

    [Fact]
    public void APictureWindowsShowsAlreadyIsntSetAgain()
    {
        _system.Wallpaper.All = @"c:\day.JPG";

        _feature.Apply(On());
        _system.Events!.DisplaysChanged();

        Assert.Empty(_system.Wallpaper.Set);
    }

    [Fact]
    public void LookingAgainWithinThePeriodChangesNothing()
    {
        _feature.Apply(On());
        _system.Events!.Unlocked();
        _system.Events.Resumed();
        _feature.Apply(On());

        Assert.Single(_system.Wallpaper.Set);
    }

    [Fact]
    public void WithoutAPlaceDayEndsAtEighteen()
    {
        _system.ZoneCity = null;

        _feature.Apply(On());

        Assert.Equal(new DateTimeOffset(2026, 6, 21, 18, 0, 0, TimeSpan.Zero), _system.TimerAt);
    }

    [Fact]
    public void AClockChangeReadsTheTimeZoneAgain()
    {
        _feature.Apply(On());

        _system.Events!.ClockChanged();

        Assert.Equal(1, _system.ZoneReads);
    }

    [Fact]
    public void WindowsModeFollowsTheMode()
    {
        _feature.Apply(On(changeWith: "WindowsMode"));
        Assert.Equal(@"C:\Day.jpg", _system.Wallpaper.All);
        Assert.Null(_system.TimerAt);

        _system.Dark = true;
        _system.Events!.SettingChanged();

        Assert.Equal(@"C:\Night.jpg", _system.Wallpaper.All);
    }

    [Fact]
    public void EachDisplayGetsItsOwnPicture()
    {
        TwoDisplays();

        _feature.Apply(Section($$"""
            {
              "enabled": true, "eachDisplay": true,
              "displays": [{ "model": "DELF11B", "serial": "bbbbbbbbbbbbbbbb", "connection": "", "name": "DELL E2420H" }],
              "sun": { "day": { "picture": "C:\\Day.jpg", "displays": [{ "picture": "C:\\Second.jpg" }] }, "night": { "picture": "C:\\Night.jpg" } }
            }
            """));

        Assert.Equal(
            [(FakeWallpaper.FirstDisplay, @"C:\Day.jpg"), (FakeWallpaper.SecondDisplay, @"C:\Second.jpg")],
            _system.Wallpaper.Set.OrderBy(set => set.Display));
    }

    [Fact]
    public void SpanShowsOnePictureOnEveryDisplay()
    {
        TwoDisplays();

        _feature.Apply(Section("""
            { "enabled": true, "eachDisplay": true, "fit": "Span",
              "displays": [{ "model": "DELF11B", "serial": "bbbbbbbbbbbbbbbb" }],
              "sun": { "day": { "picture": "C:\\Day.jpg", "displays": [{ "picture": "C:\\Second.jpg" }] }, "night": { "picture": "C:\\Night.jpg" } } }
            """));

        Assert.Equal([(null, @"C:\Day.jpg")], _system.Wallpaper.Set);
        Assert.Equal(WallpaperPosition.Span, _system.Wallpaper.Fit);
    }

    [Fact]
    public void AFolderShowsItsNextPictureEachPeriod()
    {
        _system.PictureFiles.Folders[@"C:\Days"] = [@"C:\Days\1.jpg", @"C:\Days\2.jpg", @"C:\Days\3.jpg"];
        var settings = Section("""{ "enabled": true, "sun": { "day": { "folder": "C:\\Days" }, "night": { "picture": "C:\\Night.jpg" } } }""");

        _feature.Apply(settings);
        var first = _system.Wallpaper.All;
        _system.Events!.DisplaysChanged();
        Assert.Equal(first, _system.Wallpaper.All);
        Assert.Single(_system.Wallpaper.Set);

        _time.Now = Noon.AddDays(1).AddHours(-12);
        _system.FireTimer();
        _time.Now = Noon.AddDays(1);
        _system.FireTimer();

        Assert.StartsWith(@"C:\Days\", _system.Wallpaper.All, StringComparison.Ordinal);
        Assert.NotEqual(first, _system.Wallpaper.All);
    }

    [Fact]
    public void APictureThatsGoneLeavesTheBackgroundAsItIs()
    {
        _system.PictureFiles.States[@"C:\Day.jpg"] = PictureState.Missing;

        _feature.Apply(On());

        Assert.Empty(_system.Wallpaper.Set);
        Assert.Equal([new PictureIssue(-1, PictureProblem.Missing)], _feature.Report!.Issues);
    }

    [Fact]
    public void APictureWindowsCantReadIsReported()
    {
        _system.Wallpaper.Unreadable.Add(@"C:\Day.jpg");

        _feature.Apply(On());

        Assert.Empty(_system.Wallpaper.Set);
        Assert.Equal([new PictureIssue(-1, PictureProblem.Refused)], _feature.Report!.Issues);
        Assert.True(_system.Wallpaper.Connected);
    }

    [Fact]
    public void WhileAPolicyManagesTheBackgroundNothingChanges()
    {
        _system.Policies = true;

        _feature.Apply(On());

        Assert.Empty(_system.Wallpaper.Set);
        Assert.Equal(WallpaperBlock.Managed, _feature.Report!.Block);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("Managed", StringComparison.Ordinal));
    }

    [Fact]
    public void APictureWindowsRefusesWithoutAnErrorCountsAsManaged()
    {
        _system.Wallpaper.Refuses = true;

        _feature.Apply(On());

        Assert.Equal(WallpaperBlock.Managed, _feature.Report!.Block);
    }

    [Fact]
    public void AnotherPictureShownInsteadCountsAsManaged()
    {
        _system.Wallpaper.Forced = @"C:\Company.jpg";

        _feature.Apply(On());

        Assert.Equal(WallpaperBlock.Managed, _feature.Report!.Block);
    }

    [Fact]
    public void InARemoteSessionWithoutABackgroundNothingIsSet()
    {
        _system.Wallpaper.State = WallpaperStatus.OffInRemoteSession;

        _feature.Apply(On());

        Assert.Empty(_system.Wallpaper.Set);
        Assert.Equal(WallpaperBlock.RemoteSession, _feature.Report!.Block);
    }

    [Fact]
    public void WhileExplorerIsntRunningThePicturesWaitForItsTaskbar()
    {
        _system.ConnectFailure = new WallpaperException("Not running.", unchecked((int)0x80040154));

        _feature.Apply(On());
        Assert.Equal(WallpaperBlock.ExplorerNotAnswering, _feature.Report!.Block);

        _system.ConnectFailure = null;
        _system.Events!.TaskbarCreated();

        Assert.Equal([(null, @"C:\Day.jpg")], _system.Wallpaper.Set);
        Assert.Equal(WallpaperBlock.None, _feature.Report.Block);
    }

    [Fact]
    public void WhenExplorerEndsTheNextLookConnectsAgain()
    {
        _feature.Apply(On());
        _system.Wallpaper.Failure = new WallpaperException("Gone.", unchecked((int)0x800706BA));

        _system.Events!.DisplaysChanged();
        Assert.False(_system.Wallpaper.Connected);
        _system.Events.DisplaysChanged();

        Assert.True(_system.Wallpaper.Connected);
        Assert.Equal(2, _system.Connections);
    }

    [Fact]
    public void AFailureOnTheWallpaperThreadTurnsTheFeatureOff()
    {
        _system.Wallpaper.Failure = new InvalidOperationException("A bug.");

        _feature.Apply(On());
        _feature.Apply(On());

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact]
    public void SettingsThatCantBeReadTurnTheFeatureOff()
    {
        _feature.Apply(On());

        _feature.Apply(Section("""{ "enabled": true, "times": 3 }"""));

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void AnUnknownChoiceIsLogged()
    {
        _feature.Apply(Section("""{ "enabled": true, "fit": "Zoom", "sun": { "day": { "picture": "C:\\Day.jpg" }, "night": { "picture": "C:\\Night.jpg" } } }"""));

        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("fit", StringComparison.Ordinal));
        Assert.Equal(WallpaperPosition.Fill, _system.Wallpaper.Fit);
    }

    [Fact]
    public void TurningOffStopsEverything()
    {
        _feature.Apply(On());

        _feature.Apply(Section("""{ "enabled": false }"""));
        _feature.Dispose();
        _feature.Dispose();

        Assert.False(_feature.IsRunning);
        Assert.False(_system.WindowOpen);
        Assert.False(_system.WorkerRunning);
        Assert.Null(_system.TimerAt);
    }

    private static System.Text.Json.JsonElement On(string changeWith = "Sun", string fit = "Fill") => Section($$"""
        {
          "enabled": true, "changeWith": "{{changeWith}}", "fit": "{{fit}}",
          "sun": { "day": { "picture": "C:\\Day.jpg" }, "night": { "picture": "C:\\Night.jpg" } },
          "windowsMode": { "light": { "picture": "C:\\Day.jpg" }, "dark": { "picture": "C:\\Night.jpg" } }
        }
        """);

    private void TwoDisplays()
    {
        _system.Wallpaper.Monitors =
        [
            new WallpaperMonitor(FakeWallpaper.FirstDisplay, new Rectangle(0, 0, 1920, 1080)),
            new WallpaperMonitor(FakeWallpaper.SecondDisplay, new Rectangle(1920, 0, 1920, 1080)),
            new WallpaperMonitor(@"\\?\DISPLAY#GONE", null),
        ];
        _system.Displays = new WallpaperDisplays(
            [Connected(1, FakeWallpaper.FirstDisplay), Connected(2, FakeWallpaper.SecondDisplay)],
            new Dictionary<string, string> { [FakeWallpaper.FirstDisplay] = "aaaaaaaaaaaaaaaa", [FakeWallpaper.SecondDisplay] = "bbbbbbbbbbbbbbbb" });
    }

    private static ConnectedDisplay Connected(uint id, string path) =>
        new(new DisplayTarget(1, id), path, "DELF11B", "DELL E2420H", DisplayConnection.DisplayPort, [new DisplaySource(1, id)]);
}
