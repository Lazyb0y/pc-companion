using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>How PC Companion talks to a server.</summary>
public enum RemoteProtocol
{
    /// <summary>The SSH File Transfer Protocol.</summary>
    Sftp,
}

/// <summary>How PC Companion signs in to a server.</summary>
public enum RemoteSignIn
{
    /// <summary>With a password, kept in Credential Manager.</summary>
    Password,

    /// <summary>With an SSH key file, whose passphrase, if any, is kept in Credential Manager.</summary>
    Key,
}

/// <summary>The server key the user confirmed, as OpenSSH shows it.</summary>
public sealed record RemoteHostKey
{
    /// <summary>The key's algorithm as the server names it, such as <c>ssh-ed25519</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The SHA-256 fingerprint of the key, in base64 without padding, as OpenSSH shows it after <c>SHA256:</c>.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// A server in the list, whose folder File Explorer shows. See "Servers" in
/// docs/features/files/remote-folders.md.
/// </summary>
public sealed record RemoteServer
{
    /// <summary>The port SFTP uses unless the user gives another.</summary>
    public const int DefaultPort = 22;

    /// <summary>A random ID of 32 hexadecimal digits, which names the server's sync root and credential.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name File Explorer shows, up to <see cref="RemoteFoldersSettings.MaximumNameLength"/> characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Sftp</c>, kept as written, so a protocol this version doesn't know keeps it when the settings are saved, and isn't used.</summary>
    public string Protocol { get; set; } = nameof(RemoteProtocol.Sftp);

    /// <summary>The server's host name or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    public string UserName { get; set; } = string.Empty;

    /// <summary><c>Password</c> or <c>Key</c>, kept as written as <see cref="Protocol"/> is.</summary>
    public string SignIn { get; set; } = nameof(RemoteSignIn.Password);

    /// <summary>For <c>Key</c>: the full path of the SSH key file on the PC.</summary>
    public string KeyFile { get; set; } = string.Empty;

    /// <summary>The folder on the server that the folder on the PC shows, as the server names it, such as <c>/home/ana</c>.</summary>
    public string RemoteFolder { get; set; } = string.Empty;

    /// <summary>The folder on the PC, set when the server is added, which never changes.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The server key the user confirmed. A server without one isn't connected.</summary>
    public RemoteHostKey? HostKey { get; set; }

    [JsonIgnore]
    public RemoteProtocol? KnownProtocol => Known<RemoteProtocol>(Protocol);

    [JsonIgnore]
    public RemoteSignIn? KnownSignIn => Known<RemoteSignIn>(SignIn);

    /// <summary>
    /// Whether everything PC Companion needs to connect the server is there and known to this
    /// version. A server that isn't usable stays in the list, and its folder isn't connected.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable =>
        IsValidId(Id)
        && Name.Length is > 0 and <= RemoteFoldersSettings.MaximumNameLength
        && KnownProtocol is not null
        && KnownSignIn is { } signIn
        && Host.Length > 0
        && Port is > 0 and <= 65_535
        && UserName.Length > 0
        && (signIn != RemoteSignIn.Key || Path.IsPathFullyQualified(KeyFile))
        && RemoteFolder.Length > 0
        && Path.IsPathFullyQualified(Folder)
        && HostKey is { Type.Length: > 0, Sha256.Length: > 0 };

    /// <summary>Whether <paramref name="id"/> is 32 hexadecimal digits, as a new server's random ID is.</summary>
    public static bool IsValidId(string id) =>
        id.Length == 32 && id.All(char.IsAsciiHexDigit);

    /// <summary>A new random ID for a server.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    internal static T? Known<T>(string name)
        where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Remote Folders settings, stored in <c>features.remoteFolders</c>. See
/// docs/features/files/remote-folders.md.
/// </summary>
public sealed record RemoteFoldersSettings : IFeatureSettings<RemoteFoldersSettings>
{
    /// <summary>The most servers in the list; later ones are left out.</summary>
    public const int MaximumServers = 20;

    /// <summary>The longest name of a server, which is also its folder's name.</summary>
    public const int MaximumNameLength = 60;

    public bool Enabled { get; set; }

    public IReadOnlyList<RemoteServer> Servers { get; set; } = [];

    /// <summary>
    /// The servers whose folders are connected: those that are usable, without a second server with
    /// the same ID or folder, which a hand edit could make.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<RemoteServer> UsableServers
    {
        get
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return [.. Servers.Where(server => server.IsUsable && ids.Add(server.Id) && folders.Add(Path.TrimEndingDirectorySeparator(server.Folder)))];
        }
    }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in the list,
    /// such as after a hand edit, is left out, and so are servers after the first <see cref="MaximumServers"/>.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static RemoteFoldersSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, RemoteFoldersJsonContext.Default.RemoteFoldersSettings, () => new RemoteFoldersSettings());
        return settings with { Servers = [.. settings.Servers.Where(server => server is not null).Take(MaximumServers)] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, RemoteFoldersJsonContext.Default.RemoteFoldersSettings);

    public RemoteFoldersSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the servers one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(RemoteFoldersSettings? other) =>
        other is not null && Enabled == other.Enabled && Servers.SequenceEqual(other.Servers);

    public override int GetHashCode() => HashCode.Combine(Enabled, Servers.Count);
}
