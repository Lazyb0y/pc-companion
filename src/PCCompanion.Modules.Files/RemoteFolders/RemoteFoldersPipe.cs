using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>What the settings app and the host ask PCCompanion.RemoteFolders.exe, through its pipe.</summary>
public enum RemoteFoldersRequestKind
{
    /// <summary>Every server's state.</summary>
    Status,

    /// <summary>
    /// Tests a server's details from the dialog: without <see cref="RemoteFoldersRequest.Key"/>, reads
    /// the server's key, sending nothing else; with it, signs in, trusting only that key, and checks the folder.
    /// </summary>
    TestServer,

    /// <summary>Lists again the folders listed in the last hour and the kept folders, of one server or all.</summary>
    SyncNow,

    /// <summary>Tries a server again after the user signed in again, with a new password in Credential Manager.</summary>
    SignInAgain,

    /// <summary>Whether the server presents <see cref="RemoteFoldersRequest.Key"/> now, before the settings app trusts it.</summary>
    CheckKey,

    /// <summary>The user opened the server's folder, so its conflicts are no longer listed.</summary>
    ConflictsSeen,
}

/// <summary>A server's state, as the page and the PC Companion icon's menu show it.</summary>
public enum RemoteServerState
{
    NotConnected,
    Connected,
    Unreachable,
    SignInFailed,
    KeyChanged,

    /// <summary>The server offers none of the algorithms PC Companion allows.</summary>
    NoSafeAlgorithm,
}

/// <summary>Why something failed on a server, as the page names it.</summary>
public enum RemoteFailure
{
    Failed,
    Unreachable,
    KeyChanged,
    SignInFailed,

    /// <summary>The SSH key file can't be read, or needs another passphrase.</summary>
    KeyFile,
    NoSafeAlgorithm,
    NotFound,
    PermissionDenied,
}

/// <summary>What testing a server's details found.</summary>
public enum RemoteTestOutcome
{
    /// <summary>The server's key, for the user to check before anything else is sent.</summary>
    KeyToConfirm,

    /// <summary>Signed in, and the folder is there.</summary>
    Connected,

    /// <summary>The folder on the server isn't there, or isn't a folder.</summary>
    FolderNotFound,

    /// <summary>It failed; see <see cref="RemoteTestResult.Failure"/>.</summary>
    Failed,
}

/// <summary>A request to PCCompanion.RemoteFolders.exe.</summary>
public sealed record RemoteFoldersRequest
{
    public RemoteFoldersRequestKind Kind { get; set; }

    /// <summary>The server, by its ID; for Sync now, none means every server.</summary>
    public string? ServerId { get; set; }

    /// <summary>For a test: the details from the dialog, not saved yet.</summary>
    public RemoteServer? Server { get; set; }

    /// <summary>For a test: the password, or the key's passphrase, typed in the dialog. Never logged or kept.</summary>
    public string? Secret { get; set; }

    /// <summary>For a test, the key the user confirmed; for <see cref="RemoteFoldersRequestKind.CheckKey"/>, the key the page shows.</summary>
    public RemoteHostKey? Key { get; set; }
}

/// <summary>A change from the journal the server refused, so it was skipped.</summary>
public sealed record RemoteSkippedChange
{
    /// <summary>Whether it was a rename or move; otherwise a delete.</summary>
    public bool IsRename { get; set; }

    /// <summary>Its path below the server's folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>For a rename, the new path.</summary>
    public string? Target { get; set; }

    public RemoteFailure Failure { get; set; }
}

/// <summary>A file the server refused to take, with its path below the server's folder.</summary>
public sealed record RemoteFailedUpload
{
    public string Path { get; set; } = string.Empty;

    public RemoteFailure Failure { get; set; }
}

/// <summary>One server's state, as the provider knows it.</summary>
public sealed record RemoteServerStatus
{
    public string Id { get; set; } = string.Empty;

    public RemoteServerState State { get; set; }

    /// <summary>When the server was last tried and failed.</summary>
    public DateTimeOffset? LastFailure { get; set; }

    /// <summary>How many changes on the PC wait for the server.</summary>
    public int ChangesWaiting { get; set; }

    /// <summary>The conflict copies made since the user last opened the folder, by their paths below it.</summary>
    public IReadOnlyList<string> Conflicts { get; set; } = [];

    public IReadOnlyList<RemoteSkippedChange> Skipped { get; set; } = [];

    public IReadOnlyList<RemoteFailedUpload> FailedUploads { get; set; } = [];

    /// <summary>How many entries on the server can't be shown, by reason, in the folders listed so far.</summary>
    public IReadOnlyDictionary<HiddenReason, int> Hidden { get; set; } = new Dictionary<HiddenReason, int>();

    /// <summary>For <see cref="RemoteServerState.KeyChanged"/>: the key the server presents now.</summary>
    public RemoteHostKey? PresentedKey { get; set; }
}

/// <summary>What testing a server's details found.</summary>
public sealed record RemoteTestResult
{
    public RemoteTestOutcome Outcome { get; set; }

    /// <summary>For <see cref="RemoteTestOutcome.Failed"/>: why.</summary>
    public RemoteFailure Failure { get; set; }

    /// <summary>The key the server presented: to confirm, or one that differs from the confirmed one.</summary>
    public RemoteHostKey? PresentedKey { get; set; }

    /// <summary>The folder the server starts in, for a server added without a folder of its own.</summary>
    public string? HomeFolder { get; set; }
}

/// <summary>PCCompanion.RemoteFolders.exe's answer.</summary>
public sealed record RemoteFoldersAnswer
{
    /// <summary>For <see cref="RemoteFoldersRequestKind.Status"/>: the servers whose folders are running.</summary>
    public IReadOnlyList<RemoteServerStatus> Servers { get; set; } = [];

    public RemoteTestResult? Test { get; set; }

    /// <summary>For <see cref="RemoteFoldersRequestKind.CheckKey"/>: whether the server presents that key now.</summary>
    public bool? KeyMatches { get; set; }

    /// <summary>Whether the request was for a server that's running, and was done.</summary>
    public bool Done { get; set; }
}

/// <summary>
/// The pipe of PCCompanion.RemoteFolders.exe, through which the settings app and the host ask for
/// the servers' states, test a server's details for the dialog, and ask for Sync now, Sign in again
/// and the check before Trust the new key. Only the user's own programs on this PC can open it. See
/// "Threads and processes" in docs/features/files/remote-folders.md.
/// </summary>
public static class RemoteFoldersPipe
{
    /// <summary>The longest request the provider reads.</summary>
    public const int MaximumRequestLength = 64 * 1024;

    /// <summary>The longest answer the settings app and the host read.</summary>
    public const int MaximumAnswerLength = 4 * 1024 * 1024;

    /// <summary>How long a test may take: reading a key, or signing in and checking the folder.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long the other requests may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.RemoteFolders");

    /// <summary>
    /// Sends <paramref name="request"/> and returns the answer, or <see langword="null"/> when the
    /// provider doesn't run, as while Remote Folders is off, or doesn't answer in time.
    /// </summary>
    public static async Task<RemoteFoldersAnswer?> AskAsync(RemoteFoldersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            Name,
            Write(request),
            request.Kind == RemoteFoldersRequestKind.TestServer ? TestTimeout : Timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : ReadAnswer(answer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one.</summary>
    public static RemoteFoldersRequest? ReadRequest(byte[] request) => Read(request, RemoteFoldersPipeJsonContext.Default.RemoteFoldersRequest);

    /// <summary>The answer, or <see langword="null"/> when it isn't one.</summary>
    public static RemoteFoldersAnswer? ReadAnswer(byte[] answer) => Read(answer, RemoteFoldersPipeJsonContext.Default.RemoteFoldersAnswer);

    public static byte[] Write(RemoteFoldersRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, RemoteFoldersPipeJsonContext.Default.RemoteFoldersRequest);

    public static byte[] Write(RemoteFoldersAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, RemoteFoldersPipeJsonContext.Default.RemoteFoldersAnswer);

    private static T? Read<T>(byte[] json, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The JSON format of the pipe's requests and answers.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RemoteFoldersRequest))]
[JsonSerializable(typeof(RemoteFoldersAnswer))]
internal sealed partial class RemoteFoldersPipeJsonContext : JsonSerializerContext;
