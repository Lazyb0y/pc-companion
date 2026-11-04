using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// Reads and writes what PC Companion recorded about each drive in the list, one file for each in
/// the <c>drive-actions</c> folder of the data folder: the files its imports copied and the copies
/// its backups made. Only the host writes them. Any program running as the user can change them, so
/// they're read as untrusted.
/// </summary>
/// <param name="folder">The folder's full path.</param>
public sealed class DriveRecordsStore(string folder)
{
    /// <summary>The folder's name in the data folder.</summary>
    public const string FolderName = "drive-actions";

    /// <summary>The key of Camera cards' records, which have no ID.</summary>
    public const string CameraCardsKey = "camera-cards";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 64 * 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FolderPath { get; } = folder;

    /// <summary>The key of a drive's records: its ID, or <see cref="CameraCardsKey"/>.</summary>
    public static string KeyOf(DriveEntry drive)
    {
        ArgumentNullException.ThrowIfNull(drive);

        return drive.KnownKind == DriveEntryKind.CameraCards ? CameraCardsKey : drive.Id;
    }

    /// <summary>
    /// Returns the records saved under <paramref name="key"/>, or empty ones when there are none yet.
    /// When the file can't be read, <paramref name="readable"/> is <see langword="false"/> and the
    /// records are empty; the next save replaces the file.
    /// </summary>
    public DriveRecords Load(string key, out bool readable)
    {
        readable = true;
        try
        {
            using var stream = File.OpenRead(PathOf(key));
            if (stream.Length > MaximumFileSize)
            {
                readable = false;
                return new DriveRecords();
            }

            var file = JsonSerializer.Deserialize(stream, DriveRecordsJsonContext.Default.DriveRecordsFile) ?? new DriveRecordsFile();

            // The JSON reader checks nullable annotations on properties but not on items.
            var copies = file.Copies
                .Where(pair => pair.Value is not null)
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, BackupCopy>)pair.Value.Where(copy => copy.Value is not null).ToDictionary(copy => copy.Key, copy => copy.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            return new DriveRecords(file.Imported.Where(imported => imported is not null), copies);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DriveRecords();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            readable = false;
            return new DriveRecords();
        }
    }

    /// <summary>
    /// Saves the records under <paramref name="key"/>, to a temporary file first that then replaces
    /// the file, so a reader never sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(string key, DriveRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var path = PathOf(key);
        Directory.CreateDirectory(FolderPath);
        var temporaryFile = path + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var file = new DriveRecordsFile
            {
                Imported = records.Imported,
                Copies = records.Copies.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(copy => copy.Key, copy => copy.Value)),
            };
            JsonSerializer.Serialize(stream, file, DriveRecordsJsonContext.Default.DriveRecordsFile);
            stream.Flush(flushToDisk: true);
        }

        // The move fails while another process reads the file, which only takes a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryFile, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceAttempts && exception is UnauthorizedAccessException or (IOException and not FileNotFoundException))
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    /// <summary>
    /// Deletes the records of every drive whose key isn't in <paramref name="keys"/>, as when drives
    /// are removed from the list. A file that can't be deleted stays until the next time.
    /// </summary>
    public void DeleteOthers(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        string[] files;
        try
        {
            files = Directory.Exists(FolderPath) ? Directory.GetFiles(FolderPath, "*.json") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var key = Path.GetFileNameWithoutExtension(file);
            if ((key == CameraCardsKey || DriveIdentity.IsId(key)) && !keys.Contains(key, StringComparer.Ordinal))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Tried again the next time the settings change.
                }
            }
        }
    }

    // A key is an ID of 32 hexadecimal digits or Camera cards' key, so it can't leave the folder.
    private string PathOf(string key)
    {
        if (key != CameraCardsKey && !DriveIdentity.IsId(key))
        {
            throw new ArgumentException("Not a key of a drive's records.", nameof(key));
        }

        return Path.Join(FolderPath, key + ".json");
    }
}

/// <summary>The contents of a drive's records file.</summary>
internal sealed record DriveRecordsFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<ImportedFile> Imported { get; set; } = [];

    public IReadOnlyDictionary<string, Dictionary<string, BackupCopy>> Copies { get; set; } = new Dictionary<string, Dictionary<string, BackupCopy>>();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DriveRecordsFile))]
internal sealed partial class DriveRecordsJsonContext : JsonSerializerContext;
