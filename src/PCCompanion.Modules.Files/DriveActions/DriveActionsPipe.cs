using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What the settings app asks the host for.</summary>
public enum DriveActionsRequestKind
{
    /// <summary>The drives connected now that count, and the run that's going.</summary>
    Drives,

    /// <summary>Writes a connected drive's ID file, or uses the one it has, and gives the list's entry for it.</summary>
    AddDrive,

    /// <summary>Gives the list's entry for a drive with its ID file and other serial numbers, with its serials, after the user confirms.</summary>
    UseDrive,

    /// <summary>The history, newest first.</summary>
    History,

    /// <summary>Empties the history.</summary>
    ClearHistory,

    /// <summary>What a connected drive's actions would copy now, for Run now.</summary>
    RunNowList,

    /// <summary>Runs a connected drive's actions, with the files chosen in Run now.</summary>
    RunNow,

    /// <summary>Stops the run that's going.</summary>
    Stop,

    /// <summary>Sends the copies an import made to the Recycle Bin: one file, or the whole run.</summary>
    Undo,
}

/// <summary>The files chosen in Run now for one of a drive's imports.</summary>
public sealed record ChosenImport
{
    /// <summary>The action's number among the drive's actions.</summary>
    public int Action { get; set; }

    /// <summary>The files' paths in the folder on the drive, as Run now's list gave them.</summary>
    public IReadOnlyList<string> Files { get; set; } = [];
}

/// <summary>A request from the settings app, through the pipe.</summary>
public sealed record DriveActionsRequest
{
    public DriveActionsRequestKind Kind { get; set; }

    /// <summary>For a connected drive: its letter, as <see cref="DriveActionsRequestKind.Drives"/> gave it.</summary>
    public string Letter { get; set; } = string.Empty;

    /// <summary>For a connected drive: its volume's GUID path, so that a request about a drive swapped meanwhile is refused.</summary>
    public string Volume { get; set; } = string.Empty;

    /// <summary>For Use this drive, Run now's list and Run now: the key of the list's drive the request is about.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>For Add a drive: the name for a drive without a label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>For Run now: the files chosen for each import; an import that isn't here copies what it would on its own.</summary>
    public IReadOnlyList<ChosenImport> Chosen { get; set; } = [];

    /// <summary>For Undo: the run's number in the history.</summary>
    public long Run { get; set; }

    /// <summary>For Undo: when the run started, so that a number used again after Clear history doesn't undo another run.</summary>
    public DateTimeOffset Started { get; set; }

    /// <summary>For Undo of one file: its path in the folder on the drive; empty for the whole run.</summary>
    public string File { get; set; } = string.Empty;
}

/// <summary>How a request went.</summary>
public enum DriveActionsAnswerStatus
{
    Done,

    /// <summary>The host can't do what was asked, such as for a drive that isn't in the list, so it did nothing.</summary>
    Refused,

    /// <summary>The drive isn't connected any more, or another drive has its letter.</summary>
    NotConnected,

    /// <summary>For Add a drive: the drive can't be added; <see cref="DriveActionsAnswer.AddProblem"/> says why.</summary>
    CantAdd,

    /// <summary>Windows refused; <see cref="DriveActionsAnswer.ErrorCode"/> says why.</summary>
    Failed,
}

/// <summary>A connected drive that counts, as the page shows it.</summary>
public sealed record ConnectedDriveInfo
{
    public string Letter { get; set; } = string.Empty;

    public string Volume { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string FileSystem { get; set; } = string.Empty;

    public long Size { get; set; }

    public long Free { get; set; }

    public ConnectedDriveKind? Kind { get; set; }

    public bool IsLocked { get; set; }

    public bool KeepsStreams { get; set; }

    public RecognitionOutcome Outcome { get; set; }

    /// <summary>
    /// Its number in the list: for a drive that's recognized, a look-alike or Camera cards; for one
    /// that isn't recognized, the list's drive with its serial numbers, as when its ID file was
    /// deleted; for a locked one, the list's drive with its hardware serial number; -1 otherwise.
    /// </summary>
    public int Drive { get; set; } = -1;

    /// <summary>The key of the list's drive <see cref="Drive"/> names, or an empty string.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Why it can't be added to the list, or <see cref="DriveActions.AddProblem.None"/>.</summary>
    public AddProblem AddProblem { get; set; }
}

/// <summary>A file an import would copy, for Run now.</summary>
public sealed record PreviewFile
{
    /// <summary>Its path in the folder on the drive.</summary>
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }

    /// <summary>The folder it would go to.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>A file with its name, size and time was imported before, so it's unchecked.</summary>
    public bool ImportedBefore { get; set; }
}

/// <summary>What one of a drive's actions would do now, for Run now.</summary>
public sealed record ActionPreview
{
    /// <summary>The action's number among the drive's actions.</summary>
    public int Action { get; set; }

    /// <summary>For an import: the files it would copy, those imported before included.</summary>
    public IReadOnlyList<PreviewFile> Files { get; set; } = [];

    /// <summary>For a backup: how many files it would copy, and the space they need.</summary>
    public int Copies { get; set; }

    public long Bytes { get; set; }

    /// <summary>For a backup: how many files it would leave, for the reasons the history gives.</summary>
    public int Skipped { get; set; }
}

/// <summary>The host's answer, through the pipe.</summary>
public sealed record DriveActionsAnswer
{
    public DriveActionsAnswerStatus Status { get; set; }

    /// <summary>For <see cref="DriveActionsAnswerStatus.Failed"/>: Windows' error code.</summary>
    public int ErrorCode { get; set; }

    /// <summary>For <see cref="DriveActionsAnswerStatus.CantAdd"/>: why.</summary>
    public AddProblem AddProblem { get; set; }

    /// <summary>For Drives: the connected drives that count.</summary>
    public IReadOnlyList<ConnectedDriveInfo> Drives { get; set; } = [];

    /// <summary>For Drives and History: the run that's going, if any.</summary>
    public RunProgress? Running { get; set; }

    /// <summary>For Add a drive and Use this drive: the list's entry, which the settings app saves.</summary>
    public DriveEntry? Entry { get; set; }

    /// <summary>For History: the runs, newest first.</summary>
    public IReadOnlyList<RunEntry> Runs { get; set; } = [];

    /// <summary>For Run now's list: what each action would do.</summary>
    public IReadOnlyList<ActionPreview> Actions { get; set; } = [];

    /// <summary>For Undo: how many copies went to the Recycle Bin, and how many stayed because they changed or Windows kept them.</summary>
    public int Undone { get; set; }

    public int NotUndone { get; set; }
}

/// <summary>
/// The pipe through which the settings app asks the host about Drive Actions. See "The settings
/// app and the host" in docs/features/files/drive-actions.md.
/// </summary>
public static class DriveActionsPipe
{
    /// <summary>The longest request the host reads: Run now with the files of a large card.</summary>
    public const int MaximumRequestLength = 8 * 1024 * 1024;

    /// <summary>The longest answer the settings app reads, far more than the whole history.</summary>
    public const int MaximumAnswerLength = 32 * 1024 * 1024;

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.DriveActions");

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when the host doesn't serve the pipe, as while Drive Actions
    /// is off, or doesn't answer within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<DriveActionsAnswer?> AskAsync(string name, DriveActionsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            name,
            JsonSerializer.SerializeToUtf8Bytes(request, DriveActionsPipeJsonContext.Default.DriveActionsRequest),
            timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : Read(answer, DriveActionsPipeJsonContext.Default.DriveActionsAnswer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one, for example from a program that isn't PC Companion.</summary>
    internal static DriveActionsRequest? ReadRequest(byte[] request) => Read(request, DriveActionsPipeJsonContext.Default.DriveActionsRequest);

    internal static byte[] Write(DriveActionsAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, DriveActionsPipeJsonContext.Default.DriveActionsAnswer);

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
[JsonSerializable(typeof(DriveActionsRequest))]
[JsonSerializable(typeof(DriveActionsAnswer))]
internal sealed partial class DriveActionsPipeJsonContext : JsonSerializerContext;
