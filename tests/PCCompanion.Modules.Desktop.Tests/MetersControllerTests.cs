using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// Fake readings and icons, on the test's thread.
public sealed class MetersControllerTests : IDisposable
{
    private static readonly SystemMetersSettings AllMeters = new() { Enabled = true };

    private readonly FakeMetersSystem _system = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<MetersControllerTests> _logger = new();
    private readonly FakeMetersSystem.FakeMeterIcons _icons;
    private readonly MetersController _controller;

    public MetersControllerTests()
    {
        _icons = new FakeMetersSystem.FakeMeterIcons(_system);
        _controller = new MetersController(_system.Readings, _system.Appearance, _icons, _time, _logger);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void TheMetersShowAtTheFirstUpdateWithCpuAddedLast()
    {
        _controller.Apply(AllMeters);

        UpdateAfter(TimeSpan.FromSeconds(1));

        // Windows shows the newest icon first, so CPU comes first.
        Assert.Equal(["show Network", "show Memory", "show Cpu"], _system.Events);
        Assert.Equal(MeterText.MemoryToolTip(MemoryUse.From(_system.FakeReadings.Memory), CultureInfo.CurrentCulture), _icons.ToolTips[Meter.Memory]);
    }

    [Fact]
    public void OnlyTheMetersThatAreOnShow()
    {
        _controller.Apply(AllMeters with { Cpu = false, Network = false });

        UpdateAfter(TimeSpan.FromSeconds(1));

        Assert.Equal(["show Memory"], _system.Events);
        Assert.Equal(0, _system.FakeReadings.UtilityReads);
    }

    [Fact]
    public void OnlyMetersThatChangedAreSentAgain()
    {
        _controller.Apply(AllMeters);
        UpdateAfter(TimeSpan.FromSeconds(1));

        UpdateAfter(TimeSpan.FromSeconds(1));
        Assert.Equal(3, _system.Events.Count);

        _system.FakeReadings.Memory = new PhysicalMemory(16UL << 30, 4UL << 30);
        UpdateAfter(TimeSpan.FromSeconds(1));
        Assert.Equal("show Memory", Assert.Single(_system.Events.Skip(3)));
    }

    [Fact]
    public void MetersTurnedOffAreRemovedAndNoLongerRead()
    {
        _controller.Apply(AllMeters);
        UpdateAfter(TimeSpan.FromSeconds(1));

        _controller.Apply(AllMeters with { Cpu = false, Network = false });
        UpdateAfter(TimeSpan.FromSeconds(1));

        Assert.Equal(["remove Cpu", "remove Network"], _system.Events.Skip(3));
        Assert.True(_system.FakeReadings.UtilityDisposed);
    }

    [Fact]
    public void AMeterTurnedOnAgainShowsAtTheNextUpdate()
    {
        _controller.Apply(AllMeters with { Network = false });
        UpdateAfter(TimeSpan.FromSeconds(1));

        _controller.Apply(AllMeters);
        UpdateAfter(TimeSpan.FromSeconds(1));

        Assert.Equal("show Network", Assert.Single(_system.Events.Skip(2)));
    }

    [Fact]
    public void ARefusedIconIsLoggedOnceAndShownWhenWindowsTakesItAgain()
    {
        _icons.Refuse = true;
        _controller.Apply(AllMeters with { Cpu = false, Network = false });

        UpdateAfter(TimeSpan.FromSeconds(1));
        UpdateAfter(TimeSpan.FromSeconds(1));
        _icons.Refuse = false;
        UpdateAfter(TimeSpan.FromSeconds(1));

        Assert.Equal(["refuse Memory", "refuse Memory", "show Memory"], _system.Events);
        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void ResumeStartsTheReadingsOver()
    {
        _system.FakeReadings.Adapters.Add(Adapter(received: 0));
        _controller.Apply(AllMeters with { Cpu = false, Memory = false });

        // While paused, 10 seconds and 1,000 bytes go by.
        _time.Advance(TimeSpan.FromSeconds(10));
        _system.FakeReadings.Adapters[0] = Adapter(received: 1_000);
        _controller.Resume();

        _system.FakeReadings.Adapters[0] = Adapter(received: 1_125);
        UpdateAfter(TimeSpan.FromSeconds(1));

        Assert.Equal(MeterText.NetworkToolTip(new NetworkRates(1_000, 0), CultureInfo.CurrentCulture), _icons.ToolTips[Meter.Network]);
    }

    [Fact]
    public void DisposeRemovesEveryMeterShown()
    {
        _controller.Apply(AllMeters);
        UpdateAfter(TimeSpan.FromSeconds(1));

        _controller.Dispose();

        Assert.Equal(["remove Cpu", "remove Memory", "remove Network"], _system.Events.Skip(3).Order(StringComparer.Ordinal));
        Assert.True(_system.FakeReadings.UtilityDisposed);
    }

    private static NetworkAdapterCounts Adapter(ulong received) =>
        new(1, IsHardware: true, IsFilter: false, IsLoopback: false, IsUp: true, BytesReceived: received, BytesSent: 0);

    private void UpdateAfter(TimeSpan time)
    {
        _time.Advance(time);
        _controller.Update();
    }
}
