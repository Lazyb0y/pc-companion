using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>A request through the pipe: run an action.</summary>
public sealed record LauncherRequest
{
    public string Feature { get; set; } = "";

    public string Action { get; set; } = "";

    public string? Target { get; set; }
}

/// <summary>How the host took a request.</summary>
public enum LauncherResult
{
    /// <summary>The host started the action.</summary>
    [JsonStringEnumMemberName("ran")]
    Ran,

    /// <summary>The action can't run now, and the host showed a notification that says why.</summary>
    [JsonStringEnumMemberName("refused")]
    Refused,

    /// <summary>The request isn't one, so nothing ran and nothing showed.</summary>
    [JsonStringEnumMemberName("invalid")]
    Invalid,
}

/// <summary>The host's answer through the pipe.</summary>
public sealed record LauncherAnswer
{
    public LauncherResult Result { get; set; }
}

/// <summary>
/// The pipe through which the command asks the host to run an action, which the host serves while
/// Launcher Actions is on. See "The host" in docs/features/input/launcher-actions.md.
/// </summary>
public static class LauncherPipe
{
    /// <summary>The longest request the host reads: more than the longest command, with every character of its target escaped.</summary>
    public const int MaximumRequestLength = 2 * 1024;

    /// <summary>The longest answer the command reads.</summary>
    public const int MaximumAnswerLength = 256;

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.Actions");

    /// <summary>
    /// Asks the host serving the pipe named <paramref name="name"/> to run <paramref name="action"/>,
    /// and returns its answer, or <see langword="null"/> when no host serves the pipe or answers
    /// within <paramref name="timeout"/>, or the answer isn't one.
    /// </summary>
    public static async Task<LauncherResult?> AskAsync(string name, FeatureAction action, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var answer = await RequestPipe.AskAsync(name, WriteRequest(action), timeout, MaximumAnswerLength, cancellationToken).ConfigureAwait(false);
        return answer is null ? null : ReadAnswer(answer);
    }

    /// <summary><paramref name="action"/> as the command sends it.</summary>
    public static byte[] WriteRequest(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return JsonSerializer.SerializeToUtf8Bytes(
            new LauncherRequest { Feature = action.FeatureId, Action = action.ActionId, Target = action.Target },
            LauncherPipeJsonContext.Default.LauncherRequest);
    }

    /// <summary>
    /// The action <paramref name="request"/> asks for, or <see langword="null"/> when it isn't a
    /// request that keeps the command's rules, for example from a program that isn't PC Companion.
    /// </summary>
    public static FeatureAction? ReadRequest(ReadOnlySpan<byte> request)
    {
        LauncherRequest? read;
        try
        {
            read = JsonSerializer.Deserialize(request, LauncherPipeJsonContext.Default.LauncherRequest);
        }
        catch (JsonException)
        {
            return null;
        }

        return read is not null && new FeatureAction(read.Feature, read.Action, read.Target) is var action && LauncherCommand.IsValid(action)
            ? action
            : null;
    }

    /// <summary><paramref name="result"/> as the host answers it.</summary>
    public static byte[] WriteAnswer(LauncherResult result) =>
        JsonSerializer.SerializeToUtf8Bytes(new LauncherAnswer { Result = result }, LauncherPipeJsonContext.Default.LauncherAnswer);

    /// <summary>The result the host answered, or <see langword="null"/> when the answer isn't one.</summary>
    public static LauncherResult? ReadAnswer(ReadOnlySpan<byte> answer)
    {
        try
        {
            return JsonSerializer.Deserialize(answer, LauncherPipeJsonContext.Default.LauncherAnswer) is { } read && Enum.IsDefined(read.Result)
                ? read.Result
                : null;
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
[JsonSerializable(typeof(LauncherRequest))]
[JsonSerializable(typeof(LauncherAnswer))]
internal sealed partial class LauncherPipeJsonContext : JsonSerializerContext;
