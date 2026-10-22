using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>
/// Turns a profile's proxy settings into Windows' and back, and compares them. See "Profiles" in
/// docs/features/network/network-profiles.md.
/// </summary>
public static class ProxyMatching
{
    /// <summary>How Windows stores "Don't use the proxy server for local (intranet) addresses" in the exceptions.</summary>
    public const string LocalAddresses = "<local>";

    // What Windows Settings writes before a server when it saves one.
    private const string HttpPrefix = "http://";

    /// <summary>
    /// The profile's settings for Windows. The values of the parts that are off aren't written; see
    /// <see cref="InternetProxy.Write"/>.
    /// </summary>
    public static ProxySettings ToWindows(ProxyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var exceptions = Exceptions(profile.Exceptions, out var local);
        if (local || profile.BypassLocal)
        {
            exceptions.Add(LocalAddresses);
        }

        return new ProxySettings(profile.AutoDetect, profile.UseScript, profile.ScriptAddress.Trim(), profile.UseServer, profile.Server.Trim(), string.Join(';', exceptions));
    }

    /// <summary>A profile with Windows' current settings, as Add from current settings makes.</summary>
    public static ProxyProfile FromWindows(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var exceptions = Exceptions(settings.Bypass.Split(';'), out var local);
        return new ProxyProfile
        {
            AutoDetect = settings.AutoDetect,
            UseScript = settings.UseScript,
            ScriptAddress = settings.ScriptAddress.Trim(),
            UseServer = settings.UseServer,
            Server = settings.Server.Trim(),
            Exceptions = exceptions,
            BypassLocal = local,
        };
    }

    /// <summary>
    /// Whether Windows uses the profile's settings: the same parts are on, and their values are the
    /// same, ignoring case, spaces at the ends, <c>http://</c> before a server and the order of the
    /// exceptions. The values of the parts that are off don't count.
    /// </summary>
    public static bool Matches(ProxyProfile profile, ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        if (profile.AutoDetect != settings.AutoDetect || profile.UseScript != settings.UseScript || profile.UseServer != settings.UseServer)
        {
            return false;
        }

        if (profile.UseScript && !string.Equals(profile.ScriptAddress.Trim(), settings.ScriptAddress.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!profile.UseServer)
        {
            return true;
        }

        var profileExceptions = Exceptions(profile.Exceptions, out var profileLocal);
        var windowsExceptions = Exceptions(settings.Bypass.Split(';'), out var windowsLocal);
        return string.Equals(NormalizeServer(profile.Server), NormalizeServer(settings.Server), StringComparison.OrdinalIgnoreCase)
            && (profileLocal || profile.BypassLocal) == windowsLocal
            && profileExceptions.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(windowsExceptions);
    }

    // The exceptions, trimmed and without empty ones, and whether <local> was among them.
    private static List<string> Exceptions(IEnumerable<string> exceptions, out bool local)
    {
        var all = exceptions.Select(exception => exception.Trim()).Where(exception => exception.Length > 0).ToList();
        local = all.RemoveAll(exception => string.Equals(exception, LocalAddresses, StringComparison.OrdinalIgnoreCase)) > 0;
        return all;
    }

    // Each server without spaces or http:// before it, so a server saved by Windows Settings matches.
    private static string NormalizeServer(string server) =>
        string.Join(';', server.Trim().TrimEnd(';').Split(';').Select(entry =>
        {
            var parts = entry.Split('=', 2);
            return parts.Length == 2 ? parts[0].Trim() + "=" + WithoutPrefix(parts[1].Trim()) : WithoutPrefix(entry.Trim());
        }));

    private static string WithoutPrefix(string server) =>
        server.StartsWith(HttpPrefix, StringComparison.OrdinalIgnoreCase) ? server[HttpPrefix.Length..] : server;
}
