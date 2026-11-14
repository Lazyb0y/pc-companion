using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.RemoteFolders;

/// <summary>
/// Answers the requests that come through <see cref="RemoteFoldersPipe"/>: every server's state, a
/// test of a server's details, Sync now, Sign in again, the key check before the settings app trusts
/// a new key, and conflicts seen. Each request is checked: a server must be one whose folder runs.
/// </summary>
public sealed partial class ProviderPipe(
    Func<IReadOnlyList<ServerFolder>> folders,
    Func<RemoteServer, string?, RemoteHostKey?, CancellationToken, Task<RemoteTestResult>> test,
    Func<RemoteServer, CancellationToken, Task<ServerKey>> readKey,
    TimeProvider time,
    ILogger logger)
{
    /// <summary>How far back Sync now looks for folders that were listed.</summary>
    public static readonly TimeSpan SyncListedWithin = TimeSpan.FromHours(1);

    /// <summary>Answers a request; one that isn't a request gets an empty answer. Never throws.</summary>
    public async Task<byte[]> AnswerAsync(byte[] message)
    {
        var answer = new RemoteFoldersAnswer();
        try
        {
            if (RemoteFoldersPipe.ReadRequest(message) is { } request)
            {
                answer = await AnswerAsync(request).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
        }

        return RemoteFoldersPipe.Write(answer);
    }

    private static RemoteFailure FailureOf(RemoteError error) => error switch
    {
        RemoteError.Unreachable => RemoteFailure.Unreachable,
        RemoteError.KeyNotTrusted => RemoteFailure.KeyChanged,
        RemoteError.SignInFailed => RemoteFailure.SignInFailed,
        RemoteError.KeyFile => RemoteFailure.KeyFile,
        RemoteError.NoSafeAlgorithm => RemoteFailure.NoSafeAlgorithm,
        RemoteError.NotFound => RemoteFailure.NotFound,
        RemoteError.PermissionDenied => RemoteFailure.PermissionDenied,
        _ => RemoteFailure.Failed,
    };

    private static RemoteServerState StateOf(ServerStatus status) => status switch
    {
        ServerStatus.Connected => RemoteServerState.Connected,
        ServerStatus.Unreachable => RemoteServerState.Unreachable,
        ServerStatus.SignInFailed => RemoteServerState.SignInFailed,
        ServerStatus.KeyChanged => RemoteServerState.KeyChanged,
        ServerStatus.Error => RemoteServerState.NoSafeAlgorithm,
        _ => RemoteServerState.NotConnected,
    };

    private static RemoteServerStatus StatusOf(ServerFolder folder)
    {
        var state = folder.State;
        return new RemoteServerStatus
        {
            Id = folder.Server.Id,
            State = StateOf(state.Status),
            LastFailure = state.LastFailure,
            ChangesWaiting = folder.Uploads.Waiting + folder.Changes.Waiting,
            Conflicts = [.. folder.Uploads.Conflicts.Order(StringComparer.OrdinalIgnoreCase)],
            Skipped = [.. folder.Changes.Skipped.Select(skipped => new RemoteSkippedChange
            {
                IsRename = skipped.Entry.Kind == JournalKind.Rename,
                Path = skipped.Entry.Path,
                Target = skipped.Entry.Target,
                Failure = FailureOf(skipped.Error),
            })],
            FailedUploads = [.. folder.Uploads.Failed.Select(failed => new RemoteFailedUpload { Path = failed.Key, Failure = FailureOf(failed.Value) })],
            Hidden = folder.Listings.Hidden,
            PresentedKey = state.PresentedKey is { } key ? new RemoteHostKey { Type = key.Type, Sha256 = key.Sha256 } : null,
        };
    }

    private async Task<RemoteFoldersAnswer> AnswerAsync(RemoteFoldersRequest request)
    {
        if (request.Kind == RemoteFoldersRequestKind.Status)
        {
            return new RemoteFoldersAnswer { Servers = [.. folders().Select(StatusOf)], Done = true };
        }

        if (request.Kind == RemoteFoldersRequestKind.TestServer)
        {
            if (request.Server is not { } server)
            {
                return new RemoteFoldersAnswer();
            }

            using var timeout = new CancellationTokenSource(RemoteFoldersPipe.TestTimeout - TimeSpan.FromSeconds(2), time);
            var result = await test(server, request.Secret, request.Key, timeout.Token).ConfigureAwait(false);
            LogTested(logger, result.Outcome);
            return new RemoteFoldersAnswer { Test = result, Done = true };
        }

        var chosen = folders().Where(folder => request.ServerId is null ? request.Kind == RemoteFoldersRequestKind.SyncNow : folder.Server.Id.Equals(request.ServerId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (chosen.Count == 0)
        {
            return new RemoteFoldersAnswer();
        }

        switch (request.Kind)
        {
            case RemoteFoldersRequestKind.SyncNow:
            case RemoteFoldersRequestKind.SignInAgain:
                // The server is tried again at once; connections that work stay open, since closing
                // them all at once left an OpenSSH for Windows server refusing new ones in testing.
                foreach (var folder in chosen)
                {
                    folder.State.TryAgainNow();
                    _ = Task.Run(() => SyncAsync(folder));
                }

                return new RemoteFoldersAnswer { Done = true };

            case RemoteFoldersRequestKind.CheckKey when request.Key is { } key:
                using (var timeout = new CancellationTokenSource(RemoteFoldersPipe.Timeout - TimeSpan.FromSeconds(1), time))
                {
                    try
                    {
                        var presented = await readKey(chosen[0].Server, timeout.Token).ConfigureAwait(false);
                        return new RemoteFoldersAnswer { KeyMatches = presented.Type == key.Type && presented.Sha256 == key.Sha256, Done = true };
                    }
                    catch (Exception exception) when (exception is RemoteServerException or OperationCanceledException)
                    {
                        return new RemoteFoldersAnswer { KeyMatches = false, Done = true };
                    }
                }

            case RemoteFoldersRequestKind.ConflictsSeen:
                chosen[0].Uploads.ClearConflicts();
                return new RemoteFoldersAnswer { Done = true };

            default:
                return new RemoteFoldersAnswer();
        }
    }

    // The folders listed in the last hour are listed again, and the kept ones too.
    private async Task SyncAsync(ServerFolder folder)
    {
        try
        {
            var since = time.GetUtcNow() - SyncListedWithin;
            var listed = folder.Listings.Listed.Where(pair => pair.Value >= since).Select(pair => pair.Key).ToList();
            if (listed.Count == 0)
            {
                listed.Add(string.Empty);
            }

            foreach (string relative in listed)
            {
                if (!await folder.Listings.RefreshAsync(relative).ConfigureAwait(false))
                {
                    break;
                }
            }

            folder.Keeping?.FindPinned(keptFoldersOnly: true);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A server's details were tested: {Outcome}.")]
    private static partial void LogTested(ILogger logger, RemoteTestOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A request through the pipe failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Tests a server's details for the settings app's dialog, over SFTP.</summary>
public static class ServerTest
{
    /// <summary>
    /// Without <paramref name="confirmedKey"/>, reads the server's key and sends nothing else; with
    /// it, signs in, trusting only that key, and checks the folder on the server.
    /// </summary>
    public static async Task<RemoteTestResult> TestAsync(RemoteServer server, string? secret, RemoteHostKey? confirmedKey, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (server.KnownProtocol != RemoteProtocol.Sftp || string.IsNullOrWhiteSpace(server.Host) || server.Port is < 1 or > 65535)
        {
            return new RemoteTestResult { Outcome = RemoteTestOutcome.Failed, Failure = RemoteFailure.Failed };
        }

        try
        {
            if (confirmedKey is null)
            {
                var key = await SftpServer.ReadKeyAsync(server.Host, server.Port, cancellation).ConfigureAwait(false);
                return new RemoteTestResult { Outcome = RemoteTestOutcome.KeyToConfirm, PresentedKey = new RemoteHostKey { Type = key.Type, Sha256 = key.Sha256 } };
            }

            var signIn = server.KnownSignIn == RemoteSignIn.Key
                ? new SftpSignIn(server.Host, server.Port, server.UserName, null, server.KeyFile, secret)
                : new SftpSignIn(server.Host, server.Port, server.UserName, secret, null, null);
            using var connection = await SftpServer.ConnectAsync(signIn, new ServerKey(confirmedKey.Type, confirmedKey.Sha256), cancellation).ConfigureAwait(false);
            string folder = string.IsNullOrEmpty(server.RemoteFolder) ? connection.HomeFolder : server.RemoteFolder;
            return await connection.StatAsync(folder, cancellation).ConfigureAwait(false) is { IsDirectory: true }
                ? new RemoteTestResult { Outcome = RemoteTestOutcome.Connected, HomeFolder = connection.HomeFolder }
                : new RemoteTestResult { Outcome = RemoteTestOutcome.FolderNotFound, HomeFolder = connection.HomeFolder };
        }
        catch (RemoteServerException exception)
        {
            return new RemoteTestResult
            {
                Outcome = RemoteTestOutcome.Failed,
                Failure = exception.Error switch
                {
                    RemoteError.Unreachable => RemoteFailure.Unreachable,
                    RemoteError.KeyNotTrusted => RemoteFailure.KeyChanged,
                    RemoteError.SignInFailed => RemoteFailure.SignInFailed,
                    RemoteError.KeyFile => RemoteFailure.KeyFile,
                    RemoteError.NoSafeAlgorithm => RemoteFailure.NoSafeAlgorithm,
                    RemoteError.PermissionDenied => RemoteFailure.PermissionDenied,
                    _ => RemoteFailure.Failed,
                },
                PresentedKey = exception.PresentedKey is { } presented ? new RemoteHostKey { Type = presented.Type, Sha256 = presented.Sha256 } : null,
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new RemoteTestResult { Outcome = RemoteTestOutcome.Failed, Failure = RemoteFailure.Unreachable };
        }
    }

    /// <summary>The key a server presents now.</summary>
    public static Task<ServerKey> ReadKeyAsync(RemoteServer server, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(server);
        return SftpServer.ReadKeyAsync(server.Host, server.Port, cancellation);
    }
}
