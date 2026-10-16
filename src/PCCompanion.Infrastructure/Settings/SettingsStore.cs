using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using PCCompanion.Core.Settings;

namespace PCCompanion.Infrastructure.Settings;

/// <summary>
/// Reads and writes <c>settings.json</c>. Only the settings app saves; other processes only load.
/// </summary>
public sealed class SettingsStore(AppDataPaths paths)
{
    private const int ReplaceAttempts = 20;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Returns the saved settings, or the defaults when nothing has been saved yet.
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
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
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

        // The swap fails while another process is reading the file, which only takes a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryFile, paths.SettingsFile, overwrite: true);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }
}
