using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xunit;

namespace PCCompanion.Logging.Tests;

public sealed partial class FileLoggingTests : IDisposable
{
    private const string Profile = @"C:\Users\Alex";

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void WritesEntriesToADailyFile()
    {
        Log(logger => LogStarted(logger, "1.0"));

        var file = Assert.Single(Directory.GetFiles(_folder.FullPath));
        Assert.Matches(@"^test-\d{8}\.log$", Path.GetFileName(file));
        Assert.Contains("[INF] Tests: Started 1.0.", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void RedactsMessagesAndExceptions()
    {
        Log(logger => LogCantRead(
            logger,
            new FileNotFoundException($@"Could not find file '{Profile}\settings.json'."),
            $@"{Profile}\settings.json"));

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_folder.FullPath)));
        Assert.DoesNotContain(Profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"Can't read %USERPROFILE%\settings.json.", text, StringComparison.Ordinal);
        Assert.Contains(@"Could not find file '%USERPROFILE%\settings.json'.", text, StringComparison.Ordinal);
    }

    private void Log(Action<ILogger> write)
    {
        // Disposing the factory flushes and closes the file.
        using var factory = LoggerFactory.Create(builder =>
            builder.AddFileLog(_folder.FullPath, "test", new PathRedactor(Profile)));

        write(factory.CreateLogger("Tests"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started {Version}.")]
    private static partial void LogStarted(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't read {Path}.")]
    private static partial void LogCantRead(ILogger logger, Exception exception, string path);
}
