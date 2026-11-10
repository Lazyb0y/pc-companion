using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>What the background was before Dynamic Wallpaper changed it.</summary>
public enum BackgroundKind
{
    /// <summary>A picture, the same on every display or one for each.</summary>
    Picture,

    /// <summary>A solid color: no picture.</summary>
    SolidColor,

    /// <summary>A slideshow.</summary>
    Slideshow,
}

/// <summary>A display's picture, or every display's.</summary>
/// <param name="Display">The display's device path; <see langword="null"/> for every display.</param>
/// <param name="Picture">The picture's full path.</param>
public sealed record DisplayPicture(string? Display, string Picture);

/// <summary>
/// The background Windows had before Dynamic Wallpaper changed it, which turning off puts back.
/// See "Working alongside Windows" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed record RememberedBackground
{
    public BackgroundKind Kind { get; set; }

    /// <summary>Whether Windows spotlight changed it; then <see cref="Pictures"/> has spotlight's last picture.</summary>
    public bool Spotlight { get; set; }

    /// <summary>For a picture: every display's, or each display's.</summary>
    public IReadOnlyList<DisplayPicture> Pictures { get; set; } = [];

    public WallpaperPosition Fit { get; set; } = WallpaperPosition.Fill;

    /// <summary>The background color, as 0xRRGGBB.</summary>
    public uint Color { get; set; }

    /// <summary>For a slideshow: its folder or pictures, all in one folder.</summary>
    public IReadOnlyList<string> Slideshow { get; set; } = [];

    public bool SlideshowShuffle { get; set; }

    /// <summary>How often the slideshow changes, in milliseconds.</summary>
    public long SlideshowInterval { get; set; }

    // Compares the lists item by item, so the same background read twice is equal.
    public bool Equals(RememberedBackground? other) =>
        other is not null
        && Kind == other.Kind
        && Spotlight == other.Spotlight
        && Pictures.SequenceEqual(other.Pictures)
        && Fit == other.Fit
        && Color == other.Color
        && Slideshow.SequenceEqual(other.Slideshow)
        && SlideshowShuffle == other.SlideshowShuffle
        && SlideshowInterval == other.SlideshowInterval;

    public override int GetHashCode() => HashCode.Combine(Kind, Spotlight, Pictures.Count, Fit, Color);
}

/// <summary>Where a folder's pictures are in their shuffled order.</summary>
/// <param name="Folder">The folder's full path.</param>
/// <param name="Pictures">A fingerprint of the folder's pictures, which changes when they do.</param>
/// <param name="Seed">The seed of the order.</param>
/// <param name="Next">The place in the order of the picture that shows next.</param>
public sealed record FolderPlace(string Folder, string Pictures, int Seed, int Next);

/// <summary>
/// What only the host keeps about Dynamic Wallpaper, in <c>dynamic-wallpaper.json</c>: the
/// background to put back, the pictures it set last, whether it stepped aside, and each folder's
/// order.
/// </summary>
public sealed record WallpaperState
{
    public int Version { get; set; } = 1;

    /// <summary>The background before Dynamic Wallpaper changed it; <see langword="null"/> before its first change.</summary>
    public RememberedBackground? Remembered { get; set; }

    /// <summary>The pictures Dynamic Wallpaper set last, by display, to tell its own changes from others'.</summary>
    public IReadOnlyList<DisplayPicture> SetLast { get; set; } = [];

    /// <summary>Whether it stepped aside because someone else changed the background.</summary>
    public bool SteppedAside { get; set; }

    public IReadOnlyList<FolderPlace> Folders { get; set; } = [];

    // Compares the lists item by item, so the same state read twice is equal.
    public bool Equals(WallpaperState? other) =>
        other is not null
        && Version == other.Version
        && Remembered == other.Remembered
        && SetLast.SequenceEqual(other.SetLast)
        && SteppedAside == other.SteppedAside
        && Folders.SequenceEqual(other.Folders);

    public override int GetHashCode() => HashCode.Combine(Version, Remembered, SetLast.Count, SteppedAside, Folders.Count);
}

/// <summary>
/// Reads, writes and deletes <c>dynamic-wallpaper.json</c> in the data folder. Only the host writes
/// it. Any program running as the user can change the file, so it's read as untrusted. Call it off
/// the UI thread.
/// </summary>
/// <param name="path">The file's full path.</param>
public sealed class WallpaperStateStore(string path)
{
    /// <summary>The file's name in the data folder.</summary>
    public const string FileName = "dynamic-wallpaper.json";

    /// <summary>The largest file that's read; a larger one counts as unreadable.</summary>
    public const int MaximumFileSize = 1024 * 1024;

    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the state in the file, or a new one when there's no file yet. When the file can't be
    /// read, <paramref name="readable"/> is <see langword="false"/> and the state is new; the next
    /// save replaces the file. Values aren't checked here.
    /// </summary>
    public WallpaperState Load(out bool readable)
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
                return new WallpaperState();
            }

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), WallpaperStateJsonContext.Default.WallpaperState) ?? new WallpaperState();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new WallpaperState();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            readable = false;
            return new WallpaperState();
        }
    }

    /// <summary>
    /// Saves the state, to a temporary file first that then replaces the file, so a reader never
    /// sees a partly written one.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be written.</exception>
    public void Save(WallpaperState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryFile = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, WallpaperStateJsonContext.Default.WallpaperState);
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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(WallpaperState))]
internal sealed partial class WallpaperStateJsonContext : JsonSerializerContext;
