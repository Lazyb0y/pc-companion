using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class MeterReadingTests
{
    private readonly FakeSystemReadings _readings = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<MeterReadingTests> _logger = new();

    [Fact]
    public void SettingsDefaultToEveryMeterEachSecond()
    {
        var settings = SystemMetersSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.True(settings.Cpu && settings.Memory && settings.Network);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.Interval);
    }

    [Fact]
    public void SettingsRoundTripWithOnlyTheirOwnProperties()
    {
        var settings = new SystemMetersSettings { Enabled = true, Network = false, UpdateIntervalSeconds = 5 };

        var section = settings.Write();

        Assert.Equal(settings, SystemMetersSettings.Read(section));
        Assert.Equal(
            """{"enabled":true,"cpu":true,"memory":true,"network":false,"updateIntervalSeconds":5}""",
            section.GetRawText());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(3, 3)]
    [InlineData(60, 10)]
    public void TheIntervalIsBroughtIntoRange(int seconds, int expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), new SystemMetersSettings { UpdateIntervalSeconds = seconds }.Interval);
    }

    [Fact]
    public void SettingsThatDontMatchTheFormatThrow()
    {
        using var document = JsonDocument.Parse("""{ "cpu": "yes" }""");

        Assert.Throws<JsonException>(() => SystemMetersSettings.Read(document.RootElement.Clone()));
    }

    [Fact]
    public void CpuReadsProcessorUtilityCappedAt100()
    {
        _readings.Utility.Enqueue(42.5);
        _readings.Utility.Enqueue(130);
        using var cpu = new CpuMeter(_readings, _logger);

        Assert.False(cpu.UsesProcessorTime);
        Assert.Equal(42.5, cpu.Read());
        Assert.Equal(100, cpu.Read());
    }

    [Fact]
    public void CpuFallsBackToProcessorTimeWhenTheCounterIsntAvailable()
    {
        _readings.UtilityAvailable = false;
        _readings.Times = new ProcessorTimes(Idle: 1000, Kernel: 3000, User: 1000);
        using var cpu = new CpuMeter(_readings, _logger);
        _readings.Times = new ProcessorTimes(Idle: 1600, Kernel: 3800, User: 1200);

        // 1,000 ticks passed, 600 of them idle.
        Assert.True(cpu.UsesProcessorTime);
        Assert.Equal(40, cpu.Read());
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void CpuFallsBackAfterTheCounterFailsThreeTimesInARow()
    {
        _readings.Utility.Enqueue(null);
        _readings.Utility.Enqueue(10);
        _readings.Utility.Enqueue(null);
        _readings.Utility.Enqueue(null);
        _readings.Utility.Enqueue(null);
        using var cpu = new CpuMeter(_readings, _logger);

        Assert.Null(cpu.Read());
        Assert.Equal(10, cpu.Read());
        Assert.Null(cpu.Read());
        Assert.Null(cpu.Read());
        Assert.False(cpu.UsesProcessorTime);
        Assert.Null(cpu.Read());

        Assert.True(cpu.UsesProcessorTime);
        Assert.True(_readings.UtilityDisposed);
    }

    [Fact]
    public void ProcessorTimeNeedsTimeToPassAndNeverGoesBack()
    {
        _readings.UtilityAvailable = false;
        using var cpu = new CpuMeter(_readings, _logger);
        _readings.Times = new ProcessorTimes(100, 300, 100);

        Assert.Null(cpu.Read()); // the first reading
        Assert.Null(cpu.Read()); // no time passed
        _readings.Times = new ProcessorTimes(50, 200, 50);
        Assert.Null(cpu.Read()); // times went back
        _readings.Times = new ProcessorTimes(150, 400, 50);
        Assert.Equal(50, cpu.Read());
    }

    [Fact]
    public void RestartingTheCpuMeterStartsTheNextReadingNow()
    {
        _readings.Utility.Enqueue(90); // used up by the restart
        _readings.Utility.Enqueue(12);
        using var cpu = new CpuMeter(_readings, _logger);

        cpu.Restart();

        Assert.Equal(12, cpu.Read());
    }

    [Theory]
    [InlineData(16UL << 30, 4UL << 30, 75.0)]
    [InlineData(16UL << 30, 16UL << 30, 0.0)]
    [InlineData(16UL << 30, 20UL << 30, 0.0)]
    public void MemoryInUseIsWhatsNotAvailable(ulong total, ulong available, double percent)
    {
        var use = MemoryUse.From(new PhysicalMemory(total, available));

        Assert.Equal(percent, use.Percent);
        Assert.Equal(total, use.TotalBytes);
        Assert.Equal(total - ulong.Min(available, total), use.UsedBytes);
    }

    [Fact]
    public void MemoryWithNoTotalIsNone()
    {
        Assert.Equal(default, MemoryUse.From(new PhysicalMemory(0, 0)));
    }

    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, false, false, true, false)] // virtual, such as a VPN
    [InlineData(true, true, false, true, false)] // a filter over a hardware adapter
    [InlineData(false, false, true, true, false)] // loopback
    [InlineData(true, false, false, false, false)] // down
    public void OnlyUpHardwareAdaptersCount(bool hardware, bool filter, bool loopback, bool up, bool counts)
    {
        Assert.Equal(counts, NetworkMeter.Counts(new NetworkAdapterCounts(1, hardware, filter, loopback, up, 0, 0)));
    }

    [Fact]
    public void NetworkRatesAreOverTheMeasuredTime()
    {
        _readings.Adapters.Add(Adapter(1, received: 1_000, sent: 500));
        _readings.Adapters.Add(Adapter(2, received: 0, sent: 0, hardware: false));
        var network = new NetworkMeter(_readings, _time);
        _readings.Adapters[0] = Adapter(1, received: 3_000, sent: 1_500);
        _readings.Adapters[1] = Adapter(2, received: 99_000, sent: 99_000, hardware: false);

        // Two seconds instead of one, as when an update comes late.
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(new NetworkRates(8_000, 4_000), network.Read());
    }

    [Fact]
    public void AdaptersThatAppearDisappearOrResetAddNothing()
    {
        _readings.Adapters.Add(Adapter(1, received: 1_000, sent: 1_000));
        _readings.Adapters.Add(Adapter(2, received: 5_000, sent: 5_000));
        var network = new NetworkMeter(_readings, _time);
        _readings.Adapters.Clear();
        _readings.Adapters.Add(Adapter(1, received: 10, sent: 10)); // reset
        _readings.Adapters.Add(Adapter(3, received: 7_000, sent: 7_000)); // new
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new NetworkRates(0, 0), network.Read());
    }

    [Fact]
    public void TheFirstNetworkReadingAfterAFailureIsNone()
    {
        _readings.AdaptersFail = true;
        var network = new NetworkMeter(_readings, _time);

        Assert.Null(network.Read());
        _readings.AdaptersFail = false;
        Assert.Null(network.Read());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new NetworkRates(0, 0), network.Read());
    }

    [Fact]
    public void NoTimePassedGivesNoRate()
    {
        var network = new NetworkMeter(_readings, _time);

        Assert.Null(network.Read());
    }

    private static NetworkAdapterCounts Adapter(ulong id, ulong received, ulong sent, bool hardware = true) =>
        new(id, hardware, IsFilter: false, IsLoopback: false, IsUp: true, received, sent);
}
