using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Settings;

namespace PCCompanion.Infrastructure.Settings;

/// <summary>
/// Reads and writes <c>settings.json</c>. Only the settings app saves; other processes only load.
/// </summary>
public sealed partial class SettingsStore(AppDataPaths paths, ILogger<SettingsStore> logger)
{
    private const string CorruptFileName = "settings.corrupt.json";
    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    private bool _keepCorruptFile;

    /// <summary>
    /// Returns the saved settings, or the defaults when nothing has been saved yet or the file
    /// can't be read. An unreadable file is left as it is until <see cref="Save"/> moves it aside.
    /// </summary>
    public AppSettings Load()
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(paths.SettingsFile);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppSettings();
        }

        using (stream)
        {
            try
            {
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
            catch (JsonException exception)
            {
                LogUnreadableFile(logger, exception, CorruptFileName);
                _keepCorruptFile = true;
                return new AppSettings();
            }
        }
    }

    /// <summary>
    /// Saves the settings. The file is written to a temporary file first and then swapped in,
    /// so readers never see a partly written file.
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(paths.Root);
        var temporaryFile = paths.SettingsFile + ".tmp";

        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
            stream.Flush(flushToDisk: true);
        }

        if (_keepCorruptFile)
        {
            try
            {
                Replace(paths.SettingsFile, Path.Combine(paths.Root, CorruptFileName));
                LogKeptCorruptFile(logger, CorruptFileName);
            }
            catch (FileNotFoundException)
            {
                // Deleted since it was loaded, so there is nothing to keep.
            }

            _keepCorruptFile = false;
        }

        Replace(temporaryFile, paths.SettingsFile);
    }

    // The move fails while another process is reading the file, which only takes a moment.
    private static void Replace(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceAttempts
                && exception is UnauthorizedAccessException or (IOException and not FileNotFoundException))
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings file can't be read, so the default settings are used. It will be kept as {CorruptFileName} when settings are saved.")]
    private static partial void LogUnreadableFile(ILogger logger, Exception exception, string corruptFileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kept the unreadable settings file as {CorruptFileName}.")]
    private static partial void LogKeptCorruptFile(ILogger logger, string corruptFileName);
}
