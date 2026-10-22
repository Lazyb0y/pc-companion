using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>
/// A profile's proxy settings, the same as on Windows' Proxy page. A part keeps its values while
/// it's off. See "Profiles" in docs/features/network/network-profiles.md.
/// </summary>
public sealed record ProxyProfile
{
    /// <summary>Automatically detect settings.</summary>
    public bool AutoDetect { get; set; }

    /// <summary>Use setup script.</summary>
    public bool UseScript { get; set; }

    /// <summary>The setup script's <c>http</c> or <c>https</c> address.</summary>
    public string ScriptAddress { get; set; } = string.Empty;

    /// <summary>Use a proxy server.</summary>
    public bool UseServer { get; set; }

    /// <summary>
    /// The proxy server, <c>address:port</c>, or a server for each protocol as Windows stores it,
    /// such as <c>http=proxy.example.com:8080;https=proxy.example.com:8443</c>.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Addresses that don't use the proxy server, with <c>*</c> as a wildcard.</summary>
    public IReadOnlyList<string> Exceptions { get; set; } = [];

    /// <summary>Don't use the proxy server for local (intranet) addresses.</summary>
    public bool BypassLocal { get; set; }

    // Compares the exceptions one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(ProxyProfile? other) =>
        other is not null && AutoDetect == other.AutoDetect && UseScript == other.UseScript && ScriptAddress == other.ScriptAddress
        && UseServer == other.UseServer && Server == other.Server && Exceptions.SequenceEqual(other.Exceptions) && BypassLocal == other.BypassLocal;

    public override int GetHashCode() => HashCode.Combine(AutoDetect, UseScript, ScriptAddress, UseServer, Server, Exceptions.Count, BypassLocal);
}

/// <summary>A named set of settings that the user switches to.</summary>
public sealed record NetworkProfile
{
    /// <summary>The profile's name, shown in the menu.</summary>
    public string Name { get; set; } = string.Empty;

    public ProxyProfile Proxy { get; set; } = new();
}

/// <summary>
/// Network Profiles settings, stored in <c>features.networkProfiles</c>. See docs/features/network/network-profiles.md.
/// </summary>
public sealed record NetworkProfilesSettings : IFeatureSettings<NetworkProfilesSettings>
{
    /// <summary>The most profiles the menu shows; later ones are left out.</summary>
    public const int MaximumProfiles = 20;

    public bool Enabled { get; set; }

    public IReadOnlyList<NetworkProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static NetworkProfilesSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, NetworkProfilesJsonContext.Default.NetworkProfilesSettings, () => new NetworkProfilesSettings());

    public JsonElement Write() => FeatureSettings.Write(this, NetworkProfilesJsonContext.Default.NetworkProfilesSettings);

    public NetworkProfilesSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the profiles one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(NetworkProfilesSettings? other) =>
        other is not null && Enabled == other.Enabled && Profiles.SequenceEqual(other.Profiles);

    public override int GetHashCode() => HashCode.Combine(Enabled, Profiles.Count);
}
