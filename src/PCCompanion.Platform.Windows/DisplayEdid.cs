using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Display;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A display's serial number from its EDID: a number, text, or both. Some models report the
/// same serial number on every display, or none.
/// </summary>
/// <param name="Number">The serial number; 0 when the EDID has none.</param>
/// <param name="Text">The serial number text; empty when the EDID has none.</param>
public readonly record struct DisplaySerialNumber(uint Number, string Text);

/// <summary>
/// Reads displays' EDIDs, the data every display reports about itself, for their serial numbers.
/// Nothing else from the EDID is passed on.
/// </summary>
public static class DisplayEdid
{
    private const int BlockLength = 128;
    private const int DescriptorLength = 18;

    private static ReadOnlySpan<byte> Header => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// Reads the serial number of the display with a device path, such as a
    /// <see cref="ConnectedDisplay.DevicePath"/>, through Windows' <c>DisplayMonitor</c>; null
    /// when Windows has no EDID for it, or the EDID has no serial number.
    /// </summary>
    /// <remarks>
    /// The first read in a process loads Windows' display monitor components, which took 80 ms and
    /// about 2 MB of memory; reads after that took about 12 ms.
    /// </remarks>
    public static async Task<DisplaySerialNumber?> ReadSerialNumberAsync(string devicePath)
    {
        var monitor = await DisplayMonitor.FromInterfaceIdAsync(devicePath);
        return monitor is null ? null : SerialNumberOf(monitor.GetDescriptor(DisplayMonitorDescriptorKind.Edid));
    }

    /// <summary>The serial number in an EDID; null when it has none, or isn't an EDID.</summary>
    public static DisplaySerialNumber? SerialNumberOf(ReadOnlySpan<byte> edid)
    {
        if (edid.Length < BlockLength || !edid[..Header.Length].SequenceEqual(Header))
        {
            return null;
        }

        var number = BinaryPrimitives.ReadUInt32LittleEndian(edid[12..]);
        var text = "";

        // The serial number text is in one of the four 18-byte descriptors, tagged 0xFF.
        for (var offset = 54; offset + DescriptorLength <= BlockLength; offset += DescriptorLength)
        {
            var descriptor = edid.Slice(offset, DescriptorLength);
            if (descriptor[0] == 0 && descriptor[1] == 0 && descriptor[2] == 0 && descriptor[3] == 0xFF)
            {
                text = TextOf(descriptor[5..]);
                break;
            }
        }

        return number == 0 && text.Length == 0 ? null : new DisplaySerialNumber(number, text);
    }

    // ASCII text, ended by a line feed and padded with spaces; other characters are left out.
    private static string TextOf(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            if (value == '\n')
            {
                break;
            }

            if (value is >= 0x20 and < 0x7F)
            {
                text.Append((char)value);
            }
        }

        return text.ToString().Trim();
    }
}
