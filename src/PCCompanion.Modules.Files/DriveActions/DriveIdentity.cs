using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// The values that recognize a drive: the random ID in the ID file PC Companion writes on it, and
/// hashes of its serial numbers, which are never kept, shown or logged as they are. See
/// "Recognizing a drive" in docs/features/files/drive-actions.md.
/// </summary>
public static class DriveIdentity
{
    /// <summary>The ID file's name, at the drive's root.</summary>
    public const string IdFileName = ".pccompanion-drive";

    private const string HashText = "PCCompanion.DriveActions";
    private const int IdBytes = 16;
    private const int HashBytes = 8;

    /// <summary>A new random ID, as 32 lowercase hexadecimal digits.</summary>
    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdBytes));

    /// <summary>The hash of a volume serial number, as 16 lowercase hexadecimal digits.</summary>
    public static string HashVolumeSerial(uint serial) => Hash(serial.ToString("X8", CultureInfo.InvariantCulture));

    /// <summary>
    /// The hash of a device's hardware serial number, as 16 lowercase hexadecimal digits, or an empty
    /// text for a device without one. Spaces around it, which some devices add, don't count.
    /// </summary>
    public static string HashHardwareSerial(string? serial) =>
        string.IsNullOrWhiteSpace(serial) ? string.Empty : Hash(serial.Trim());

    /// <summary>Whether <paramref name="text"/> is an ID as <see cref="NewId"/> makes them.</summary>
    public static bool IsId(string text) => IsHex(text, IdBytes * 2);

    /// <summary>Whether <paramref name="text"/> is a hash as this class makes them.</summary>
    public static bool IsHash(string text) => IsHex(text, HashBytes * 2);

    /// <summary>
    /// The ID in an ID file's text, or <see langword="null"/> when it holds none. Spaces and line
    /// breaks around it don't count, and case is ignored.
    /// </summary>
    public static string? ReadId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var id = text.Trim().ToLowerInvariant();
        return IsId(id) ? id : null;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(HashText + ":" + value)), 0, HashBytes);

    private static bool IsHex(string text, int length) =>
        text is not null && text.Length == length && text.All(character => char.IsAsciiHexDigitLower(character) || char.IsAsciiDigit(character));
}
