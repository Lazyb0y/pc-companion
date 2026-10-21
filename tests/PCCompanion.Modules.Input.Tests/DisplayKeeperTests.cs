using System;
using System.Drawing;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Drives the detector and the keeper as the feature does, with a fake request instead of Windows'.
public sealed class DisplayKeeperTests
{
    private static readonly Rectangle Main = Rectangle.FromLTRB(0, 0, 1920, 1080);
    private static readonly Point Middle = new(960, 540);
    private static readonly Point TopLeft = new(0, 0);
    private static readonly Point BottomRight = new(1919, 1079);

    private readonly FakeHotCornerEnvironment _environment = new() { PointerPosition = Middle };
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<DisplayKeeper> _logger = new();
    private readonly CornerDetector _detector;
    private readonly DisplayKeeper _keeper;
    private HotCornersSettings _settings = Settings(HotCornerAction.KeepDisplaysOn);
    private int _requests;
    private int _endedRequests;
    private bool _refused;

    public DisplayKeeperTests()
    {
        _detector = new CornerDetector(_environment, _clock);
        _keeper = new DisplayKeeper(_logger, () =>
        {
            if (_refused)
            {
                return null;
            }

            _requests++;
            return new FakeRequest(() => _endedRequests++);
        });
        Configure();
    }

    [Fact]
    public void TheDisplaysAreKeptOnAfterTheActivationDelay()
    {
        Assert.Equal(CornerOutcome.Waiting, MoveTo(TopLeft));
        Assert.False(_keeper.IsKeepingOn);

        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(CornerOutcome.Triggered, Update());

        Assert.True(_keeper.IsKeepingOn);
        Assert.Equal(1, _requests);
    }

    [Fact]
    public void StayingInTheCornerKeepsOneRequest()
    {
        KeepOnFromTopLeft();

        _clock.Advance(TimeSpan.FromMinutes(10));
        Update();

        Assert.True(_keeper.IsKeepingOn);
        Assert.Equal((1, 0), (_requests, _endedRequests));
    }

    [Fact]
    public void LeavingTheCornerEndsIt()
    {
        KeepOnFromTopLeft();

        MoveTo(new Point(1, 0));

        AssertEnded("PointerLeft");
    }

    [Fact]
    public void MovingStraightToAnotherCornerEndsIt()
    {
        _settings = Settings(HotCornerAction.KeepDisplaysOn) with { BottomRight = new CornerSettings { Action = HotCornerAction.Lock } };
        Configure();
        KeepOnFromTopLeft();

        MoveTo(BottomRight);

        AssertEnded("PointerLeft");
    }

    [Fact]
    public void AnotherActionForTheCornerEndsIt()
    {
        KeepOnFromTopLeft();

        _settings = Settings(HotCornerAction.TaskView);
        Configure();

        AssertEnded("ActionChanged");
    }

    [Fact]
    public void NoActionForTheCornerEndsIt()
    {
        KeepOnFromTopLeft();

        _settings = Settings(HotCornerAction.None);
        Configure();

        AssertEnded("ActionChanged");
    }

    [Fact]
    public void OtherChangesToTheSettingsOrDisplaysKeepIt()
    {
        KeepOnFromTopLeft();

        _settings = Settings(HotCornerAction.KeepDisplaysOn) with { ActivationDelayMilliseconds = 500, BottomRight = new CornerSettings { Action = HotCornerAction.Lock } };
        Configure();
        _detector.Configure(_settings, [Main, Rectangle.FromLTRB(1920, 0, 3840, 1080)]);
        _keeper.Check(_detector.CurrentCorner, _settings);

        Assert.True(_keeper.IsKeepingOn);
        Assert.Equal(0, _endedRequests);
    }

    [Fact]
    public void ADisplayChangeThatRemovesTheCornerEndsIt()
    {
        KeepOnFromTopLeft();

        // A display to the left: the pointer passes into it at the top-left corner now.
        _detector.Configure(_settings, [Main, Rectangle.FromLTRB(-1920, 0, 0, 1080)]);
        _keeper.Check(_detector.CurrentCorner, _settings);

        AssertEnded("PointerLeft");
    }

    [Fact]
    public void AfterALockTheCornerKeepsTheDisplaysOnOnlyOnceThePointerComesBack()
    {
        KeepOnFromTopLeft();

        _keeper.End(KeepDisplaysOnEnd.SessionLocked);
        AssertEnded("SessionLocked");

        _clock.Advance(TimeSpan.FromSeconds(1));
        Update();
        Assert.False(_keeper.IsKeepingOn);

        MoveTo(Middle);
        KeepOnFromTopLeft();
        Assert.Equal(2, _requests);
    }

    [Fact]
    public void StoppingHotCornersEndsIt()
    {
        KeepOnFromTopLeft();

        _keeper.End(KeepDisplaysOnEnd.HotCornersStopped);
        _keeper.End(KeepDisplaysOnEnd.HotCornersStopped);

        AssertEnded("HotCornersStopped");
    }

    [Fact]
    public void WhenWindowsRefusesTheDisplaysAreNotKeptOn()
    {
        _refused = true;

        Assert.False(_keeper.Start(ScreenCorner.TopLeft));

        Assert.False(_keeper.IsKeepingOn);
        _keeper.End(KeepDisplaysOnEnd.PointerLeft);
        Assert.Empty(_logger.Entries);
    }

    private static HotCornersSettings Settings(HotCornerAction topLeft) =>
        new() { TopLeft = new CornerSettings { Action = topLeft } };

    private void Configure()
    {
        _detector.Configure(_settings, [Main]);
        _keeper.Check(_detector.CurrentCorner, _settings);
    }

    private CornerOutcome MoveTo(Point position)
    {
        _environment.PointerPosition = position;
        return Update();
    }

    // As the feature does on each pointer event and timer.
    private CornerOutcome Update()
    {
        var decision = _detector.Update();
        _keeper.Check(_detector.CurrentCorner, _settings);
        if (decision is { Outcome: CornerOutcome.Triggered, Action: HotCornerAction.KeepDisplaysOn })
        {
            Assert.True(_keeper.Start(decision.Corner));
        }

        return decision.Outcome;
    }

    private void KeepOnFromTopLeft()
    {
        MoveTo(TopLeft);
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(CornerOutcome.Triggered, Update());
        Assert.True(_keeper.IsKeepingOn);
    }

    private void AssertEnded(string reason)
    {
        Assert.False(_keeper.IsKeepingOn);
        Assert.Equal(_requests, _endedRequests);
        Assert.Equal([(LogLevel.Debug, $"Keep displays on ended: {reason}.")], _logger.Entries);
    }

    private sealed class FakeRequest(Action ended) : IDisposable
    {
        public void Dispose() => ended();
    }
}
