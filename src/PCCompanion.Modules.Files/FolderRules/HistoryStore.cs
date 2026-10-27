using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Reads and writes <c>folder-rules.json</c> in the data folder, which holds the history and when
/// the rules last looked at each folder. Only the host writes it. Any program running as the user
/// can change the file, so it's read as untrusted.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class HistoryStore(string path)
{
    /// <summary>The file's name in the data folder.</summary>
    public const string FileName = "folder-rules.json";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 4 * 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the saved history, or an empty one when there's none yet. When the file can't be
    /// read, <paramref name="readable"/> is <see langword="false"/> and the history is empty; the
    /// next save replaces the file.
    /// </summary>
    public FolderRulesHistory Load(out bool readable)
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
                return new FolderRulesHistory();
            }

            var file = JsonSerializer.Deserialize(buffer.AsSpan(0, length), HistoryJsonContext.Default.HistoryFile) ?? new HistoryFile();

            // The JSON reader checks nullable annotations on properties but not on a list's items.
            return new FolderRulesHistory(file.Entries.Where(entry => entry is not null), file.LastLooked);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new FolderRulesHistory();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            readable = false;
            return new FolderRulesHistory();
        }
    }

    /// <summary>
    /// Saves the history, to a temporary file first that then replaces the file, so a reader never
    /// sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(FolderRulesHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var file = new HistoryFile { Entries = history.Entries, LastLooked = history.LastLooked };
            JsonSerializer.Serialize(stream, file, HistoryJsonContext.Default.HistoryFile);
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
}

/// <summary>The contents of <c>folder-rules.json</c>.</summary>
internal sealed record HistoryFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<HistoryEntry> Entries { get; set; } = [];

    public IReadOnlyDictionary<string, DateTimeOffset> LastLooked { get; set; } = new Dictionary<string, DateTimeOffset>();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(HistoryFile))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
