using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// Reads, writes and deletes <c>input-language-per-app.json</c> in the data folder, which holds the
/// input method each app had last. Only the host writes it. Any program running as the user can
/// change the file, so it's read as untrusted. Call it off the UI thread.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class RememberedLanguagesStore(string path)
{
    /// <summary>The file's name in the data folder.</summary>
    public const string FileName = "input-language-per-app.json";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the apps in the file, the app used last first, or none when there's no file yet.
    /// When the file can't be read, <paramref name="readable"/> is <see langword="false"/> and
    /// there are none; the next save replaces the file. Entries aren't checked here.
    /// </summary>
    public IReadOnlyList<RememberedLanguage?> Load(out bool readable)
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

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), RememberedLanguagesJsonContext.Default.RememberedLanguagesFile)?.Apps ?? [];
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

    /// <summary>
    /// Saves the apps, to a temporary file first that then replaces the file, so a reader never
    /// sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(IReadOnlyList<RememberedLanguage> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new RememberedLanguagesFile { Apps = apps }, RememberedLanguagesJsonContext.Default.RememberedLanguagesFile);
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

    /// <summary>Deletes the file, if there is one.</summary>
    /// <exception cref="IOException">The file couldn't be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be deleted.</exception>
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

/// <summary>The contents of <c>input-language-per-app.json</c>.</summary>
internal sealed record RememberedLanguagesFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<RememberedLanguage?> Apps { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RememberedLanguagesFile))]
internal sealed partial class RememberedLanguagesJsonContext : JsonSerializerContext;
