using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UpdateCheckStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly RecordingLogger<UpdateCheckStore> _logger = new();
    private readonly AppDataPaths _paths;
    private readonly UpdateCheckStore _store;

    public UpdateCheckStoreTests()
    {
        // A data folder that does not exist yet, as on first run.
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new UpdateCheckStore(_paths, _logger);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void LoadReturnsNoCheckWhenNothingIsSaved()
    {
        Assert.Equal(new UpdateCheckState(), _store.Load());
    }

    [Fact]
    public void SaveThenLoadReturnsTheSameResult()
    {
        var state = new UpdateCheckState
        {
            LastCheck = new DateTimeOffset(2026, 10, 20, 9, 14, 0, TimeSpan.Zero),
            LatestVersion = new ReleaseVersion(0, 2, 0),
            NotifiedVersion = new ReleaseVersion(0, 1, 5),
        };

        _store.Save(state);

        Assert.Equal(state, _store.Load());
    }

    [Fact]
    public void SaveWritesTheVersionsAsText()
    {
        _store.Save(new UpdateCheckState
        {
            LastCheck = new DateTimeOffset(2026, 10, 20, 9, 14, 0, TimeSpan.Zero),
            LatestVersion = new ReleaseVersion(0, 2, 0),
        });

        var json = File.ReadAllText(_paths.UpdateCheckFile);
        Assert.Contains("\"lastCheck\": \"2026-10-20T09:14:00+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"latestVersion\": \"0.2.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"notifiedVersion\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveReplacesTheFileAndLeavesNoTemporaryFile()
    {
        _store.Save(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 2, 0) });
        _store.Save(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 3, 0) });

        Assert.Equal(new ReleaseVersion(0, 3, 0), _store.Load().LatestVersion);
        Assert.Equal(["update-check.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "latestVersion": "v0.2.0" }""")]
    [InlineData("""{ "latestVersion": "0.2" }""")]
    [InlineData("""{ "latestVersion": 2 }""")]
    [InlineData("""{ "lastCheck": "yesterday" }""")]
    public void AnUnreadableFileCountsAsNoCheck(string json)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.UpdateCheckFile, json);

        Assert.Equal(new UpdateCheckState(), _store.Load());
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileOverTheLimitCountsAsNoCheck()
    {
        Directory.CreateDirectory(_paths.Root);
        var json = """{ "latestVersion": "0.2.0" }""";
        File.WriteAllText(_paths.UpdateCheckFile, json.PadRight(UpdateCheckStore.MaximumFileSize + 1));

        Assert.Equal(new UpdateCheckState(), _store.Load());

        File.WriteAllText(_paths.UpdateCheckFile, json.PadRight(UpdateCheckStore.MaximumFileSize));

        Assert.Equal(new ReleaseVersion(0, 2, 0), _store.Load().LatestVersion);
    }

    [Fact]
    public void AnUnreadableFileIsReplacedAtTheNextSave()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.UpdateCheckFile, "not json");

        _store.Save(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 2, 0) });

        Assert.Equal(new ReleaseVersion(0, 2, 0), _store.Load().LatestVersion);
    }
}
