using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using Renci.SshNet.Sftp;

namespace PCCompanion.RemoteFolders;

/// <summary>How to sign in to an SFTP server: with a password, or with a key file and its passphrase, if any.</summary>
public sealed record SftpSignIn(string Host, int Port, string UserName, string? Password, string? KeyFile, string? Passphrase)
{
    // Keeps secrets out of logs and exception messages that print the record.
    public override string ToString() => $"SFTP sign-in for port {Port}";
}

/// <summary>
/// An SFTP server, through SSH.NET. It checks the server's key before signing in, so a server that
/// pretends to be the user's gets nothing, and allows only the algorithms in <see cref="SshAlgorithms"/>.
/// </summary>
public sealed class SftpServer : IRemoteServer
{
    /// <summary>How long connecting may take, until the server answers.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a request may wait for the server's answer.</summary>
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often a keepalive goes while connected, so a dropped connection is noticed.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly SftpClient _client;
    private int _replacesAtOnce = 1;

    private SftpServer(SftpClient client)
    {
        _client = client;
        HomeFolder = client.WorkingDirectory;
    }

    public string HomeFolder { get; }

    public bool IsConnected => _client.IsConnected;

    public bool CanReplaceAtOnce => Volatile.Read(ref _replacesAtOnce) != 0;

    /// <summary>
    /// Connects only to read the server's key, and sends nothing else: no user name, password or
    /// key. The user confirms the key before <see cref="ConnectAsync"/> signs in.
    /// </summary>
    public static async Task<ServerKey> ReadKeyAsync(string host, int port, CancellationToken cancellation)
    {
        var connection = new ConnectionInfo(host, port, "-", new NoneAuthenticationMethod("-")) { Timeout = ConnectTimeout };
        SshAlgorithms.Apply(connection, trustedKeyType: null);
        using var client = new SftpClient(connection);
        ServerKey? presented = null;
        client.HostKeyReceived += (_, e) =>
        {
            presented = new ServerKey(e.HostKeyName, e.FingerPrintSHA256);
            e.CanTrust = false;
        };

        try
        {
            await client.ConnectAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (presented is not null && exception is not OperationCanceledException)
        {
            return presented;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }

        // Not reached: the key is never trusted, so connecting never succeeds.
        throw new RemoteServerException(RemoteError.Failed, "The server presented no key.");
    }

    /// <summary>Connects and signs in, after checking that the server presents <paramref name="trustedKey"/>.</summary>
    public static async Task<SftpServer> ConnectAsync(SftpSignIn signIn, ServerKey trustedKey, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(trustedKey);
        var methods = SignInMethods(signIn);
        var connection = new ConnectionInfo(signIn.Host, signIn.Port, signIn.UserName, [.. methods]) { Timeout = ConnectTimeout };
        SshAlgorithms.Apply(connection, trustedKey.Type);
        var client = new SftpClient(connection) { OperationTimeout = OperationTimeout, KeepAliveInterval = KeepAliveInterval };
        ServerKey? presented = null;
        client.HostKeyReceived += (_, e) =>
        {
            presented = new ServerKey(e.HostKeyName, e.FingerPrintSHA256);
            e.CanTrust = string.Equals(presented.Sha256, trustedKey.Sha256, StringComparison.Ordinal)
                && SshAlgorithms.SameKind(trustedKey.Type).Contains(presented.Type, StringComparer.Ordinal);
        };

        try
        {
            await client.ConnectAsync(cancellation).ConfigureAwait(false);
            return new SftpServer(client);
        }
        catch (Exception exception)
        {
            client.Dispose();
            foreach (var method in methods.OfType<IDisposable>())
            {
                method.Dispose();
            }

            if (exception is OperationCanceledException)
            {
                throw;
            }

            if (presented is not null && (presented.Sha256 != trustedKey.Sha256 || !SshAlgorithms.SameKind(trustedKey.Type).Contains(presented.Type, StringComparer.Ordinal)))
            {
                throw new RemoteServerException(RemoteError.KeyNotTrusted, "The server's key isn't the one the user confirmed.", exception) { PresentedKey = presented };
            }

            throw Map(exception);
        }
    }

    public async Task<RemoteEntry?> StatAsync(string path, CancellationToken cancellation)
    {
        try
        {
            var file = await _client.GetAsync(path, cancellation).ConfigureAwait(false);
            return await FollowAsync(file, cancellation).ConfigureAwait(false);
        }
        catch (SftpPathNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task<RemoteListing> ListAsync(string folder, int limit, CancellationToken cancellation)
    {
        try
        {
            var entries = new List<RemoteEntry>();
            await foreach (var file in _client.ListDirectoryAsync(folder, cancellation).ConfigureAwait(false))
            {
                if (file.Name is "." or "..")
                {
                    continue;
                }

                if (entries.Count == limit)
                {
                    return new RemoteListing(entries, IsTruncated: true);
                }

                if (await FollowAsync(file, cancellation).ConfigureAwait(false) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            return new RemoteListing(entries, IsTruncated: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return await _client.OpenAsync(path, FileMode.Open, FileAccess.Read, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task UploadAsync(Stream content, string path, CancellationToken cancellation)
    {
        try
        {
            await _client.UploadFileAsync(content, path, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task RenameAsync(string source, string destination, CancellationToken cancellation)
    {
        try
        {
            await _client.RenameFileAsync(source, destination, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task ReplaceAsync(string source, string destination, CancellationToken cancellation)
    {
        if (CanReplaceAtOnce)
        {
            try
            {
                // SSH.NET offers posix-rename only without async.
                await Task.Run(() => _client.RenameFile(source, destination, isPosix: true), cancellation).ConfigureAwait(false);
                return;
            }
            catch (NotSupportedException)
            {
                Volatile.Write(ref _replacesAtOnce, 0);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Map(exception);
            }
        }

        // Without posix-rename, a plain rename can't replace a file: the original steps aside first.
        string aside = destination + ".pcc-old-" + Guid.NewGuid().ToString("N")[..8];
        bool movedAside = false;
        try
        {
            if (await StatAsync(destination, cancellation).ConfigureAwait(false) is not null)
            {
                await _client.RenameFileAsync(destination, aside, cancellation).ConfigureAwait(false);
                movedAside = true;
            }

            await _client.RenameFileAsync(source, destination, cancellation).ConfigureAwait(false);
            if (movedAside)
            {
                await _client.DeleteFileAsync(aside, cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not RemoteServerException)
        {
            throw Map(exception);
        }
    }

    public async Task DeleteFileAsync(string path, CancellationToken cancellation)
    {
        try
        {
            await _client.DeleteFileAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task DeleteFolderAsync(string path, CancellationToken cancellation)
    {
        try
        {
            await _client.DeleteDirectoryAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public async Task CreateFolderAsync(string path, CancellationToken cancellation)
    {
        try
        {
            await _client.CreateDirectoryAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Map(exception);
        }
    }

    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Why a request failed, from SSH.NET's exception: the server can't be reached, the sign-in was
    /// refused, the file isn't there, access was denied, or something else.
    /// </summary>
    public static RemoteServerException Map(Exception exception) => exception switch
    {
        RemoteServerException remote => remote,
        SshAuthenticationException or SshPassPhraseNullOrEmptyException when exception is SshPassPhraseNullOrEmptyException
            => new RemoteServerException(RemoteError.KeyFile, "The SSH key needs its passphrase.", exception),
        SshAuthenticationException => new RemoteServerException(RemoteError.SignInFailed, "The server refused the sign-in.", exception),
        SshConnectionException { DisconnectReason: DisconnectReason.KeyExchangeFailed } => new RemoteServerException(RemoteError.NoSafeAlgorithm, "The server offers none of the allowed algorithms.", exception),
        SshConnectionException when exception.Message.Contains("No matching", StringComparison.OrdinalIgnoreCase) => new RemoteServerException(RemoteError.NoSafeAlgorithm, "The server offers none of the allowed algorithms.", exception),
        SocketException or SshConnectionException or SshOperationTimeoutException or ProxyException or ObjectDisposedException
            => new RemoteServerException(RemoteError.Unreachable, "The server can't be reached.", exception),
        SftpPathNotFoundException => new RemoteServerException(RemoteError.NotFound, "The file or folder isn't on the server.", exception),
        SftpPermissionDeniedException => new RemoteServerException(RemoteError.PermissionDenied, "The server refused access.", exception),
        IOException when exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException
            => new RemoteServerException(RemoteError.KeyFile, "The SSH key file can't be read.", exception),
        _ => new RemoteServerException(RemoteError.Failed, "The server's request failed.", exception),
    };

    // A password goes by both methods servers use for it; a key goes alone.
    private static List<AuthenticationMethod> SignInMethods(SftpSignIn signIn)
    {
        if (signIn.KeyFile is { Length: > 0 } keyFile)
        {
            PrivateKeyFile key;
            try
            {
                key = string.IsNullOrEmpty(signIn.Passphrase) ? new PrivateKeyFile(keyFile) : new PrivateKeyFile(keyFile, signIn.Passphrase);
            }
            catch (SshPassPhraseNullOrEmptyException exception)
            {
                throw new RemoteServerException(RemoteError.KeyFile, "The SSH key needs its passphrase.", exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new RemoteServerException(RemoteError.KeyFile, "The SSH key file can't be read.", exception);
            }
            catch (SshException exception)
            {
                throw new RemoteServerException(RemoteError.KeyFile, "The SSH key's passphrase is wrong, or the file isn't a key.", exception);
            }

            return [new PrivateKeyAuthenticationMethod(signIn.UserName, key)];
        }

        string password = signIn.Password ?? string.Empty;
        var interactive = new KeyboardInteractiveAuthenticationMethod(signIn.UserName);
        interactive.AuthenticationPrompt += (_, e) =>
        {
            var prompts = e.Prompts.ToList();
            foreach (var prompt in prompts)
            {
                prompt.Response = PasswordPrompt.IsOnlyThePassword(prompts.Count, prompt.IsEchoed) ? password : string.Empty;
            }
        };
        return [new PasswordAuthenticationMethod(signIn.UserName, password), interactive];
    }

    // A link is followed: listing it shows it's a folder, and otherwise opening it reads its
    // target's size. Listing comes first, since some servers, such as OpenSSH for Windows, also
    // open a folder as if it were an empty file. The time is the link's own, as SSH.NET doesn't
    // follow links when it reads attributes. A link that can't be followed is left out.
    private async Task<RemoteEntry?> FollowAsync(ISftpFile file, CancellationToken cancellation)
    {
        if (!file.IsSymbolicLink)
        {
            return file.IsDirectory || file.IsRegularFile
                ? new RemoteEntry(file.Name, file.IsDirectory, file.IsDirectory ? 0 : file.Length, file.LastWriteTimeUtc)
                : null;
        }

        try
        {
            await foreach (var _ in _client.ListDirectoryAsync(file.FullName, cancellation).ConfigureAwait(false))
            {
                break;
            }

            return new RemoteEntry(file.Name, IsDirectory: true, 0, file.LastWriteTimeUtc) { IsLink = true };
        }
        catch (Exception exception) when (exception is SshException or SftpPathNotFoundException or SftpPermissionDeniedException)
        {
        }

        try
        {
            await using var target = await _client.OpenAsync(file.FullName, FileMode.Open, FileAccess.Read, cancellation).ConfigureAwait(false);
            return new RemoteEntry(file.Name, IsDirectory: false, target.Length, file.LastWriteTimeUtc) { IsLink = true };
        }
        catch (Exception exception) when (exception is SshException or SftpPathNotFoundException or SftpPermissionDeniedException)
        {
            return null;
        }
    }
}

/// <summary>Which keyboard-interactive questions a password answers.</summary>
public static class PasswordPrompt
{
    /// <summary>
    /// Whether to answer with the password: only when the server asks one question, hidden as a
    /// password is. More questions, such as a one-time code, aren't supported.
    /// </summary>
    public static bool IsOnlyThePassword(int questions, bool isEchoed) => questions == 1 && !isEchoed;
}
