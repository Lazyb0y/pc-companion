using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Decides what the meters show at each update: reads the meters that are on, draws them, and
/// sends only what changed. It's apart from Windows' windows and threads, so tests can drive it.
/// Use it on the meters thread.
/// </summary>
public sealed partial class MetersController : IDisposable
{
    // Windows puts the newest icon first, so CPU is added last, to show first.
    private static readonly Meter[] Order = [Meter.Network, Meter.Memory, Meter.Cpu];

    private readonly ISystemReadings _readings;
    private readonly IMeterAppearance _appearance;
    private readonly IMeterIcons _icons;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<Meter, (uint[] Pixels, string ToolTip)> _shown = [];
    private readonly HashSet<Meter> _refused = [];
    private SystemMetersSettings _settings = new();
    private CpuMeter? _cpu;
    private NetworkMeter? _network;

    public MetersController(ISystemReadings readings, IMeterAppearance appearance, IMeterIcons icons, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _readings = readings;
        _appearance = appearance;
        _icons = icons;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Starts reading the meters that are on and removes those that are off. A meter turned on
    /// takes its first reading now and shows at the next update, which measures from it.
    /// </summary>
    public void Apply(SystemMetersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        if (settings.Cpu)
        {
            _cpu ??= new CpuMeter(_readings, _logger);
        }
        else
        {
            _cpu?.Dispose();
            _cpu = null;
            Remove(Meter.Cpu);
        }

        if (!settings.Memory)
        {
            Remove(Meter.Memory);
        }

        if (settings.Network)
        {
            _network ??= new NetworkMeter(_readings, _time);
        }
        else
        {
            _network = null;
            Remove(Meter.Network);
        }
    }

    /// <summary>Reads the meters, and shows each one whose picture or tooltip changed.</summary>
    public void Update()
    {
        var size = _appearance.IconSize();
        var color = _appearance.Color();
        var culture = CultureInfo.CurrentCulture;
        foreach (var meter in Order)
        {
            if (Draw(meter, size, color, culture) is { } picture)
            {
                Show(meter, picture.Pixels, size, picture.ToolTip);
            }
        }
    }

    /// <summary>
    /// Starts the readings over after a pause, so the next update covers only the time from now.
    /// The meters keep their last values until then.
    /// </summary>
    public void Resume()
    {
        _cpu?.Restart();
        _network?.Restart();
    }

    /// <summary>Removes every meter.</summary>
    public void Dispose()
    {
        foreach (var meter in _shown.Keys.ToArray())
        {
            Remove(meter);
        }

        _cpu?.Dispose();
        _cpu = null;
    }

    private static string Name(Meter meter) => meter switch
    {
        Meter.Cpu => Strings.SystemMetersCpuHeader,
        Meter.Memory => Strings.SystemMetersMemoryHeader,
        _ => Strings.SystemMetersNetworkHeader,
    };

    private (uint[] Pixels, string ToolTip)? Draw(Meter meter, int size, uint color, CultureInfo culture)
    {
        switch (meter)
        {
            case Meter.Cpu when _cpu?.Read() is { } cpu:
                return (MeterIcons.Number(MeterText.WholePercent(cpu), size, color), MeterText.CpuToolTip(cpu, culture));
            case Meter.Memory when _settings.Memory:
                try
                {
                    var memory = MemoryUse.From(_readings.ReadMemory());
                    return (MeterIcons.Number(MeterText.WholePercent(memory.Percent), size, color), MeterText.MemoryToolTip(memory, culture));
                }
                catch (Win32Exception)
                {
                    return null;
                }

            case Meter.Network when _network?.Read() is { } rates:
                return (MeterIcons.NetworkSlots(rates.ReceiveBitsPerSecond, rates.SendBitsPerSecond, size, color), MeterText.NetworkToolTip(rates, culture));
            default:
                return null;
        }
    }

    private void Show(Meter meter, uint[] pixels, int size, string toolTip)
    {
        if (_shown.TryGetValue(meter, out var shown) && shown.ToolTip == toolTip && shown.Pixels.AsSpan().SequenceEqual(pixels))
        {
            return;
        }

        if (_icons.Show(meter, pixels, size, Name(meter), toolTip))
        {
            _shown[meter] = (pixels, toolTip);
            _refused.Remove(meter);
        }
        else if (_refused.Add(meter))
        {
            LogRefused(_logger, meter);
        }
    }

    private void Remove(Meter meter)
    {
        if (_shown.Remove(meter) | _refused.Remove(meter))
        {
            _icons.Remove(meter);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't show the {Meter} meter; it's shown when the taskbar is back.")]
    private static partial void LogRefused(ILogger logger, Meter meter);
}
