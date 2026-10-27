using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>What the settings app asks the host for.</summary>
public enum FolderRulesRequestKind
{
    /// <summary>The history.</summary>
    History,

    /// <summary>Empties the history.</summary>
    ClearHistory,

    /// <summary>Puts back the file of a history entry.</summary>
    Undo,

    /// <summary>The files in a folder that its rules would act on now, for Run now.</summary>
    RunNowList,

    /// <summary>Applies a folder's rules to the files chosen in Run now.</summary>
    RunNow,
}

/// <summary>A request from the settings app, through the pipe.</summary>
public sealed record FolderRulesRequest
{
    public FolderRulesRequestKind Kind { get; set; }

    /// <summary>For Undo: the history entry's number.</summary>
    public long Entry { get; set; }

    /// <summary>For Undo: the history entry's time, so that a number used again after Clear history doesn't undo another entry.</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>For Run now: the watched folder.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>For Run now: the files chosen, as the list gave them.</summary>
    public IReadOnlyList<ChosenFile> Files { get; set; } = [];
}

/// <summary>A file chosen in Run now: its name in the folder, and its size and last modified time as listed.</summary>
public sealed record ChosenFile
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }
}

/// <summary>How a request went.</summary>
public enum FolderRulesAnswerStatus
{
    Done,

    /// <summary>
    /// The host can't do what was asked, such as for a folder that isn't watched, a file that isn't
    /// directly in it, or an entry that isn't in the history or can't be undone, so it did nothing.
    /// </summary>
    Refused,

    /// <summary>For Undo: the file isn't where the rule put it, or it changed since.</summary>
    Changed,

    /// <summary>For Undo: a file has the old name in the old place.</summary>
    PlaceTaken,

    /// <summary>For Undo: another program has the file open.</summary>
    InUse,

    /// <summary>For Undo: Controlled folder access blocked the move back.</summary>
    Blocked,

    /// <summary>For Undo of a copy: the copy's drive has no Recycle Bin, so the copy stays.</summary>
    NoRecycleBin,

    /// <summary>For Undo of a copy: Windows asked about a copy too big for the Recycle Bin, and the answer kept it.</summary>
    Kept,

    /// <summary>Windows refused; <see cref="FolderRulesAnswer.ErrorCode"/> says why.</summary>
    Failed,
}

/// <summary>A file that a folder's rules would act on now, with the rule and what it would do.</summary>
public sealed record RunNowFile
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }

    /// <summary>The rule's name.</summary>
    public string Rule { get; set; } = string.Empty;

    public FolderRuleAction Action { get; set; }

    /// <summary>For a move or copy: the folder the file would go to.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>For a move, copy or rename: the file's name there.</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>Why the rule can't make the destination or the new name, which skips the file.</summary>
    public NamingProblem Naming { get; set; }
}

/// <summary>The host's answer, through the pipe.</summary>
public sealed record FolderRulesAnswer
{
    public FolderRulesAnswerStatus Status { get; set; }

    /// <summary>For <see cref="FolderRulesAnswerStatus.Failed"/>: Windows' error code.</summary>
    public int ErrorCode { get; set; }

    /// <summary>For History: the entries, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get; set; } = [];

    /// <summary>For Run now's list: the files, by name.</summary>
    public IReadOnlyList<RunNowFile> Files { get; set; } = [];
}

/// <summary>
/// The pipe through which the settings app asks the host about Folder Rules. See "The settings
/// app and the host" in docs/features/files/folder-rules.md.
/// </summary>
public static class FolderRulesPipe
{
    /// <summary>The longest request the host reads: Run now with the files of a large folder.</summary>
    public const int MaximumRequestLength = 4 * 1024 * 1024;

    /// <summary>The longest answer the settings app reads, far more than the whole history.</summary>
    public const int MaximumAnswerLength = 16 * 1024 * 1024;

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.FolderRules");

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when the host doesn't serve the pipe, as while Folder
    /// Rules is off, or doesn't answer within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<FolderRulesAnswer?> AskAsync(string name, FolderRulesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            name,
            JsonSerializer.SerializeToUtf8Bytes(request, FolderRulesPipeJsonContext.Default.FolderRulesRequest),
            timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : Read(answer, FolderRulesPipeJsonContext.Default.FolderRulesAnswer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one, for example from a program that isn't PC Companion.</summary>
    internal static FolderRulesRequest? ReadRequest(byte[] request) => Read(request, FolderRulesPipeJsonContext.Default.FolderRulesRequest);

    internal static byte[] Write(FolderRulesAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, FolderRulesPipeJsonContext.Default.FolderRulesAnswer);

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
[JsonSerializable(typeof(FolderRulesRequest))]
[JsonSerializable(typeof(FolderRulesAnswer))]
internal sealed partial class FolderRulesPipeJsonContext : JsonSerializerContext;
