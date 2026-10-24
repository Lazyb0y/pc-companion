using System;
using System.Globalization;
using System.Text;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// The meters' values as text: strings in the host's language, and numbers with the user's
/// regional settings, as Task Manager writes them. Formats are read at each call, so a new
/// language shows at the next update.
/// </summary>
public static class MeterText
{
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    /// <summary>A percentage as the whole number the icons show: 41.5 is 42.</summary>
    public static int WholePercent(double percent) => (int)Math.Round(Math.Clamp(percent, 0, 100), MidpointRounding.AwayFromZero);

    /// <summary>A percentage in the culture's format, such as <c>42%</c> or <c>42 %</c>.</summary>
    public static string Percent(double percent, CultureInfo culture) => (WholePercent(percent) / 100d).ToString("P0", culture);

    /// <summary>A size in GB of 1,024 MB, with one decimal, such as <c>9.7 GB</c>.</summary>
    public static string Size(ulong bytes, CultureInfo culture) =>
        string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersGigabytes), (bytes / BytesPerGigabyte).ToString("F1", culture));

    /// <summary>
    /// A rate in Kbps, Mbps or Gbps of 1,000 bits, with one decimal below 100, such as
    /// <c>56.0 Kbps</c>, and none from 100, such as <c>120 Kbps</c>. No traffic is <c>0 Kbps</c>.
    /// </summary>
    public static string Rate(double bitsPerSecond, CultureInfo culture)
    {
        if (bitsPerSecond <= 0)
        {
            return string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersKilobitsPerSecond), 0.ToString(culture));
        }

        string[] units = [Strings.SystemMetersKilobitsPerSecond, Strings.SystemMetersMegabitsPerSecond, Strings.SystemMetersGigabitsPerSecond];
        var value = bitsPerSecond / 1000;
        var unit = 0;

        // Moves to the next unit when the number would round to 1,000 or more.
        while (unit < units.Length - 1 && Math.Round(value, value < 100 ? 1 : 0) >= 1000)
        {
            value /= 1000;
            unit++;
        }

        // Decided after rounding, so 99.97 shows as 100, not 100.0.
        var format = Math.Round(value, 1, MidpointRounding.AwayFromZero) < 100 ? "F1" : "F0";
        return string.Format(culture, CompositeFormat.Parse(units[unit]), value.ToString(format, culture));
    }

    public static string CpuToolTip(double percent, CultureInfo culture) =>
        string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersCpuToolTip), Percent(percent, culture));

    public static string MemoryToolTip(MemoryUse memory, CultureInfo culture) =>
        string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersMemoryToolTip), Percent(memory.Percent, culture)) + "\n"
        + string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersMemoryInUse), Size(memory.UsedBytes, culture), Size(memory.TotalBytes, culture));

    public static string NetworkToolTip(NetworkRates rates, CultureInfo culture) =>
        Strings.SystemMetersNetworkToolTip + "\n"
        + string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersReceive), Rate(rates.ReceiveBitsPerSecond, culture)) + "\n"
        + string.Format(culture, CompositeFormat.Parse(Strings.SystemMetersSend), Rate(rates.SendBitsPerSecond, culture));
}
