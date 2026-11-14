using System;
using System.Collections.Generic;
using System.Linq;
using Renci.SshNet;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// The SSH algorithms Remote Folders allows, most preferred first: current ones only, with
/// authenticated encryption before AES-CTR. SHA-1 signatures and MACs, DSA keys, CBC and 3DES
/// ciphers, and compression are refused. See "Server keys" in docs/features/files/remote-folders.md.
/// </summary>
public static class SshAlgorithms
{
    public static IReadOnlyList<string> KeyExchange { get; } =
    [
        "mlkem768x25519-sha256",
        "sntrup761x25519-sha512",
        "sntrup761x25519-sha512@openssh.com",
        "curve25519-sha256",
        "curve25519-sha256@libssh.org",
        "ecdh-sha2-nistp521",
        "ecdh-sha2-nistp384",
        "ecdh-sha2-nistp256",
        "diffie-hellman-group-exchange-sha256",
        "diffie-hellman-group16-sha512",
        "diffie-hellman-group14-sha256",
    ];

    public static IReadOnlyList<string> HostKeys { get; } =
    [
        "ssh-ed25519",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "rsa-sha2-512",
        "rsa-sha2-256",
    ];

    public static IReadOnlyList<string> Ciphers { get; } =
    [
        "chacha20-poly1305@openssh.com",
        "aes256-gcm@openssh.com",
        "aes128-gcm@openssh.com",
        "aes256-ctr",
        "aes192-ctr",
        "aes128-ctr",
    ];

    public static IReadOnlyList<string> Macs { get; } =
    [
        "hmac-sha2-256-etm@openssh.com",
        "hmac-sha2-512-etm@openssh.com",
        "hmac-sha2-256",
        "hmac-sha2-512",
    ];

    /// <summary>
    /// Leaves only the allowed algorithms in <paramref name="connection"/>, in the preferred order.
    /// With <paramref name="trustedKeyType"/>, only that kind of server key is asked for, so an
    /// update that prefers another kind never brings up a key the user didn't confirm.
    /// </summary>
    public static void Apply(ConnectionInfo connection, string? trustedKeyType)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Keep(connection.KeyExchangeAlgorithms, KeyExchange);
        Keep(connection.HostKeyAlgorithms, trustedKeyType is null ? HostKeys : SameKind(trustedKeyType));
        Keep(connection.Encryptions, Ciphers);
        Keep(connection.HmacAlgorithms, Macs);
        Keep(connection.CompressionAlgorithms, ["none"]);
    }

    /// <summary>The algorithms that use the same key as <paramref name="keyType"/>: both RSA ones for an RSA key.</summary>
    public static IReadOnlyList<string> SameKind(string keyType) =>
        keyType.StartsWith("rsa-sha2-", StringComparison.Ordinal) || keyType == "ssh-rsa"
            ? ["rsa-sha2-512", "rsa-sha2-256"]
            : HostKeys.Contains(keyType, StringComparer.Ordinal) ? [keyType] : [];

    private static void Keep<T>(IDictionary<string, T> table, IReadOnlyList<string> allowed)
    {
        var kept = allowed.Where(table.ContainsKey).Select(name => (name, table[name])).ToList();
        table.Clear();
        foreach (var (name, value) in kept)
        {
            table.Add(name, value);
        }
    }
}
