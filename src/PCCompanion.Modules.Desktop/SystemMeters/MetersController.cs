using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>Where the meters show, and why.</summary>
public enum MetersPlace
{
    /// <summary>Nothing has shown yet.</summary>
    None,
    OnTaskbar,

    /// <summary>The strip is hidden with the taskbar while a full-screen app is in front.</summary>
    HiddenWithTaskbar,

    /// <summary>Notification icons, as the user chose.</summary>
    IconsChosen,

    /// <summary>Notification icons, since the strip doesn't run on this Windows build.</summary>
    IconsUntestedBuild,

    /// <summary>Notification icons, since the taskbar isn't running or didn't answer.</summary>
    IconsNoTaskbar,

    /// <summary>Notification icons, since the taskbar hides itself.</summary>
    IconsAutoHide,

    /// <summary>Notification icons, since the taskbar has no room for the strip.</summary>
    IconsNoRoom,
}

/// <summary>
/// Decides what the meters show at each update: reads the meters that are on, then shows them on
/// the strip, or as notification icons where the strip can't show, sending only what changed. It's
/// apart from Windows' windows and threads, so tests can drive it. Use it on the meters thread.
/// </summary>
public sealed partial class MetersController : IDisposable
{
    // Windows puts the newest icon first, so CPU is added last, to show first.
    private static readonly Meter[] Order = [Meter.Network, Meter.Memory, Meter.Cpu];

    private readonly ISystemReadings _readings;
    private readonly IMeterAppearance _appearance;
    private readonly IMeterIcons _icons;
    private readonly Func<MeterStrip?> _createStrip;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<Meter, (uint[] Pixels, string ToolTip)> _shown = [];
    private readonly HashSet<Meter> _refused = [];
    private SystemMetersSettings _settings = new();
    private CpuMeter? _cpu;
    private NetworkMeter? _network;
    private MeterStrip? _strip;
    private bool _stripUnavailable;
    private (double? Cpu, MemoryUse? Memory, NetworkRates? Network) _values;
    private bool _hasValues;
    private bool _paused;

    /// <param name="createStrip">
    /// Creates the strip, or returns <see langword="null"/> when it can't run on this Windows build.
    /// </param>
    public MetersController(ISystemReadings readings, IMeterAppearance appearance, IMeterIcons icons, Func<MeterStrip?> createStrip, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(createStrip);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _readings = readings;
        _appearance = appearance;
        _icons = icons;
        _createStrip = createStrip;
        _time = time;
        _logger = logger;
    }

    public MetersPlace Place { get; private set; }

    /// <summary>
    /// Starts reading the meters that are on and removes those that are off, and creates the strip
    /// or removes it. A meter turned on takes its first reading now and shows at the next update,
    /// which measures from it.
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

        // A meter turned off forgets its value, so it shows nothing until it's read again.
        _values = (settings.Cpu ? _values.Cpu : null, settings.Memory ? _values.Memory : null, settings.Network ? _values.Network : null);

        if (settings.ShowOn == ShowOn.Taskbar)
        {
            if (_strip is null && !_stripUnavailable)
            {
                _strip = _createStrip();
                _stripUnavailable = _strip is null;
                if (_paused)
                {
                    _strip?.PauseWatching();
                }
            }
        }
        else
        {
            _strip?.Dispose();
            _strip = null;
        }

        // A new layout or style shows at once, with the values the meters have, or when the
        // meters resume.
        if (_hasValues && !_paused)
        {
            Present();
        }
    }

    /// <summary>Reads the meters, and shows what changed.</summary>
    public void Update()
    {
        Read();
        Present();
    }

    /// <summary>
    /// Finds the strip's place again, as when the taskbar may have changed. While the meters are
    /// paused, it waits until they resume.
    /// </summary>
    public void LayOutStrip()
    {
        if (_strip is null || _paused)
        {
            return;
        }

        _strip.LayOut();
        if (_hasValues)
        {
            Present();
        }
    }

    /// <summary>Keeps the strip in front of the taskbar, or hidden with it.</summary>
    public void KeepStripInSight() => _strip?.KeepInSight();

    /// <summary>Draws the strip again, as when the pointer comes onto it or leaves it.</summary>
    public void RedrawStrip() => _strip?.Redraw();

    /// <summary>
    /// Pauses the meters, as while the session is locked or the displays are off: the strip stops
    /// watching the taskbar, and nothing is laid out or shown until <see cref="Resume"/>.
    /// </summary>
    public void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _strip?.PauseWatching();
    }

    /// <summary>
    /// Starts the readings over, so the next update covers only the time from now, as when the
    /// meters start and after a pause. The meters keep their last values until then. After a
    /// pause, the strip watches the taskbar again and finds its place once, since the taskbar may
    /// have changed meanwhile.
    /// </summary>
    public void Resume()
    {
        _cpu?.Restart();
        _network?.Restart();
        if (!_paused)
        {
            return;
        }

        _paused = false;
        if (_strip is not null)
        {
            _strip.ResumeWatching();
            LayOutStrip();
        }
    }

    /// <summary>Removes the strip and every icon.</summary>
    public void Dispose()
    {
        RemoveIcons();
        _strip?.Dispose();
        _strip = null;
        _cpu?.Dispose();
        _cpu = null;
    }

    private static string Name(Meter meter) => meter switch
    {
        Meter.Cpu => Strings.SystemMetersCpuHeader,
        Meter.Memory => Strings.SystemMetersMemoryHeader,
        _ => Strings.SystemMetersNetworkHeader,
    };

    // A meter whose reading failed keeps its last value.
    private void Read()
    {
        MemoryUse? memory = null;
        if (_settings.Memory)
        {
            try
            {
                memory = MemoryUse.From(_readings.ReadMemory());
            }
            catch (Win32Exception)
            {
                // It keeps its last value.
            }
        }

        var cpu = _cpu?.Read();
        var network = _network?.Read();
        _values = (
            _settings.Cpu ? cpu ?? _values.Cpu : null,
            _settings.Memory ? memory ?? _values.Memory : null,
            _settings.Network ? network ?? _values.Network : null);
        _hasValues = true;
    }

    private void Present()
    {
        var culture = CultureInfo.CurrentCulture;
        if (_strip is not null)
        {
            var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
            var state = _strip.Show(
                StripStyle.From(_settings, rightToLeft),
                Texts(culture),
                new StripColors(_appearance.Color(), _appearance.IsHighContrast()),
                Description(culture));

            if (state is StripState.Shown or StripState.HiddenWithTaskbar)
            {
                RemoveIcons();
                Move(state == StripState.Shown ? MetersPlace.OnTaskbar : MetersPlace.HiddenWithTaskbar);
                return;
            }

            Move(state switch
            {
                StripState.AutoHide => MetersPlace.IconsAutoHide,
                StripState.NoRoom => MetersPlace.IconsNoRoom,
                _ => MetersPlace.IconsNoTaskbar,
            });
        }
        else
        {
            Move(_settings.ShowOn == ShowOn.Taskbar ? MetersPlace.IconsUntestedBuild : MetersPlace.IconsChosen);
        }

        ShowIcons(culture);
    }

    private StripTexts Texts(CultureInfo culture) => new(
        _values.Cpu is { } cpu ? MeterText.Percent(cpu, culture) : null,
        _values.Memory is { } memory ? MeterText.Percent(memory.Percent, culture) : null,
        _values.Network is { } receive ? MeterText.Rate(receive.ReceiveBitsPerSecond, culture) : null,
        _values.Network is { } send ? MeterText.Rate(send.SendBitsPerSecond, culture) : null);

    // Every meter's tooltip, one after another, for the strip's tooltip and name.
    private string Description(CultureInfo culture)
    {
        var lines = new List<string>();
        if (_values.Cpu is { } cpu)
        {
            lines.Add(MeterText.CpuToolTip(cpu, culture));
        }

        if (_values.Memory is { } memory)
        {
            lines.Add(MeterText.MemoryToolTip(memory, culture));
        }

        if (_values.Network is { } rates)
        {
            lines.Add(MeterText.NetworkToolTip(rates, culture));
        }

        return lines.Count > 0 ? string.Join('\n', lines) : Strings.SystemMetersName;
    }

    private void Move(MetersPlace place)
    {
        if (place != Place)
        {
            Place = place;
            LogMoved(_logger, place);
        }
    }

    private void ShowIcons(CultureInfo culture)
    {
        var size = _appearance.IconSize();
        var color = _appearance.Color();
        foreach (var meter in Order)
        {
            (uint[] Pixels, string ToolTip)? picture = meter switch
            {
                Meter.Cpu when _values.Cpu is { } cpu =>
                    (MeterIcons.Number(MeterText.WholePercent(cpu), size, color), MeterText.CpuToolTip(cpu, culture)),
                Meter.Memory when _values.Memory is { } memory =>
                    (MeterIcons.Number(MeterText.WholePercent(memory.Percent), size, color), MeterText.MemoryToolTip(memory, culture)),
                Meter.Network when _values.Network is { } rates =>
                    (MeterIcons.NetworkSlots(rates.ReceiveBitsPerSecond, rates.SendBitsPerSecond, size, color), MeterText.NetworkToolTip(rates, culture)),
                _ => null,
            };

            if (picture is { } shown)
            {
                Show(meter, shown.Pixels, size, shown.ToolTip);
            }
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

    private void RemoveIcons()
    {
        foreach (var meter in _shown.Keys.Concat(_refused).ToArray())
        {
            Remove(meter);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters show: {Place}.")]
    private static partial void LogMoved(ILogger logger, MetersPlace place);
}
