using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>What keeps a profile out of the menu, or a value out of the profile editor.</summary>
public enum ProfileProblem
{
    None,
    NoName,
    NameTooLong,

    /// <summary>The name holds control characters, such as a line break.</summary>
    InvalidName,

    /// <summary>Another profile has the same name, ignoring case.</summary>
    NameInUse,

    NoServer,

    /// <summary>The proxy server isn't <c>address:port</c> or a server for each protocol.</summary>
    InvalidServer,

    NoScriptAddress,

    /// <summary>The setup script's address isn't an <c>http</c> or <c>https</c> address.</summary>
    ScriptNotWebAddress,

    /// <summary>An exception is empty or holds spaces, semicolons or control characters.</summary>
    InvalidException,

    /// <summary>A text is longer than <see cref="NetworkProfileCheck.MaximumTextLength"/>.</summary>
    TooLong,
}

/// <summary>
/// Checks a profile's values, for the menu and the profile editor. Only the parts that are on are
/// checked, since the others aren't written. See "Profiles" in docs/features/network/network-profiles.md.
/// </summary>
public static class NetworkProfileCheck
{
    /// <summary>The longest name, which the menu shows.</summary>
    public const int MaximumNameLength = 60;

    /// <summary>The longest proxy server and script address, and the longest the exceptions can be together.</summary>
    public const int MaximumTextLength = 2048;

    // The protocols a server for each protocol can be for, as Windows stores them.
    private static readonly string[] Protocols = ["http", "https", "ftp", "socks"];

    // What Windows Settings and some tools write before a server.
    private static readonly string[] ServerPrefixes = ["http://", "https://"];

    /// <summary>
    /// The first problem of a profile, or <see cref="ProfileProblem.None"/>. A name that one of
    /// <paramref name="otherNames"/> has, ignoring case, is in use.
    /// </summary>
    public static ProfileProblem Check(NetworkProfile profile, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var proxy = profile.Proxy;
        var problems = new[]
        {
            CheckName(profile.Name, otherNames),
            proxy.UseServer ? CheckServer(proxy.Server) : ProfileProblem.None,
            proxy.UseServer ? CheckExceptions(proxy.Exceptions) : ProfileProblem.None,
            proxy.UseScript ? CheckScriptAddress(proxy.ScriptAddress) : ProfileProblem.None,
        };
        return problems.FirstOrDefault(problem => problem != ProfileProblem.None);
    }

    public static ProfileProblem CheckName(string name, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(otherNames);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return ProfileProblem.NoName;
        }

        if (trimmed.Length > MaximumNameLength)
        {
            return ProfileProblem.NameTooLong;
        }

        if (HasControlCharacters(trimmed))
        {
            return ProfileProblem.InvalidName;
        }

        return otherNames.Any(other => string.Equals(other.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ? ProfileProblem.NameInUse
            : ProfileProblem.None;
    }

    /// <summary>
    /// Checks a proxy server: <c>address:port</c>, optionally after <c>http://</c>, or servers for
    /// each protocol, such as <c>http=proxy.example.com:8080;https=proxy.example.com:8443</c>.
    /// The address is a name, an IPv4 address, or an IPv6 address in brackets; the port is optional.
    /// </summary>
    public static ProfileProblem CheckServer(string server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var trimmed = server.Trim();
        if (trimmed.Length == 0)
        {
            return ProfileProblem.NoServer;
        }

        if (trimmed.Length > MaximumTextLength)
        {
            return ProfileProblem.TooLong;
        }

        var entries = trimmed.TrimEnd(';').Split(';');
        if (entries.Length == 1 && !entries[0].Contains('=', StringComparison.Ordinal))
        {
            return IsAddress(entries[0].Trim()) ? ProfileProblem.None : ProfileProblem.InvalidServer;
        }

        foreach (var entry in entries)
        {
            var parts = entry.Split('=', 2);
            if (parts.Length != 2 || !Protocols.Contains(parts[0].Trim(), StringComparer.OrdinalIgnoreCase) || !IsAddress(parts[1].Trim()))
            {
                return ProfileProblem.InvalidServer;
            }
        }

        return ProfileProblem.None;
    }

    public static ProfileProblem CheckScriptAddress(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var trimmed = address.Trim();
        if (trimmed.Length == 0)
        {
            return ProfileProblem.NoScriptAddress;
        }

        if (trimmed.Length > MaximumTextLength)
        {
            return ProfileProblem.TooLong;
        }

        return !HasControlCharacters(trimmed) && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? ProfileProblem.None
            : ProfileProblem.ScriptNotWebAddress;
    }

    /// <summary>
    /// Checks the exceptions: each is a name, address or pattern without spaces or semicolons,
    /// such as <c>*.example.com</c> or <c>10.*</c>.
    /// </summary>
    public static ProfileProblem CheckExceptions(IReadOnlyList<string> exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);

        if (exceptions.Any(exception => exception.Trim() is var trimmed && (trimmed.Length == 0 || trimmed.Any(character => character == ';' || char.IsWhiteSpace(character) || char.IsControl(character)))))
        {
            return ProfileProblem.InvalidException;
        }

        return exceptions.Sum(exception => exception.Trim().Length + 1) > MaximumTextLength ? ProfileProblem.TooLong : ProfileProblem.None;
    }

    // A name, an IPv4 address or an IPv6 address in brackets, optionally after http:// or https://,
    // with an optional port.
    private static bool IsAddress(string text)
    {
        var prefix = ServerPrefixes.FirstOrDefault(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (prefix is not null)
        {
            text = text[prefix.Length..];
        }

        string host;
        string? port = null;
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']', StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            host = text[1..end];
            var rest = text[(end + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                {
                    return false;
                }

                port = rest[1..];
            }

            if (Uri.CheckHostName(host) != UriHostNameType.IPv6)
            {
                return false;
            }
        }
        else
        {
            var colon = text.LastIndexOf(':');
            host = colon < 0 ? text : text[..colon];
            port = colon < 0 ? null : text[(colon + 1)..];
            if (Uri.CheckHostName(host) is not (UriHostNameType.Dns or UriHostNameType.IPv4))
            {
                return false;
            }
        }

        return port is null || (port.Length is > 0 and <= 5 && port.All(char.IsAsciiDigit) && int.Parse(port, System.Globalization.CultureInfo.InvariantCulture) is > 0 and <= 65535);
    }

    private static bool HasControlCharacters(string text) => text.Any(char.IsControl);
}
