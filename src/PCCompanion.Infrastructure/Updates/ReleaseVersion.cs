using System;
using System.Globalization;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// A version of PC Companion: MAJOR.MINOR.PATCH, the three parts Windows Installer compares.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    // Up to 9 digits, so a part always fits an int.
    private const int MaximumDigits = 9;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// The version of a running build, from the first three parts of its assembly version.
    /// </summary>
    public static ReleaseVersion FromAssemblyVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new ReleaseVersion(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>
    /// Reads a release's tag, such as <c>v1.2.0</c>: <c>v</c> and the version, with nothing
    /// before or after them.
    /// </summary>
    public static bool TryParseTag(string? tag, out ReleaseVersion version)
    {
        version = default;
        return tag is ['v', .. var rest] && TryParse(rest, out version);
    }

    /// <summary>
    /// Reads a version written as <see cref="ToString"/> writes it, such as <c>1.2.0</c>: three
    /// parts of ASCII digits, without leading zeros, and nothing else.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out ReleaseVersion version)
    {
        version = default;
        Span<int> parts = stackalloc int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            var end = index < parts.Length - 1 ? text.IndexOf('.') : text.Length;
            if (end < 0 || !TryParsePart(text[..end], out parts[index]))
            {
                return false;
            }

            text = index < parts.Length - 1 ? text[(end + 1)..] : [];
        }

        version = new ReleaseVersion(parts[0], parts[1], parts[2]);
        return true;
    }

    public int CompareTo(ReleaseVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    // Only ASCII digits: char.IsDigit and int.Parse also take the digits of other scripts.
    private static bool TryParsePart(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.IsEmpty || text.Length > MaximumDigits || (text[0] == '0' && text.Length > 1))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }

            value = (value * 10) + (character - '0');
        }

        return true;
    }
}
