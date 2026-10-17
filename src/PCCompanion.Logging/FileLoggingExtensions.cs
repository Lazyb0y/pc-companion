using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace PCCompanion.Logging;

public static class FileLoggingExtensions
{
    /// <summary>How many days log files are kept.</summary>
    public const int RetentionDays = 14;

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private const long FileSizeLimitBytes = 5 * 1024 * 1024;
    private const int RetainedFileCountLimit = 30;
    private static readonly TimeSpan RetainedFileTimeLimit = TimeSpan.FromDays(RetentionDays);

    /// <summary>
    /// Writes log entries to <c>{fileName}-yyyyMMdd.log</c> in <paramref name="logsFolder"/>.
    /// A new file starts every day and at 5 MB; files older than <see cref="RetentionDays"/> days are deleted.
    /// </summary>
    /// <param name="fileName">The start of the file names, for example <c>app</c>.</param>
    /// <param name="redactor">Redacts the text written; by default the current user's profile folder.</param>
    /// <param name="detailedLogging">Turns Debug entries on and off; without it, only Information and above are written.</param>
    public static ILoggingBuilder AddFileLog(
        this ILoggingBuilder builder,
        string logsFolder,
        string fileName,
        PathRedactor? redactor = null,
        DetailedLogging? detailedLogging = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(logsFolder);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var formatter = new RedactingFormatter(
            new MessageTemplateTextFormatter(OutputTemplate, CultureInfo.InvariantCulture),
            redactor ?? PathRedactor.ForCurrentUser());

        var logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(detailedLogging?.LevelSwitch ?? new LoggingLevelSwitch(LogEventLevel.Information))
            .WriteTo.File(
                formatter,
                Path.Combine(logsFolder, fileName + "-.log"),
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                retainedFileTimeLimit: RetainedFileTimeLimit)
            .CreateLogger();

        if (detailedLogging is not null)
        {
            var changes = logger.ForContext(Constants.SourceContextPropertyName, typeof(DetailedLogging).FullName);
            detailedLogging.Changed += (_, _) =>
            {
                if (detailedLogging.Until is { } until)
                {
                    changes.Information("Detailed logging is on until {Until}.", until);
                }
                else
                {
                    changes.Information("Detailed logging is off.");
                }
            };
        }

        // Debug entries pass this filter and reach the file log while detailed logging is on.
        return builder.SetMinimumLevel(LogLevel.Debug).AddSerilog(logger, dispose: true);
    }
}
