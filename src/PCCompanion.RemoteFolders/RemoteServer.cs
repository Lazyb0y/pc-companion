using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.RemoteFolders;

/// <summary>A file or folder on a server, as a listing or <c>stat</c> gives it, with links followed.</summary>
/// <param name="Name">Its name as the server gives it, unchecked: see "Names from the server" in the spec.</param>
public sealed record RemoteEntry(string Name, bool IsDirectory, long Size, DateTime LastWriteTimeUtc);

/// <summary>A folder's entries, with whether the server had more than were read.</summary>
public sealed record RemoteListing(IReadOnlyList<RemoteEntry> Entries, bool IsTruncated);

/// <summary>A server's key, as it presented it: the algorithm's name and the SHA-256 fingerprint, as OpenSSH shows them.</summary>
public sealed record ServerKey(string Type, string Sha256);

/// <summary>Why a server's request failed, as Remote Folders tells the user.</summary>
public enum RemoteError
{
    /// <summary>The server can't be reached: no answer, refused, or the connection dropped.</summary>
    Unreachable,

    /// <summary>The server's key isn't the one the user confirmed, or none was confirmed yet.</summary>
    KeyNotTrusted,

    /// <summary>The password, key or passphrase was refused, or the server asked for more than a password.</summary>
    SignInFailed,

    /// <summary>The SSH key file can't be read, needs a passphrase, or the passphrase is wrong.</summary>
    KeyFile,

    /// <summary>The server offers none of the algorithms PC Companion allows.</summary>
    NoSafeAlgorithm,

    /// <summary>The file or folder isn't there.</summary>
    NotFound,

    /// <summary>The file on the server isn't the version expected: it changed since the PC's copy was made.</summary>
    Changed,

    /// <summary>The server refused the user access.</summary>
    PermissionDenied,

    /// <summary>The server failed the request for another reason.</summary>
    Failed,
}

/// <summary>A server's request failed; <see cref="Error"/> says why.</summary>
public sealed class RemoteServerException : Exception
{
    public RemoteServerException(RemoteError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public RemoteServerException()
        : this(RemoteError.Failed, "The server's request failed.")
    {
    }

    public RemoteServerException(string message)
        : this(RemoteError.Failed, message)
    {
    }

    public RemoteServerException(string message, Exception innerException)
        : this(RemoteError.Failed, message, innerException)
    {
    }

    public RemoteError Error { get; }

    /// <summary>For <see cref="RemoteError.KeyNotTrusted"/>: the key the server presented.</summary>
    public ServerKey? PresentedKey { get; init; }
}

/// <summary>
/// A connection to a server, as Remote Folders uses it, whatever the protocol. Paths are the
/// server's own, with <c>/</c> between folders. Every method fails with
/// <see cref="RemoteServerException"/>. A connection serves one request at a time; Remote Folders
/// opens a few per server for requests at once.
/// </summary>
public interface IRemoteServer : IDisposable
{
    /// <summary>The folder the server starts in, normally the user's home folder.</summary>
    string HomeFolder { get; }

    /// <summary>Whether the connection is still open.</summary>
    bool IsConnected { get; }

    /// <summary>Whether <see cref="ReplaceAsync"/> replaces a file in one step.</summary>
    bool CanReplaceAtOnce { get; }

    /// <summary>The file or folder at <paramref name="path"/>, with a link followed, or <see langword="null"/> when it isn't there.</summary>
    Task<RemoteEntry?> StatAsync(string path, CancellationToken cancellation);

    /// <summary>
    /// A folder's entries, without <c>.</c> and <c>..</c>, with links followed and links that can't
    /// be followed left out, up to <paramref name="limit"/>.
    /// </summary>
    Task<RemoteListing> ListAsync(string folder, int limit, CancellationToken cancellation);

    /// <summary>A stream that reads the file from any offset.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellation);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, replacing a file there.</summary>
    Task UploadAsync(Stream content, string path, CancellationToken cancellation);

    /// <summary>Renames or moves a file or folder; fails when <paramref name="destination"/> is taken.</summary>
    Task RenameAsync(string source, string destination, CancellationToken cancellation);

    /// <summary>
    /// Puts the file <paramref name="source"/> in the place of <paramref name="destination"/>, in one step when
    /// <see cref="CanReplaceAtOnce"/>, otherwise by renaming <paramref name="destination"/> out of the way
    /// first, so a failure leaves one whole version there.
    /// </summary>
    Task ReplaceAsync(string source, string destination, CancellationToken cancellation);

    Task DeleteFileAsync(string path, CancellationToken cancellation);

    /// <summary>Deletes an empty folder.</summary>
    Task DeleteFolderAsync(string path, CancellationToken cancellation);

    Task CreateFolderAsync(string path, CancellationToken cancellation);
}
