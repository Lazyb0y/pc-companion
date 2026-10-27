using System;
using System.Globalization;
using PCCompanion.Core;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The Mark of the Web that Windows and browsers give downloaded files, in the file's
/// <c>Zone.Identifier</c> stream, such as <c>[ZoneTransfer]</c>, <c>ZoneId=3</c> and
/// <c>HostUrl=https://example.com/file.pdf</c>. See "Mark of the Web" in docs/features/files/folder-rules.md.
/// </summary>
/// <param name="ZoneId">The zone, such as 3 for the internet.</param>
/// <param name="HostUrl">The address the file was downloaded from.</param>
/// <param name="ReferrerUrl">The page it was downloaded on.</param>
public sealed record MarkOfTheWeb(int? ZoneId, Uri? HostUrl, Uri? ReferrerUrl)
{
    /// <summary>The name of the stream that holds the mark.</summary>
    public const string StreamName = "Zone.Identifier";

    private const string Section = "[ZoneTransfer]";

    /// <summary>
    /// The web address the file came from: <see cref="HostUrl"/>, or else <see cref="ReferrerUrl"/>,
    /// or <see langword="null"/> when neither is an <c>http</c> or <c>https</c> address, such as
    /// for a download from a private window, for which Edge writes <c>about:internet</c>.
    /// </summary>
    public Uri? Source => HostProcess.IsWebAddress(HostUrl) ? HostUrl : HostProcess.IsWebAddress(ReferrerUrl) ? ReferrerUrl : null;

    /// <summary>
    /// Reads a mark as Windows does: the values in its <c>[ZoneTransfer]</c> section, the first of
    /// each, ignoring case in names. Lines it can't read are left out.
    /// </summary>
    public static MarkOfTheWeb Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int? zoneId = null;
        Uri? hostUrl = null;
        Uri? referrerUrl = null;
        var inSection = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inSection = string.Equals(line, Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inSection || equals <= 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (string.Equals(name, "ZoneId", StringComparison.OrdinalIgnoreCase))
            {
                zoneId ??= int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var zone) ? zone : null;
            }
            else if (string.Equals(name, "HostUrl", StringComparison.OrdinalIgnoreCase))
            {
                hostUrl ??= Address(value);
            }
            else if (string.Equals(name, "ReferrerUrl", StringComparison.OrdinalIgnoreCase))
            {
                referrerUrl ??= Address(value);
            }
        }

        return new MarkOfTheWeb(zoneId, hostUrl, referrerUrl);
    }

    private static Uri? Address(string value) => Uri.TryCreate(value, UriKind.Absolute, out var address) ? address : null;
}
