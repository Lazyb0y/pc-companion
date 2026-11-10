using System;
using System.Drawing;
using System.IO;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Desktop.Tests;

// Working alongside Windows: remembering the background and putting it back, and stepping aside.
public sealed class DynamicWallpaperAlongsideTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDynamicWallpaperSystem _system = new();
    private readonly ManualTimeProvider _time = new() { Now = Noon };
    private readonly RecordingLogger<DynamicWallpaperFeature> _logger = new();
    private readonly DynamicWallpaperFeature _feature;

    public DynamicWallpaperAlongsideTests()
    {
        _feature = new DynamicWallpaperFeature(_system, _time, _logger);
    }

    private FakeWallpaper Wallpaper => _system.Wallpaper;

    public void Dispose()
    {
        _feature.Dispose();
        _system.Dispose();
    }

    [Fact]
    public void TurningOffPutsBackThePictureTheUserHad()
    {
        Wallpaper.Fit = WallpaperPosition.Center;
        Wallpaper.Color = 0x123456;

        _feature.Apply(On);
        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.Equal(WallpaperPosition.Fill, Wallpaper.Fit);
        _feature.Apply(Off);

        Assert.Equal(@"C:\Old.jpg", Wallpaper.All);
        Assert.Equal(WallpaperPosition.Center, Wallpaper.Fit);
        Assert.Equal(0x123456u, Wallpaper.Color);
        Assert.False(File.Exists(_system.Store.FilePath));
    }

    [Fact]
    public void EachDisplaysPictureIsPutBack()
    {
        Wallpaper.Monitors =
        [
            new WallpaperMonitor(FakeWallpaper.FirstDisplay, new Rectangle(0, 0, 1920, 1080)),
            new WallpaperMonitor(FakeWallpaper.SecondDisplay, new Rectangle(1920, 0, 1920, 1080)),
        ];
        Wallpaper.SetPicture(FakeWallpaper.FirstDisplay, @"C:\Left.jpg");
        Wallpaper.SetPicture(FakeWallpaper.SecondDisplay, @"C:\Right.jpg");

        _feature.Apply(On);
        _feature.Apply(Off);

        Assert.Equal(@"C:\Left.jpg", Wallpaper.PictureOf(FakeWallpaper.FirstDisplay));
        Assert.Equal(@"C:\Right.jpg", Wallpaper.PictureOf(FakeWallpaper.SecondDisplay));
    }

    [Fact]
    public void ASolidColorIsPutBack()
    {
        Wallpaper.TurnOff();
        Wallpaper.Color = 0x008080;

        _feature.Apply(On);
        Assert.Equal(WallpaperStatus.On, Wallpaper.State);
        _feature.Apply(Off);

        Assert.Equal(WallpaperStatus.None, Wallpaper.State);
        Assert.Equal(0x008080u, Wallpaper.Color);
    }

    [Fact]
    public void ASlideshowIsPutBack()
    {
        var slideshow = new WallpaperSlideshow([@"C:\Slides"], true, TimeSpan.FromMinutes(10));
        Wallpaper.SetSlideshow(slideshow);

        _feature.Apply(On);
        Assert.False(Wallpaper.State.HasFlag(WallpaperStatus.Slideshow));
        _feature.Apply(Off);

        Assert.True(Wallpaper.State.HasFlag(WallpaperStatus.Slideshow));
        Assert.Equal(slideshow.Items, Wallpaper.Show!.Items);
        Assert.Equal((true, TimeSpan.FromMinutes(10)), (Wallpaper.Show.Shuffle, Wallpaper.Show.Interval));
    }

    [Fact]
    public void AfterWindowsSpotlightItsLastPictureIsPutBack()
    {
        _system.Spotlight = true;
        Wallpaper.All = @"C:\Windows\SystemApps\Spotlight\image_2.jpg";

        _feature.Apply(On);
        Assert.True(_system.Store.Load(out _).Remembered!.Spotlight);
        _feature.Apply(Off);

        Assert.Equal(@"C:\Windows\SystemApps\Spotlight\image_2.jpg", Wallpaper.All);
    }

    [Fact]
    public void APictureThatsGoneGivesTheBackgroundColor()
    {
        _feature.Apply(On);
        Wallpaper.Unreadable.Add(@"C:\Old.jpg");

        _feature.Apply(Off);

        Assert.Equal(WallpaperStatus.None, Wallpaper.State);
    }

    [Fact]
    public void ItsOwnChangeIsntSomeoneElses()
    {
        _feature.Apply(On);

        _system.BackgroundChanged!();

        Assert.Equal(WallpaperBlock.None, _feature.Report!.Block);
    }

    [Fact]
    public void SomeoneElsesChangeMakesItStepAside()
    {
        _feature.Apply(On);
        Wallpaper.All = @"C:\Mine.jpg";

        _system.BackgroundChanged!();
        _time.Now = Noon.AddHours(9);
        _system.FireTimer();

        Assert.Equal(WallpaperBlock.SteppedAside, _feature.Report!.Block);
        Assert.Equal(@"C:\Mine.jpg", Wallpaper.All);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("stepped aside", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterSteppingAsideTurningOffLeavesTheUsersChoice()
    {
        _feature.Apply(On);
        Wallpaper.All = @"C:\Mine.jpg";
        _system.BackgroundChanged!();

        _feature.Apply(Off);

        Assert.Equal(@"C:\Mine.jpg", Wallpaper.All);
        Assert.False(File.Exists(_system.Store.FilePath));
    }

    [Fact]
    public void ASlideshowTheUserStartsMakesItStepAside()
    {
        _feature.Apply(On);
        Wallpaper.SetSlideshow(new WallpaperSlideshow([@"C:\Slides"], false, TimeSpan.FromMinutes(1)));

        _system.BackgroundChanged!();

        Assert.Equal(WallpaperBlock.SteppedAside, _feature.Report!.Block);
    }

    [Fact]
    public void SteppingAsideLastsAcrossRestarts()
    {
        _feature.Apply(On);
        Wallpaper.All = @"C:\Mine.jpg";
        _system.BackgroundChanged!();
        _feature.Dispose();

        using var restarted = new DynamicWallpaperFeature(_system, _time, _logger);
        restarted.Apply(On);

        Assert.Equal(WallpaperBlock.SteppedAside, restarted.Report!.Block);
        Assert.Equal(@"C:\Mine.jpg", Wallpaper.All);
    }

    [Fact]
    public void ResumeRemembersTheNewBackgroundAndSetsThePeriodsPicture()
    {
        _feature.Apply(On);
        Wallpaper.All = @"C:\Mine.jpg";
        _system.BackgroundChanged!();

        _feature.Resume();
        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.Equal(WallpaperBlock.None, _feature.Report!.Block);
        _feature.Apply(Off);

        Assert.Equal(@"C:\Mine.jpg", Wallpaper.All);
    }

    [Fact]
    public void ASwitchOfVirtualDesktopGetsThePeriodsPicture()
    {
        _system.Desktop = Guid.NewGuid();
        _feature.Apply(On);

        _system.Desktop = Guid.NewGuid();
        Wallpaper.All = @"C:\Old.jpg";
        _system.BackgroundChanged!();

        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.Equal(WallpaperBlock.None, _feature.Report!.Block);
    }

    [Fact]
    public void WhereTheDesktopIsntKnownAPictureOfItsOwnIsntSomeoneElses()
    {
        _feature.Apply(On);
        Wallpaper.All = @"C:\Night.jpg";

        _system.BackgroundChanged!();

        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.Equal(WallpaperBlock.None, _feature.Report!.Block);
    }

    [Fact]
    public void ExitingLeavesTheBackgroundAsItIs()
    {
        _feature.Apply(On);

        _feature.Dispose();

        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.True(File.Exists(_system.Store.FilePath));
    }

    [Fact]
    public void ABackgroundLeftToPutBackIsPutBackWhenTheHostStartsWithTheFeatureOff()
    {
        _feature.Apply(On);
        Wallpaper.Failure = new WallpaperException("Gone.", unchecked((int)0x800706BA));
        _feature.Apply(Off);
        Assert.Equal(@"C:\Day.jpg", Wallpaper.All);
        Assert.True(File.Exists(_system.Store.FilePath));

        using var restarted = new DynamicWallpaperFeature(_system, _time, _logger);
        restarted.Apply(Off);
        restarted.Apply(Off);

        Assert.Equal(@"C:\Old.jpg", Wallpaper.All);
        Assert.False(File.Exists(_system.Store.FilePath));
        Assert.Equal(2, _system.Workers);
    }

    private static System.Text.Json.JsonElement On => Section("""
        { "enabled": true, "sun": { "day": { "picture": "C:\\Day.jpg" }, "night": { "picture": "C:\\Night.jpg" } } }
        """);

    private static System.Text.Json.JsonElement Off => Section("""
        { "enabled": false, "sun": { "day": { "picture": "C:\\Day.jpg" }, "night": { "picture": "C:\\Night.jpg" } } }
        """);
}
