using System;
using System.Threading.Tasks;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class DynamicWallpaperPipeTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDynamicWallpaperSystem _system = new();
    private readonly DynamicWallpaperFeature _feature;

    public DynamicWallpaperPipeTests()
    {
        _feature = new DynamicWallpaperFeature(_system, new ManualTimeProvider { Now = Noon }, new RecordingLogger<DynamicWallpaperFeature>());
    }

    public void Dispose()
    {
        _feature.Dispose();
        _system.Dispose();
    }

    [Fact]
    public void ThePipeIsServedWhileTheFeatureIsOn()
    {
        Assert.Null(_system.Pipe);

        _feature.Apply(On);
        Assert.NotNull(_system.Pipe);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.Null(_system.Pipe);
    }

    [Fact]
    public async Task ThePipeAnswersWithTheFeaturesState()
    {
        _feature.Apply(On);

        var answer = await Ask(DynamicWallpaperRequestKind.State);

        Assert.True(answer.Running);
        Assert.Equal("Sun.Day", answer.Period);
        Assert.Equal(WallpaperBlock.None, answer.Block);
        Assert.Empty(answer.Issues);
        Assert.False(answer.Spotlight);
    }

    [Fact]
    public async Task ThePipeSaysWhatsInTheWay()
    {
        _system.PictureFiles.States[@"C:\Day.jpg"] = PictureState.OnlineOnly;
        _feature.Apply(On);

        var answer = await Ask(DynamicWallpaperRequestKind.State);

        Assert.Equal([new PictureIssue(-1, PictureProblem.OnlineOnly)], answer.Issues);
    }

    [Fact]
    public async Task ResumeThroughThePipeSetsThePeriodsPicture()
    {
        _feature.Apply(On);
        _system.Wallpaper.All = @"C:\Mine.jpg";
        _system.BackgroundChanged!();
        Assert.Equal(WallpaperBlock.SteppedAside, (await Ask(DynamicWallpaperRequestKind.State)).Block);

        var answer = await Ask(DynamicWallpaperRequestKind.Resume);

        Assert.Equal(WallpaperBlock.None, answer.Block);
        Assert.Equal(@"C:\Day.jpg", _system.Wallpaper.All);
    }

    [Fact]
    public async Task ThePipeSaysWhenWindowsSpotlightWasOnBefore()
    {
        _system.Spotlight = true;
        _feature.Apply(On);

        Assert.True((await Ask(DynamicWallpaperRequestKind.State)).Spotlight);
    }

    [Fact]
    public async Task AnythingElseGetsTheState()
    {
        _feature.Apply(On);

        var answer = DynamicWallpaperPipe.ReadAnswer(await _system.Pipe!("not json"u8.ToArray()));

        Assert.Equal("Sun.Day", answer!.Period);
    }

    [Fact]
    public void RequestsAndAnswersKeepTheirValues()
    {
        var request = DynamicWallpaperPipe.ReadRequest(DynamicWallpaperPipe.Write(new DynamicWallpaperRequest { Kind = DynamicWallpaperRequestKind.Resume }));
        var answer = new DynamicWallpaperAnswer
        {
            Running = true,
            Period = "Times.2",
            Block = WallpaperBlock.Managed,
            Issues = [new PictureIssue(1, PictureProblem.NoPictures)],
            Spotlight = true,
        };

        var read = DynamicWallpaperPipe.ReadAnswer(DynamicWallpaperPipe.Write(answer))!;

        Assert.Equal(DynamicWallpaperRequestKind.Resume, request!.Kind);
        Assert.Equal((true, "Times.2", WallpaperBlock.Managed, true), (read.Running, read.Period, read.Block, read.Spotlight));
        Assert.Equal(answer.Issues, read.Issues);
        Assert.Null(DynamicWallpaperPipe.ReadRequest("[1, 2]"u8.ToArray()));
    }

    [Fact]
    public async Task TheSettingsAppAsksThroughANamedPipe()
    {
        var name = "PCCompanion.Tests." + Guid.NewGuid().ToString("N");
        var answer = new DynamicWallpaperAnswer { Running = true, Period = "WindowsMode.Dark" };
        using (RequestPipe.Serve(name, DynamicWallpaperPipe.MaximumRequestLength, _ => Task.FromResult(DynamicWallpaperPipe.Write(answer)), _ => { }))
        {
            var read = await DynamicWallpaperPipe.AskAsync(name, new DynamicWallpaperRequest(), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal("WindowsMode.Dark", read!.Period);
        }

        Assert.Null(await DynamicWallpaperPipe.AskAsync(name, new DynamicWallpaperRequest(), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
    }

    private static System.Text.Json.JsonElement On => Section("""
        { "enabled": true, "sun": { "day": { "picture": "C:\\Day.jpg" }, "night": { "picture": "C:\\Night.jpg" } } }
        """);

    private async Task<DynamicWallpaperAnswer> Ask(DynamicWallpaperRequestKind kind) =>
        DynamicWallpaperPipe.ReadAnswer(await _system.Pipe!(DynamicWallpaperPipe.Write(new DynamicWallpaperRequest { Kind = kind })))!;
}
