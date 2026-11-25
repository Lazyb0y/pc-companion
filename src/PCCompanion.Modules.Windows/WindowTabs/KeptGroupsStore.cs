using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Reads, writes and deletes <c>window-tabs.json</c> in the data folder, which holds the groups
/// Keep groups after a restart keeps. Only the host writes it. Any program running as the user can
/// change the file, so it's read as untrusted. Call it off the feature's thread.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class KeptGroupsStore(string path)
{
    /// <summary>The file's name in the data folder.</summary>
    public const string FileName = "window-tabs.json";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the groups in the file, the group changed last first, or none when there's no file
    /// yet. When the file can't be read, <paramref name="readable"/> is <see langword="false"/>
    /// and there are none; the next save replaces the file. Entries aren't checked here.
    /// </summary>
    public IReadOnlyList<KeptGroup?> Load(out bool readable)
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

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), KeptGroupsJsonContext.Default.KeptGroupsFile)?.Groups ?? [];
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
    /// Saves the groups, to a temporary file first that then replaces the file, so a reader never
    /// sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(IReadOnlyList<KeptGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new KeptGroupsFile { Groups = groups }, KeptGroupsJsonContext.Default.KeptGroupsFile);
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

/// <summary>A group as <c>window-tabs.json</c> keeps it: its tabs' apps in the strip's order, and which tab was in front.</summary>
public sealed record KeptGroup
{
    public IReadOnlyList<KeptTab?> Tabs { get; set; } = [];

    /// <summary>The tab in front, counted from 0.</summary>
    public int Front { get; set; }
}

/// <summary>
/// A tab's app as <c>window-tabs.json</c> keeps it, as the taskbar groups apps: by app ID,
/// otherwise by program. Never a title.
/// </summary>
public sealed record KeptTab
{
    /// <summary>The app ID (AppUserModelID) the app's window or its Store package has.</summary>
    public string? AppId { get; set; }

    /// <summary>The full path of the app's program file, for an app without an app ID.</summary>
    public string? Program { get; set; }

    /// <summary>
    /// The app of a window as it's kept: its app ID, otherwise its program, or <see langword="null"/>
    /// when it has neither.
    /// </summary>
    public static KeptTab? Of(AppOfWindow app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.AppId is { } appId ? new KeptTab { AppId = appId }
            : app.Program is { } program ? new KeptTab { Program = program }
            : null;
    }

    /// <summary>A tab read from the file, or <see langword="null"/> when it has neither or both an app ID and a program.</summary>
    public static KeptTab? Checked(KeptTab? tab)
    {
        var appId = string.IsNullOrWhiteSpace(tab?.AppId) ? null : tab.AppId;
        var program = string.IsNullOrWhiteSpace(tab?.Program) ? null : tab.Program;
        return (appId is null) == (program is null) ? null : new KeptTab { AppId = appId, Program = program };
    }

    /// <summary>Whether a window is of this app, as the taskbar groups apps, whatever their case.</summary>
    public bool Matches(AppOfWindow app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.AppId is { } appId
            ? string.Equals(AppId, appId, StringComparison.OrdinalIgnoreCase)
            : Program is not null && string.Equals(Program, app.Program, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The contents of <c>window-tabs.json</c>.</summary>
internal sealed record KeptGroupsFile
{
    public int Version { get; set; } = 1;

    public IReadOnlyList<KeptGroup?> Groups { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(KeptGroupsFile))]
internal sealed partial class KeptGroupsJsonContext : JsonSerializerContext;
