using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Display;

namespace PCCompanion.Logging;

public static class FileLoggingExtensions
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private const long FileSizeLimitBytes = 5 * 1024 * 1024;
    private const int RetainedFileCountLimit = 30;
    private static readonly TimeSpan RetainedFileTimeLimit = TimeSpan.FromDays(14);

    /// <summary>
    /// Writes log entries to <c>{fileName}-yyyyMMdd.log</c> in <paramref name="logsFolder"/>.
    /// A new file starts every day and at 5 MB; files older than 14 days are deleted.
    /// </summary>
    /// <param name="fileName">The start of the file names, for example <c>app</c>.</param>
    /// <param name="redactor">Redacts the text written; by default the current user's profile folder.</param>
    public static ILoggingBuilder AddFileLog(
        this ILoggingBuilder builder,
        string logsFolder,
        string fileName,
        PathRedactor? redactor = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(logsFolder);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var formatter = new RedactingFormatter(
            new MessageTemplateTextFormatter(OutputTemplate, CultureInfo.InvariantCulture),
            redactor ?? PathRedactor.ForCurrentUser());

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                formatter,
                Path.Combine(logsFolder, fileName + "-.log"),
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                retainedFileTimeLimit: RetainedFileTimeLimit)
            .CreateLogger();

        return builder.AddSerilog(logger, dispose: true);
    }
}
