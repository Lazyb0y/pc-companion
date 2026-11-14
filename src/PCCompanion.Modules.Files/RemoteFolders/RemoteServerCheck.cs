using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>What's wrong with a server's details in the server dialog.</summary>
public enum ServerProblem
{
    None,
    NoName,
    NameTooLong,

    /// <summary>Windows doesn't allow the name for a folder.</summary>
    InvalidName,

    /// <summary>Another server has that name, ignoring case.</summary>
    NameInUse,
    NoAddress,
    InvalidAddress,
    InvalidPort,
    NoUserName,
    UserNameTooLong,
    NoPassword,
    NoKeyFile,
    KeyFileNotFound,
    NoRemoteFolder,
}

/// <summary>
/// The checks of the server dialog, and where a new server's folder goes. See "Servers" in
/// docs/features/files/remote-folders.md.
/// </summary>
public static class RemoteServerCheck
{
    /// <summary>The folder in the user's profile that holds the servers' folders.</summary>
    public const string RootFolderName = "Remote Folders";

    /// <summary>The longest host name DNS allows.</summary>
    public const int MaximumAddressLength = 253;

    /// <summary>The longest user name, which servers keep far shorter.</summary>
    public const int MaximumUserNameLength = 256;

    /// <summary>The most numbers tried after a folder's name, as in <c>Home server (2)</c>.</summary>
    private const int MaximumNumber = 1_000;

    /// <summary>The folder that holds the servers' folders, in <paramref name="userProfile"/>.</summary>
    public static string RootFolder(string userProfile) => Path.Join(userProfile, RootFolderName);

    /// <summary>Whether <paramref name="name"/>, as trimmed, can name a server and its folder.</summary>
    public static ServerProblem CheckName(string name, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(otherNames);

        name = name.Trim();
        return name.Length == 0 ? ServerProblem.NoName
            : name.Length > RemoteFoldersSettings.MaximumNameLength ? ServerProblem.NameTooLong
            : FileNaming.CheckName(name) != NamingProblem.None ? ServerProblem.InvalidName
            : otherNames.Any(other => string.Equals(other.Trim(), name, StringComparison.OrdinalIgnoreCase)) ? ServerProblem.NameInUse
            : ServerProblem.None;
    }

    /// <summary>Whether <paramref name="address"/>, as trimmed, is a host name or an IP address.</summary>
    public static ServerProblem CheckAddress(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        address = address.Trim();
        return address.Length == 0 ? ServerProblem.NoAddress
            : address.Length > MaximumAddressLength || Uri.CheckHostName(address) == UriHostNameType.Unknown ? ServerProblem.InvalidAddress
            : ServerProblem.None;
    }

    /// <summary>The port in <paramref name="text"/>, from 1 to 65535 in ASCII digits, or <see langword="null"/>.</summary>
    public static int? ParsePort(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65_535 ? port : null;
    }

    public static ServerProblem CheckPort(string text) => ParsePort(text) is null ? ServerProblem.InvalidPort : ServerProblem.None;

    public static ServerProblem CheckUserName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);

        userName = userName.Trim();
        return userName.Length == 0 ? ServerProblem.NoUserName
            : userName.Length > MaximumUserNameLength ? ServerProblem.UserNameTooLong
            : ServerProblem.None;
    }

    /// <summary>Whether <paramref name="keyFile"/> is the full path of a file that's there.</summary>
    public static ServerProblem CheckKeyFile(string keyFile, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        ArgumentNullException.ThrowIfNull(fileExists);

        keyFile = keyFile.Trim();
        return keyFile.Length == 0 ? ServerProblem.NoKeyFile
            : !Path.IsPathFullyQualified(keyFile) || !fileExists(keyFile) ? ServerProblem.KeyFileNotFound
            : ServerProblem.None;
    }

    /// <summary>
    /// The folder for a new server named <paramref name="name"/> in <paramref name="root"/>: the
    /// name itself, or with a number added, as File Explorer adds, such as <c>Home server (2)</c>,
    /// when another server has that folder or something is there. <see langword="null"/> when no
    /// name is free.
    /// </summary>
    public static string? NewFolder(string root, string name, IEnumerable<string> otherFolders, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(otherFolders);
        ArgumentNullException.ThrowIfNull(exists);

        var taken = otherFolders.Select(Path.TrimEndingDirectorySeparator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int number = 1; number <= MaximumNumber; number++)
        {
            string candidate = number == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} ({number})");
            string folder = Path.Join(root, candidate);
            if (!taken.Contains(folder) && !exists(folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// The server's address as an SSH user writes it, <c>user@address:port</c>, with an IPv6
    /// address in brackets.
    /// </summary>
    public static string Address(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        string host = server.Host.Contains(':', StringComparison.Ordinal) ? "[" + server.Host + "]" : server.Host;
        return string.Create(CultureInfo.InvariantCulture, $"{server.UserName}@{host}:{server.Port}");
    }

    /// <summary>
    /// A server key as OpenSSH shows it, its type and SHA-256 fingerprint, such as
    /// <c>ED25519 SHA256:cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI</c>.
    /// </summary>
    public static string KeyText(RemoteHostKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string type = key.Type switch
        {
            "ssh-ed25519" => "ED25519",
            "ssh-ed448" => "ED448",
            "ssh-rsa" or "rsa-sha2-256" or "rsa-sha2-512" => "RSA",
            _ when key.Type.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) => "ECDSA",
            _ => key.Type,
        };
        return type + " SHA256:" + key.Sha256;
    }
}
