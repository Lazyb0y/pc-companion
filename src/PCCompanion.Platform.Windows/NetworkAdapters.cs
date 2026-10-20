using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;

namespace PCCompanion.Platform.Windows;

/// <summary>A network adapter's byte counts, and what kind of adapter it is.</summary>
/// <param name="Id">The adapter's locally unique ID (LUID), which stays the same while Windows runs.</param>
/// <param name="IsHardware">Whether it's a hardware adapter, such as Ethernet, Wi-Fi or cellular, rather than a virtual one.</param>
/// <param name="IsFilter">Whether it's a filter that a driver puts over another adapter, which reports that adapter's counts.</param>
/// <param name="BytesReceived">The bytes received since the adapter started; the count starts again when it's reset.</param>
/// <param name="BytesSent">The bytes sent since the adapter started.</param>
public readonly record struct NetworkAdapterCounts(ulong Id, bool IsHardware, bool IsFilter, bool IsLoopback, bool IsUp, ulong BytesReceived, ulong BytesSent);

/// <summary>
/// The network adapters, as <c>GetIfTable2</c> lists them. Their names and hardware addresses
/// aren't read out.
/// </summary>
public static class NetworkAdapters
{
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe IReadOnlyList<NetworkAdapterCounts> ReadCounts()
    {
        MIB_IF_TABLE2* table;
        var result = PInvoke.GetIfTable2(&table);
        if (result != WIN32_ERROR.NO_ERROR)
        {
            throw new Win32Exception((int)result);
        }

        try
        {
            var rows = table->Table.AsSpan((int)table->NumEntries);
            var counts = new NetworkAdapterCounts[rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                ref readonly var row = ref rows[i];
                counts[i] = new NetworkAdapterCounts(
                    row.InterfaceLuid.Value,
                    row.InterfaceAndOperStatusFlags.HardwareInterface,
                    row.InterfaceAndOperStatusFlags.FilterInterface,
                    row.Type == PInvoke.IF_TYPE_SOFTWARE_LOOPBACK,
                    row.OperStatus == IF_OPER_STATUS.IfOperStatusUp,
                    row.InOctets,
                    row.OutOctets);
            }

            return counts;
        }
        finally
        {
            PInvoke.FreeMibTable(table);
        }
    }
}
