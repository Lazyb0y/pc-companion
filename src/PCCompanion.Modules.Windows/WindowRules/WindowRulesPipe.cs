using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>A request from the settings app, through the pipe: Apply now for a rule as the page shows it.</summary>
public sealed record WindowRulesRequest
{
    public WindowRule Rule { get; set; } = new();
}

/// <summary>The host's answer, through the pipe.</summary>
public sealed record WindowRulesAnswer
{
    /// <summary>How many open windows the rule was applied to.</summary>
    public int Windows { get; set; }
}

/// <summary>
/// The pipe through which the settings app asks the host to apply a rule now. See "Apply now" in
/// docs/features/windows/window-rules.md.
/// </summary>
public static class WindowRulesPipe
{
    /// <summary>The longest request the host reads, far more than one rule.</summary>
    public const int MaximumRequestLength = 64 * 1024;

    /// <summary>The longest answer the settings app reads.</summary>
    public const int MaximumAnswerLength = 1024;

    /// <summary>How long the settings app waits for the host.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.WindowRules");

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when the host doesn't serve the pipe, as while Window
    /// Rules is off, or doesn't answer within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<WindowRulesAnswer?> AskAsync(string name, WindowRulesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            name,
            JsonSerializer.SerializeToUtf8Bytes(request, WindowRulesPipeJsonContext.Default.WindowRulesRequest),
            timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : Read(answer, WindowRulesPipeJsonContext.Default.WindowRulesAnswer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one, for example from a program that isn't PC Companion.</summary>
    internal static WindowRulesRequest? ReadRequest(byte[] request) =>
        Read(request, WindowRulesPipeJsonContext.Default.WindowRulesRequest) is { } read ? read with { Rule = read.Rule.Checked() } : null;

    internal static byte[] Write(WindowRulesAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, WindowRulesPipeJsonContext.Default.WindowRulesAnswer);

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
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WindowRulesRequest))]
[JsonSerializable(typeof(WindowRulesAnswer))]
internal sealed partial class WindowRulesPipeJsonContext : JsonSerializerContext;
