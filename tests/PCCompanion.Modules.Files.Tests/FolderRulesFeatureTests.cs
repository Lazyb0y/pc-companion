using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

[Collection(nameof(FolderRulesThreads))]
public sealed class FolderRulesFeatureTests : IDisposable
{
    private const string Downloads = @"D:\Downloads";

    private const string On = """
        {
          "enabled": true,
          "folders": [ { "path": "D:\\Downloads", "rules": [ { "name": "PDFs", "extensions": ["pdf"], "destination": "D:\\Sorted" } ] } ]
        }
        """;

    private readonly FakeFolderRulesSystem _system = new FakeFolderRulesSystem().AddFolder(Downloads).AddFolder(@"D:\Sorted");
    private readonly RecordingLogger<FolderRulesFeature> _logger = new();
    private readonly TemporaryFolder _data = new();
    private readonly FolderRulesFeature _feature;

    public FolderRulesFeatureTests()
    {
        _feature = new FolderRulesFeature(_system, new DataFolder(_data.FullPath), new ManualTimeProvider(), _logger);
    }

    public void Dispose()
    {
        _feature.Dispose();
        _data.Dispose();
    }

    private DateTimeOffset? LastLooked() => new HistoryStore(Path.Combine(_data.FullPath, HistoryStore.FileName)).Load(out _).LastLookedAt(Downloads);

    [Fact]
    public void TheFeatureIsOffWithoutASection()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void TurningItOnWatchesTheFolders()
    {
        _feature.Apply(Section(On));

        Assert.True(_feature.IsRunning);
        Assert.True(_system.WaitUntil(system => system.IsWatching(Downloads)));
    }

    [Fact]
    public void TurningItOffStopsTheWatchesAndForgetsWhenTheFoldersWereLookedAt()
    {
        _feature.Apply(Section(On));
        Assert.True(_system.WaitUntil(system => system.IsWatching(Downloads)));

        _feature.Apply(Section("""{ "enabled": false }"""));
        _feature.Dispose();

        Assert.False(_feature.IsRunning);
        Assert.False(_system.IsWatching(Downloads));
        Assert.Null(LastLooked());
    }

    [Fact]
    public void QuittingKeepsWhenTheFoldersWereLookedAt()
    {
        _feature.Apply(Section(On));
        Assert.True(_system.WaitUntil(system => system.Events.Contains($"list {Downloads}")));

        _feature.Dispose();

        Assert.NotNull(LastLooked());
    }

    [Fact]
    public void WhileTheHostRunsAsAdministratorAfterAUacPromptItPauses()
    {
        _system.IsUacElevated = true;

        _feature.Apply(Section(On));
        _feature.Apply(Section(On));

        Assert.False(_feature.IsRunning);
        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public void ASectionThatCantBeReadTurnsItOff()
    {
        _feature.Apply(Section(On));

        _feature.Apply(Section("""{ "enabled": true, "folders": 5 }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The Folder Rules settings can't be read, so Folder Rules is off."), _logger.Entries);
    }
}
