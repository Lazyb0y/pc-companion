using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>A request from the settings app, through the pipe: switch to a profile.</summary>
public sealed record DisplayProfilesRequest
{
    /// <summary>The profile's number in the profile list, from 1.</summary>
    public int Number { get; set; }

    /// <summary>The profile as the page has it; the host switches only if its list has it unchanged.</summary>
    public DisplayProfile Profile { get; set; } = new();
}

/// <summary>How a request went.</summary>
public enum DisplayProfilesAnswerStatus
{
    /// <summary>Switched, and kept at once, since it only moved displays.</summary>
    Kept,

    /// <summary>Switched, and the host asks whether to keep it.</summary>
    Asking,

    /// <summary>The setup in use already is the profile's.</summary>
    AlreadyInUse,

    /// <summary>A display that's on in the profile isn't connected.</summary>
    NotConnected,

    /// <summary>Windows refused the setup, so nothing changed.</summary>
    Refused,

    /// <summary>Windows refused to read the displays.</summary>
    Failed,

    /// <summary>
    /// The host's list doesn't have the profile unchanged, as after an edit it hasn't read yet, or
    /// the request isn't one, so nothing changed.
    /// </summary>
    UnknownProfile,

    /// <summary>A remote session, where profiles can't be used.</summary>
    RemoteSession,

    /// <summary>Display Profiles turned off before it answered.</summary>
    Off,
}

/// <summary>The host's answer, through the pipe.</summary>
public sealed record DisplayProfilesAnswer
{
    public DisplayProfilesAnswerStatus Status { get; set; }

    /// <summary>For <see cref="DisplayProfilesAnswerStatus.Refused"/> and <see cref="DisplayProfilesAnswerStatus.Failed"/>: Windows' error code.</summary>
    public int ErrorCode { get; set; }

    /// <summary>For <see cref="DisplayProfilesAnswerStatus.Refused"/>: the profile's displays, by their places in it, whose mode Windows doesn't list.</summary>
    public IReadOnlyList<int> ModesNotOffered { get; set; } = [];

    /// <summary>The profile's displays, by their places in it, that Windows applied in its own way.</summary>
    public IReadOnlyList<int> NotAsInProfile { get; set; } = [];
}

/// <summary>
/// The pipe through which the settings app asks the host to switch to a profile, so the switch
/// gets the host's question and revert, whatever happens to the settings window. See "The
/// settings app and the host" in docs/features/display/display-profiles.md.
/// </summary>
public static class DisplayProfilesPipe
{
    /// <summary>The longest request the host reads, far more than a profile of 16 displays.</summary>
    public const int MaximumRequestLength = 256 * 1024;

    /// <summary>The longest answer the settings app reads.</summary>
    public const int MaximumAnswerLength = 64 * 1024;

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.DisplayProfiles");

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when the host doesn't serve the pipe, as while Display
    /// Profiles is off, or doesn't answer within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<DisplayProfilesAnswer?> AskAsync(string name, DisplayProfilesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            name,
            JsonSerializer.SerializeToUtf8Bytes(request, DisplayProfilesPipeJsonContext.Default.DisplayProfilesRequest),
            timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : Read(answer, DisplayProfilesPipeJsonContext.Default.DisplayProfilesAnswer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one, for example from a program that isn't PC Companion.</summary>
    internal static DisplayProfilesRequest? ReadRequest(byte[] request) => Read(request, DisplayProfilesPipeJsonContext.Default.DisplayProfilesRequest);

    internal static byte[] Write(DisplayProfilesAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, DisplayProfilesPipeJsonContext.Default.DisplayProfilesAnswer);

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
[JsonSerializable(typeof(DisplayProfilesRequest))]
[JsonSerializable(typeof(DisplayProfilesAnswer))]
internal sealed partial class DisplayProfilesPipeJsonContext : JsonSerializerContext;
