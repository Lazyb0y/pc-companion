using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// A recent result: when a listed app's shortcut was pressed, the app's place in the list, what
/// happened, and how many times in a row that happened, <see cref="Time"/> being the latest. It
/// names no app.
/// </summary>
public sealed record DropDownResult(DateTimeOffset Time, int AppIndex, DropDownOutcome Outcome, int Count);

/// <summary>
/// The last few results, newest first, which the host gives the settings app through a pipe. See
/// "Recent results" in docs/features/windows/drop-down-apps.md. Results can be added from any thread.
/// </summary>
public sealed class DropDownResults(TimeProvider time)
{
    /// <summary>How many results are kept.</summary>
    public const int Capacity = 5;

    /// <summary>The longest snapshot the settings app reads; five results are far shorter.</summary>
    public const int MaximumSnapshotLength = 16 * 1024;

    private readonly Lock _lock = new();
    private readonly List<DropDownResult> _results = [];

    /// <summary>The name of the pipe that gives the results, for this user session.</summary>
    public static string PipeName => SnapshotPipe.ForSession("PCCompanion.DropDownApps.Results");

    /// <summary>
    /// Adds a result. The same result again right after itself is counted instead, so it doesn't
    /// push the others out.
    /// </summary>
    public void Add(int appIndex, DropDownOutcome outcome)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            if (_results.Count > 0 && _results[0] is var last && (last.AppIndex, last.Outcome) == (appIndex, outcome))
            {
                _results[0] = last with { Time = now, Count = last.Count + 1 };
                return;
            }

            _results.Insert(0, new DropDownResult(now, appIndex, outcome, 1));
            if (_results.Count > Capacity)
            {
                _results.RemoveAt(Capacity);
            }
        }
    }

    /// <summary>The results, newest first.</summary>
    public IReadOnlyList<DropDownResult> List()
    {
        lock (_lock)
        {
            return [.. _results];
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _results.Clear();
        }
    }

    /// <summary>The results as the pipe gives them.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes([.. List()], DropDownResultsJsonContext.Default.DropDownResultArray);

    /// <summary>
    /// Reads the results the pipe gave, or returns <see langword="null"/> when they can't be read,
    /// for example from a program that isn't PC Companion. Only the first <see cref="Capacity"/>
    /// are kept.
    /// </summary>
    public static IReadOnlyList<DropDownResult>? FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DropDownResultsJsonContext.Default.DropDownResultArray) is { } results
                ? [.. results.Take(Capacity)]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The JSON format of the results the pipe gives.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DropDownResult[]))]
internal sealed partial class DropDownResultsJsonContext : JsonSerializerContext;
