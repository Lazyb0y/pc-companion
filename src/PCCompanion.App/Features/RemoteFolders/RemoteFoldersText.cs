using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.App.Features.RemoteFolders;

/// <summary>The page's and the server dialog's text for servers, their states and their problems.</summary>
public static class RemoteFoldersText
{
    private static readonly CompositeFormat NameTooLongFormat = CompositeFormat.Parse(Strings.RemoteFoldersNameTooLong);
    private static readonly CompositeFormat LastTriedFormat = CompositeFormat.Parse(Strings.RemoteFoldersLastTried);
    private static readonly CompositeFormat WithChangesFormat = CompositeFormat.Parse(Strings.RemoteFoldersStateWithChanges);
    private static readonly CompositeFormat SkippedDeleteFormat = CompositeFormat.Parse(Strings.RemoteFoldersSkippedDelete);
    private static readonly CompositeFormat SkippedRenameFormat = CompositeFormat.Parse(Strings.RemoteFoldersSkippedRename);
    private static readonly CompositeFormat FailedUploadFormat = CompositeFormat.Parse(Strings.RemoteFoldersFailedUpload);
    private static readonly CompositeFormat HiddenLineFormat = CompositeFormat.Parse(Strings.RemoteFoldersHiddenLine);

    // Paths are isolated as left to right, so they read the same within a sentence in any language.
    private const char LeftToRightIsolate = '\u2066';
    private const char PopIsolate = '\u2069';

    /// <summary>What's wrong with a box of the server dialog, or an empty string for nothing.</summary>
    public static string Problem(ServerProblem problem) => problem switch
    {
        ServerProblem.None => string.Empty,
        ServerProblem.NoName => Strings.RemoteFoldersNameMissing,
        ServerProblem.NameTooLong => ReadingOrder.Format(NameTooLongFormat, RemoteFoldersSettings.MaximumNameLength.ToString("N0", CultureInfo.CurrentCulture)),
        ServerProblem.InvalidName => Strings.RemoteFoldersNameInvalid,
        ServerProblem.NameInUse => Strings.RemoteFoldersNameInUse,
        ServerProblem.NoAddress or ServerProblem.InvalidAddress => Strings.RemoteFoldersAddressInvalid,
        ServerProblem.InvalidPort => Strings.RemoteFoldersPortInvalid,
        ServerProblem.NoKeyFile or ServerProblem.KeyFileNotFound => Strings.RemoteFoldersKeyFileNotFound,

        // A missing user name, password or folder shows as the hint below Connect instead.
        _ => string.Empty,
    };

    /// <summary>Why Connect failed, below the button.</summary>
    public static string ConnectFailure(RemoteFailure failure) => failure switch
    {
        RemoteFailure.Unreachable => Strings.RemoteFoldersConnectUnreachable,
        RemoteFailure.SignInFailed => Strings.RemoteFoldersConnectSignInFailed,
        RemoteFailure.KeyFile => Strings.RemoteFoldersConnectKeyFile,
        RemoteFailure.NoSafeAlgorithm => Strings.RemoteFoldersNoSafeAlgorithmProblem,
        RemoteFailure.PermissionDenied => Strings.RemoteFoldersConnectPermissionDenied,
        RemoteFailure.NotFound => Strings.RemoteFoldersFolderNotFound,
        _ => Strings.RemoteFoldersConnectFailed,
    };

    /// <summary>
    /// A server's state on the page, with the time of the last try when the server can't be
    /// reached, and the changes that wait.
    /// </summary>
    /// <param name="status">What the provider says about the server, or null when it doesn't run the server's folder.</param>
    /// <param name="running">Whether the provider answered.</param>
    /// <param name="starting">Whether the provider may still be starting, when it doesn't answer.</param>
    public static string State(RemoteServerStatus? status, bool running, bool starting)
    {
        string state = status?.State switch
        {
            null => running ? Strings.RemoteFoldersStateNotConnected : starting ? Strings.RemoteFoldersStateNotRunning : Strings.RemoteFoldersStateStopped,
            RemoteServerState.Connected => Strings.RemoteFoldersStateConnected,
            RemoteServerState.Unreachable => Strings.RemoteFoldersStateUnreachable,
            RemoteServerState.SignInFailed => Strings.RemoteFoldersStateSignInFailed,
            RemoteServerState.KeyChanged => Strings.RemoteFoldersStateKeyChanged,
            RemoteServerState.NoSafeAlgorithm => Strings.RemoteFoldersStateNoSafeAlgorithm,
            _ => Strings.RemoteFoldersStateNotConnected,
        };

        if (status is { State: RemoteServerState.Unreachable, LastFailure: { } last })
        {
            state = string.Format(CultureInfo.CurrentCulture, LastTriedFormat, state, last.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
        }

        return ReadingOrder.Line(status is { ChangesWaiting: > 0 } waiting
            ? string.Format(CultureInfo.CurrentCulture, WithChangesFormat, state, PluralStrings.Format("RemoteFoldersChangesWaiting", waiting.ChangesWaiting))
            : state);
    }

    /// <summary>The conflict copies, one path below the server's folder per line.</summary>
    public static string Conflicts(IReadOnlyList<string> paths) => string.Join(Environment.NewLine, paths.Select(Path));

    /// <summary>The changes the server refused and the files it didn't take, one per line, with the reason.</summary>
    public static string NotMade(IReadOnlyList<RemoteSkippedChange> skipped, IReadOnlyList<RemoteFailedUpload> failed)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        ArgumentNullException.ThrowIfNull(failed);

        var lines = skipped.Select(change => change.IsRename
            ? string.Format(CultureInfo.CurrentCulture, SkippedRenameFormat, Path(change.Path), Path(change.Target ?? string.Empty), Reason(change.Failure))
            : string.Format(CultureInfo.CurrentCulture, SkippedDeleteFormat, Path(change.Path), Reason(change.Failure)))
            .Concat(failed.Select(upload => string.Format(CultureInfo.CurrentCulture, FailedUploadFormat, Path(upload.Path), Reason(upload.Failure))));
        return ReadingOrder.Line(string.Join(Environment.NewLine, lines));
    }

    /// <summary>How many entries on the server can't be shown, one reason per line, without their names.</summary>
    public static string Hidden(IReadOnlyDictionary<HiddenReason, int> hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);

        var lines = hidden
            .Where(pair => pair.Key != HiddenReason.None && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => string.Format(CultureInfo.CurrentCulture, HiddenLineFormat, HiddenReasonText(pair.Key), pair.Value.ToString("N0", CultureInfo.CurrentCulture)));
        return ReadingOrder.Line(string.Join(Environment.NewLine, lines));
    }

    private static string HiddenReasonText(HiddenReason reason) => reason switch
    {
        HiddenReason.InvalidName => Strings.RemoteFoldersHiddenInvalidName,
        HiddenReason.TooLong => Strings.RemoteFoldersHiddenTooLong,
        HiddenReason.NotUtf8 => Strings.RemoteFoldersHiddenNotUtf8,
        HiddenReason.SameNameOtherCase => Strings.RemoteFoldersHiddenSameNameOtherCase,
        HiddenReason.OwnTemporaryFile => Strings.RemoteFoldersHiddenOwnTemporaryFile,
        _ => Strings.RemoteFoldersHiddenTooMany,
    };

    private static string Reason(RemoteFailure failure) => failure switch
    {
        RemoteFailure.Unreachable => Strings.RemoteFoldersReasonUnreachable,
        RemoteFailure.KeyChanged => Strings.RemoteFoldersReasonKeyChanged,
        RemoteFailure.SignInFailed or RemoteFailure.KeyFile => Strings.RemoteFoldersReasonSignInFailed,
        RemoteFailure.NotFound => Strings.RemoteFoldersReasonNotFound,
        RemoteFailure.PermissionDenied => Strings.RemoteFoldersReasonPermissionDenied,
        _ => Strings.RemoteFoldersReasonFailed,
    };

    /// <summary>A path, address or key isolated as left to right, so it reads the same within text in any language.</summary>
    public static string Isolated(string text) => LeftToRightIsolate + text + PopIsolate;

    private static string Path(string path) => Isolated(path);
}
