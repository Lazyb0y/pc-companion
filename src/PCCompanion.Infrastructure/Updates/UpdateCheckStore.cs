using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Settings;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// Reads and writes <c>update-check.json</c>. Any program running as the user can change the
/// file, so it's read as untrusted. It's in the user's folder, so a process elevated through UAC
/// mustn't save it: a link planted there could redirect the write.
/// </summary>
public sealed partial class UpdateCheckStore(AppDataPaths paths, ILogger<UpdateCheckStore> logger)
{
    /// <summary>The largest file that's read; a larger one counts as no check yet.</summary>
    public const int MaximumFileSize = 4096;

    /// <summary>
    /// Returns the saved result, or an empty one when there's none yet or the file is too large
    /// or can't be read.
    /// </summary>
    public UpdateCheckState Load()
    {
        try
        {
            using var stream = File.OpenRead(paths.UpdateCheckFile);
            var buffer = new byte[MaximumFileSize + 1];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (length > MaximumFileSize)
            {
                LogUnreadable(logger, null);
                return new UpdateCheckState();
            }

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), UpdateJsonContext.Default.UpdateCheckState) ?? new UpdateCheckState();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new UpdateCheckState();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogUnreadable(logger, exception);
            return new UpdateCheckState();
        }
    }

    /// <summary>
    /// Saves the result, to a temporary file first that then replaces the file, so readers never
    /// see a partly written one.
    /// </summary>
    public void Save(UpdateCheckState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(paths.Root);
        var temporaryFile = paths.UpdateCheckFile + ".tmp";

        using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, UpdateJsonContext.Default.UpdateCheckState);
            stream.Flush(flushToDisk: true);
        }

        SettingsStore.Replace(temporaryFile, paths.UpdateCheckFile);
        LogSaved(logger);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The update check's result can't be read, so it counts as no check yet.")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved the update check's result.")]
    private static partial void LogSaved(ILogger logger);
}
