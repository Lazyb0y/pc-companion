using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Reads and writes <c>window-rules.json</c> in the data folder: the places and opacities rules
/// remember, as "Remembered places and opacities in a file" in
/// docs/features/windows/window-rules.md says. Only the host writes it, on the thread pool. The
/// file is untrusted: it's read only up to <see cref="MaximumFileSize"/>, and what can't be used is
/// left out as it's loaded.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class RememberedPlacesStore(string path)
{
    public const string FileName = "window-rules.json";

    /// <summary>The largest file that's read, far more than 50 rules' places take.</summary>
    public const int MaximumFileSize = 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// What the file keeps, or none when there's no file. <paramref name="readable"/> is false
    /// when there's a file that can't be read, as after a hand edit, which the next save replaces.
    /// </summary>
    public IReadOnlyList<RememberedRule?> Load(out bool readable)
    {
        readable = true;
        try
        {
            using var stream = File.OpenRead(FilePath);
            var buffer = new byte[MaximumFileSize + 1];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (length > MaximumFileSize)
            {
                readable = false;
                return [];
            }

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), RememberedPlacesJsonContext.Default.RememberedFile)?.Rules ?? [];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            readable = false;
            return [];
        }
    }

    /// <summary>Writes what rules remember: to a file beside it first, which then replaces it, so a crash never leaves half a file.</summary>
    public void Save(IReadOnlyList<RememberedRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new RememberedFile { Rules = rules }, RememberedPlacesJsonContext.Default.RememberedFile);
            stream.Flush(flushToDisk: true);
        }

        // The move fails while another process reads the file, which only takes a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryFile, FilePath, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceAttempts && exception is UnauthorizedAccessException or (IOException and not FileNotFoundException))
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    /// <summary>Deletes the file, as when the feature is turned off.</summary>
    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (DirectoryNotFoundException)
        {
            // Without a data folder, there's no file to delete.
        }
    }
}

/// <summary>The file's format.</summary>
internal sealed record RememberedFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<RememberedRule?> Rules { get; set; } = [];
}

/// <summary>The JSON format of <c>window-rules.json</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RememberedFile))]
internal sealed partial class RememberedPlacesJsonContext : JsonSerializerContext;
