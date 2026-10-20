using System;
using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>Network rates, in bits per second.</summary>
public readonly record struct NetworkRates(double ReceiveBitsPerSecond, double SendBitsPerSecond);

/// <summary>
/// How fast the PC's network adapters receive and send data. Only hardware adapters count:
/// filters, loopback and virtual adapters, such as VPNs, are left out, since their traffic also
/// passes through a hardware adapter and would count twice. Use it on one thread.
/// </summary>
public sealed class NetworkMeter
{
    private readonly ISystemReadings _readings;
    private readonly TimeProvider _time;
    private Dictionary<ulong, (ulong Received, ulong Sent)> _last = [];
    private long? _lastTimestamp;

    public NetworkMeter(ISystemReadings readings, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(time);

        _readings = readings;
        _time = time;
        Restart();
    }

    /// <summary>Whether an adapter's counts belong to the meter: an up hardware adapter, not a filter or loopback.</summary>
    public static bool Counts(NetworkAdapterCounts adapter) =>
        adapter is { IsHardware: true, IsFilter: false, IsLoopback: false, IsUp: true };

    /// <summary>
    /// The rates since the last reading, over the time measured between the two, or
    /// <see langword="null"/> when there's no earlier reading or Windows refused. An adapter that
    /// appears or disappears between two readings, or whose counts go back, adds nothing.
    /// </summary>
    public NetworkRates? Read()
    {
        var timestamp = _time.GetTimestamp();
        IReadOnlyList<NetworkAdapterCounts> adapters;
        try
        {
            adapters = _readings.ReadNetworkAdapters();
        }
        catch (Win32Exception)
        {
            return null;
        }

        var previous = _last;
        var previousTimestamp = _lastTimestamp;
        (_last, _lastTimestamp) = (Snapshot(adapters), timestamp);
        if (previousTimestamp is not { } earlier)
        {
            return null;
        }

        var seconds = _time.GetElapsedTime(earlier, timestamp).TotalSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        ulong received = 0, sent = 0;
        foreach (var (id, counts) in _last)
        {
            if (previous.TryGetValue(id, out var before) && counts.Received >= before.Received && counts.Sent >= before.Sent)
            {
                received += counts.Received - before.Received;
                sent += counts.Sent - before.Sent;
            }
        }

        return new NetworkRates(received * 8 / seconds, sent * 8 / seconds);
    }

    /// <summary>Starts over, so the next reading covers only the time from now, as after a pause.</summary>
    public void Restart()
    {
        try
        {
            _last = Snapshot(_readings.ReadNetworkAdapters());
            _lastTimestamp = _time.GetTimestamp();
        }
        catch (Win32Exception)
        {
            _last = [];
            _lastTimestamp = null;
        }
    }

    private static Dictionary<ulong, (ulong Received, ulong Sent)> Snapshot(IReadOnlyList<NetworkAdapterCounts> adapters)
    {
        var snapshot = new Dictionary<ulong, (ulong, ulong)>();
        foreach (var adapter in adapters)
        {
            if (Counts(adapter))
            {
                snapshot[adapter.Id] = (adapter.BytesReceived, adapter.BytesSent);
            }
        }

        return snapshot;
    }
}
