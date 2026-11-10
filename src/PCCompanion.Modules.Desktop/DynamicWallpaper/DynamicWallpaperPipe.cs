using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>What the settings app asks the host, through the pipe.</summary>
public enum DynamicWallpaperRequestKind
{
    /// <summary>The feature's state.</summary>
    State,

    /// <summary>Change the background again after stepping aside, then the state.</summary>
    Resume,
}

/// <summary>A request from the settings app, through the pipe.</summary>
public sealed record DynamicWallpaperRequest
{
    public DynamicWallpaperRequestKind Kind { get; set; }
}

/// <summary>The host's answer, through the pipe: what only it knows about the feature's work.</summary>
public sealed record DynamicWallpaperAnswer
{
    /// <summary>Whether the feature runs in the host.</summary>
    public bool Running { get; set; }

    /// <summary>The period whose pictures were set last, such as <c>Sun.Day</c>.</summary>
    public string? Period { get; set; }

    public WallpaperBlock Block { get; set; }

    /// <summary>The period's pictures and folders that can't be used now.</summary>
    public IReadOnlyList<PictureIssue> Issues { get; set; } = [];

    /// <summary>Whether Windows spotlight changed the background before Dynamic Wallpaper did, for the page's note after turning off.</summary>
    public bool Spotlight { get; set; }
}

/// <summary>
/// The pipe through which the settings app asks the host what only it knows: whether the feature
/// stepped aside, a policy manages the background, a picture can't be used or Explorer didn't
/// answer, and the period in use; and asks it to resume. See "The settings app and the host" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class DynamicWallpaperPipe
{
    /// <summary>The longest request the host reads.</summary>
    public const int MaximumRequestLength = 1024;

    /// <summary>The longest answer the settings app reads.</summary>
    public const int MaximumAnswerLength = 64 * 1024;

    /// <summary>The name of the pipe, for this user session.</summary>
    public static string Name => SnapshotPipe.ForSession("PCCompanion.DynamicWallpaper");

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when the host doesn't serve the pipe, as while Dynamic
    /// Wallpaper is off, or doesn't answer within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<DynamicWallpaperAnswer?> AskAsync(string name, DynamicWallpaperRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = await RequestPipe.AskAsync(
            name,
            JsonSerializer.SerializeToUtf8Bytes(request, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperRequest),
            timeout,
            MaximumAnswerLength,
            cancellationToken).ConfigureAwait(false);

        return answer is null ? null : Read(answer, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperAnswer);
    }

    /// <summary>The request, or <see langword="null"/> when it isn't one, for example from a program that isn't PC Companion.</summary>
    public static DynamicWallpaperRequest? ReadRequest(byte[] request) => Read(request, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperRequest);

    /// <summary>The answer, or <see langword="null"/> when it isn't one.</summary>
    public static DynamicWallpaperAnswer? ReadAnswer(byte[] answer) => Read(answer, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperAnswer);

    public static byte[] Write(DynamicWallpaperRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperRequest);

    public static byte[] Write(DynamicWallpaperAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, DynamicWallpaperPipeJsonContext.Default.DynamicWallpaperAnswer);

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
[JsonSerializable(typeof(DynamicWallpaperRequest))]
[JsonSerializable(typeof(DynamicWallpaperAnswer))]
internal sealed partial class DynamicWallpaperPipeJsonContext : JsonSerializerContext;
