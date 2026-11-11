using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>A shortcut that Windows refused, for example because another program has it.</summary>
/// <param name="Feature">The ID of the feature whose shortcut it is, such as <c>hideApps</c>.</param>
/// <param name="Shortcut">The feature's own name for the shortcut, such as <c>hideOthers</c>.</param>
public sealed record RefusedShortcut(string Feature, string Shortcut);

/// <summary>
/// Which shortcuts Windows refused, for every feature, which the host gives the settings app
/// through a pipe so that each page can say so, and nothing else: never which program has a
/// shortcut. Each feature tells it which of its shortcuts Windows refused each time it registers
/// them, and that none were when it stops. It can be set and read from any thread.
/// </summary>
public sealed class ShortcutRefusals
{
    /// <summary>The longest snapshot the settings app reads; the list is far shorter.</summary>
    public const int MaximumSnapshotLength = 64 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, string[]> _refused = new(StringComparer.Ordinal);

    /// <summary>The name of the pipe that gives the list, for this user session.</summary>
    public static string PipeName => SnapshotPipe.ForSession("PCCompanion.Shortcuts.Status");

    /// <summary>Every shortcut Windows refused, by feature.</summary>
    public IReadOnlyList<RefusedShortcut> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _refused.SelectMany(pair => pair.Value.Select(shortcut => new RefusedShortcut(pair.Key, shortcut)))];
            }
        }
    }

    /// <summary>
    /// Says that Windows refused <paramref name="shortcuts"/> of the feature with the ID
    /// <paramref name="feature"/>, in place of what the feature said before; none means that
    /// Windows refused none of them, or that the feature stopped.
    /// </summary>
    public void Set(string feature, IEnumerable<string> shortcuts)
    {
        ArgumentException.ThrowIfNullOrEmpty(feature);
        ArgumentNullException.ThrowIfNull(shortcuts);

        var list = shortcuts.Distinct(StringComparer.Ordinal).ToArray();
        lock (_lock)
        {
            if (list.Length == 0)
            {
                _refused.Remove(feature);
            }
            else
            {
                _refused[feature] = list;
            }
        }
    }

    /// <summary>The list as the pipe gives it.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(All, ShortcutRefusalsJsonContext.Default.IReadOnlyListRefusedShortcut);

    /// <summary>
    /// Reads the list the pipe gave, or returns <see langword="null"/> when it can't be read, for
    /// example from a program that isn't PC Companion.
    /// </summary>
    public static IReadOnlyList<RefusedShortcut>? FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            var list = JsonSerializer.Deserialize(json, ShortcutRefusalsJsonContext.Default.IReadOnlyListRefusedShortcut);
            return list is not null && list.All(refused => refused is not null) ? list : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Which of the shortcuts in <paramref name="refused"/> belong to the feature with the ID <paramref name="feature"/>.</summary>
    public static IReadOnlySet<string> Of(IReadOnlyList<RefusedShortcut> refused, string feature)
    {
        ArgumentNullException.ThrowIfNull(refused);

        return refused.Where(shortcut => shortcut.Feature == feature).Select(shortcut => shortcut.Shortcut).ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>The JSON format of the list the pipe gives.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(IReadOnlyList<RefusedShortcut>))]
internal sealed partial class ShortcutRefusalsJsonContext : JsonSerializerContext;
