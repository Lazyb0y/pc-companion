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
}

/// <summary>A request from the settings app, through the pipe.</summary>
public sealed record FolderRulesRequest
{
    public FolderRulesRequestKind Kind { get; set; }
}

/// <summary>How a request went.</summary>
public enum FolderRulesAnswerStatus
{
    Done,

    /// <summary>The host can't do what was asked, so it did nothing.</summary>
    Refused,
}

/// <summary>The host's answer, through the pipe.</summary>
public sealed record FolderRulesAnswer
{
    public FolderRulesAnswerStatus Status { get; set; }

    /// <summary>For History: the entries, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// The pipe through which the settings app asks the host about Folder Rules. See "The settings
/// app and the host" in docs/features/files/folder-rules.md.
/// </summary>
public static class FolderRulesPipe
{
    /// <summary>The longest request the host reads.</summary>
    public const int MaximumRequestLength = 64 * 1024;

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
