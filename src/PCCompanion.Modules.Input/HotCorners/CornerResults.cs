using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What happened when the pointer reached a corner.
/// </summary>
public enum CornerResultKind
{
    Ran,
    LeftEarly,
    MouseButtonHeld,
    KeyNotHeld,
    PointerConfined,
    PointerHidden,
    FullScreenApp,
    AppExcluded,

    /// <summary>Windows didn't run the action.</summary>
    Refused,

    /// <summary>Run a program doesn't run while PC Companion runs as administrator.</summary>
    ProgramAsAdministrator,

    ProgramNotChosen,

    ProgramNotFound,

    /// <summary>The program isn't the full path of an <c>.exe</c> file.</summary>
    ProgramNotExe,

    /// <summary>Windows couldn't start the program.</summary>
    ProgramCantStart,
}

/// <summary>
/// A recent result: when the pointer reached a corner, with its action, what happened, and how
/// many times in a row that happened, <see cref="Time"/> being the latest.
/// </summary>
public sealed record CornerResult(DateTimeOffset Time, ScreenCorner Corner, HotCornerAction Action, CornerResultKind Kind, int Count);

/// <summary>
/// The last few results, newest first, which the host gives the settings app through a pipe. See
/// "Recent results" in docs/features/input/hot-corners.md. Results can be added from any thread.
/// </summary>
public sealed class CornerResults(TimeProvider time)
{
    /// <summary>How many results are kept.</summary>
    public const int Capacity = 5;

    /// <summary>The longest snapshot the settings app reads; five results are far shorter.</summary>
    public const int MaximumSnapshotLength = 16 * 1024;

    private readonly Lock _lock = new();
    private readonly List<CornerResult> _results = [];

    /// <summary>The name of the pipe that gives the results, for this user session.</summary>
    public static string PipeName => SnapshotPipe.ForSession("PCCompanion.HotCorners.Results");

    /// <summary>
    /// Adds a result. The same result again right after itself is counted instead, so it doesn't
    /// push the others out.
    /// </summary>
    public void Add(ScreenCorner corner, HotCornerAction action, CornerResultKind kind)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            if (_results.Count > 0 && _results[0] is var last && (last.Corner, last.Action, last.Kind) == (corner, action, kind))
            {
                _results[0] = last with { Time = now, Count = last.Count + 1 };
                return;
            }

            _results.Insert(0, new CornerResult(now, corner, action, kind, 1));
            if (_results.Count > Capacity)
            {
                _results.RemoveAt(Capacity);
            }
        }
    }

    /// <summary>The results, newest first.</summary>
    public IReadOnlyList<CornerResult> List()
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
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes([.. List()], CornerResultsJsonContext.Default.CornerResultArray);

    /// <summary>
    /// Reads the results the pipe gave, or returns <see langword="null"/> when they can't be read,
    /// for example from a program that isn't PC Companion. Only the first <see cref="Capacity"/>
    /// are kept.
    /// </summary>
    public static IReadOnlyList<CornerResult>? FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, CornerResultsJsonContext.Default.CornerResultArray) is { } results
                ? [.. results.Take(Capacity)]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The kind of result of a corner that the detector skipped.</summary>
    public static CornerResultKind KindOf(CornerOutcome outcome) => outcome switch
    {
        CornerOutcome.LeftEarly => CornerResultKind.LeftEarly,
        CornerOutcome.MouseButtonHeld => CornerResultKind.MouseButtonHeld,
        CornerOutcome.KeyNotHeld => CornerResultKind.KeyNotHeld,
        CornerOutcome.PointerConfined => CornerResultKind.PointerConfined,
        CornerOutcome.PointerHidden => CornerResultKind.PointerHidden,
        CornerOutcome.FullScreenApp => CornerResultKind.FullScreenApp,
        CornerOutcome.AppExcluded => CornerResultKind.AppExcluded,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}

/// <summary>
/// The JSON format of the results the pipe gives.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CornerResult[]))]
internal sealed partial class CornerResultsJsonContext : JsonSerializerContext;
