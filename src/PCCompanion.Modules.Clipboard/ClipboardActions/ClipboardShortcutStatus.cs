using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>Which of Clipboard Actions' shortcuts Windows refused, for example because another program has it.</summary>
/// <param name="CopiedText">Whether Windows refused Windows + Alt + V.</param>
/// <param name="SelectedText">Whether Windows refused Windows + Alt + S while Selected text is on.</param>
public sealed record ClipboardShortcutRefusals(bool CopiedText, bool SelectedText);

/// <summary>
/// Which shortcuts Windows refused, which the host gives the settings app through a pipe, and
/// nothing else: never which program has a shortcut. See "A shortcut another program has" in
/// docs/features/clipboard/clipboard-actions.md. It can be set and read from any thread.
/// </summary>
public sealed class ClipboardShortcutStatus
{
    /// <summary>The longest snapshot the settings app reads; the status is far shorter.</summary>
    public const int MaximumSnapshotLength = 1024;

    private readonly Lock _lock = new();
    private ClipboardShortcutRefusals _refused = new(false, false);

    /// <summary>The name of the pipe that gives the status, for this user session.</summary>
    public static string PipeName => SnapshotPipe.ForSession("PCCompanion.ClipboardActions.Status");

    /// <summary>Which shortcuts Windows refused when they were last registered.</summary>
    public ClipboardShortcutRefusals Refused
    {
        get
        {
            lock (_lock)
            {
                return _refused;
            }
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_lock)
            {
                _refused = value;
            }
        }
    }

    /// <summary>The status as the pipe gives it.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(Refused, ClipboardShortcutStatusJsonContext.Default.ClipboardShortcutRefusals);

    /// <summary>
    /// Reads the status the pipe gave, or returns <see langword="null"/> when it can't be read, for
    /// example from a program that isn't PC Companion.
    /// </summary>
    public static ClipboardShortcutRefusals? FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ClipboardShortcutStatusJsonContext.Default.ClipboardShortcutRefusals);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The JSON format of the status the pipe gives.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ClipboardShortcutRefusals))]
internal sealed partial class ClipboardShortcutStatusJsonContext : JsonSerializerContext;
