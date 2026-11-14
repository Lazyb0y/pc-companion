using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PCCompanion.RemoteFolders;

/// <summary>What a change in the journal does on the server.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JournalKind>))]
public enum JournalKind
{
    Rename,
    Delete,
}

/// <summary>A file the PC knew in a folder it deleted, with the version it knew.</summary>
public sealed record JournalFile(string Path, long Size, DateTime LastWriteTimeUtc);

/// <summary>
/// A rename, move or delete made on the PC, with paths below the server's folder. A delete keeps
/// the version of the file the PC knew, or of each file in a folder, so a file that changed on
/// the server meanwhile isn't deleted.
/// </summary>
public sealed record JournalEntry
{
    public JournalKind Kind { get; init; }

    public string Path { get; init; } = string.Empty;

    /// <summary>For a rename: the new path.</summary>
    public string? Target { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>For a file's delete: the version the PC knew; none for a file the PC never had from the server.</summary>
    public long? Size { get; init; }

    public DateTime? LastWriteTimeUtc { get; init; }

    /// <summary>For a folder's delete: the files in it the PC knew.</summary>
    public IReadOnlyList<JournalFile>? Files { get; init; }
}

/// <summary>
/// The renames, moves and deletes made on the PC while the server couldn't be reached, in order,
/// kept in <c>remote-folders\&lt;server ID&gt;.json</c> in the data folder until they're made on the
/// server. See "Uploading" in docs/features/files/remote-folders.md.
/// </summary>
public sealed class ChangeJournal
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly List<JournalEntry> _entries;

    /// <param name="file">The journal's file; a missing or unreadable one starts an empty journal.</param>
    public ChangeJournal(string file)
    {
        _file = file;
        _entries = Load(file, out bool unreadable);
        WasUnreadable = unreadable;
    }

    /// <summary>Whether the journal's file couldn't be read, so its changes were lost.</summary>
    public bool WasUnreadable { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>The first change, or <see langword="null"/> when there's none.</summary>
    public JournalEntry? First
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count == 0 ? null : _entries[0];
            }
        }
    }

    /// <exception cref="IOException">The journal can't be written.</exception>
    public void Add(JournalEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            Save();
        }
    }

    /// <summary>Removes <paramref name="entry"/>, the first change, once it's made on the server or skipped.</summary>
    /// <exception cref="IOException">The journal can't be written.</exception>
    public void Remove(JournalEntry entry)
    {
        lock (_gate)
        {
            if (_entries.Count > 0 && ReferenceEquals(_entries[0], entry))
            {
                _entries.RemoveAt(0);
                Save();
            }
        }
    }

    /// <summary>Removes the journal's file, as when the server is removed.</summary>
    public void Delete()
    {
        lock (_gate)
        {
            _entries.Clear();
            File.Delete(_file);
        }
    }

    private static List<JournalEntry> Load(string file, out bool unreadable)
    {
        unreadable = false;
        try
        {
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize(stream, JournalJsonContext.Default.ListJournalEntry) ?? [];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            unreadable = true;
            return [];
        }
    }

    // Written whole to a temporary file first, so a crash leaves the old journal or the new one.
    private void Save()
    {
        if (_entries.Count == 0)
        {
            File.Delete(_file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        string temporary = _file + ".tmp";
        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, _entries, JournalJsonContext.Default.ListJournalEntry);
        }

        File.Move(temporary, _file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<JournalEntry>))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
