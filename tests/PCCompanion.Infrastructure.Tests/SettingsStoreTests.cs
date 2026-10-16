using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Settings;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly RecordingLogger<SettingsStore> _logger = new();
    private readonly AppDataPaths _paths;
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        // A data folder that does not exist yet, as on first run.
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new SettingsStore(_paths, _logger);
    }

    private string CorruptFile => Path.Combine(_paths.Root, "settings.corrupt.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void LoadReturnsDefaultsWhenNothingIsSaved()
    {
        Assert.Equal(new AppSettings(), _store.Load());
    }

    [Fact]
    public void SaveThenLoadReturnsTheSameSettings()
    {
        var settings = new AppSettings { General = new GeneralSettings { Language = "bn", Theme = AppTheme.Dark } };

        _store.Save(settings);

        Assert.Equal(settings, _store.Load());
    }

    [Fact]
    public void SaveWritesReadableJson()
    {
        _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Light } });

        var json = File.ReadAllText(_paths.SettingsFile);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"Light\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveReplacesTheFileAndLeavesNoTemporaryFile()
    {
        _store.Save(new AppSettings());
        _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Dark } });

        Assert.Equal(AppTheme.Dark, _store.Load().General.Theme);
        Assert.Equal([_paths.SettingsFile], Directory.GetFiles(_paths.Root));
    }

    [Fact]
    public async Task SaveWaitsForAReaderToCloseTheFile()
    {
        _store.Save(new AppSettings());

        Task save;
        using (File.OpenRead(_paths.SettingsFile))
        {
            save = Task.Run(() => _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Dark } }), TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        await save;
        Assert.Equal(AppTheme.Dark, _store.Load().General.Theme);
    }

    [Fact]
    public void LoadAcceptsHandEditedFiles()
    {
        WriteSettingsFile("""
            {
              // Comments, trailing commas, unknown and missing properties are fine.
              "unknown": true,
              "general": { "theme": "dark", },
            }
            """);

        var settings = _store.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(new GeneralSettings { Theme = AppTheme.Dark }, settings.General);
    }

    [Fact]
    public void LoadUsesDefaultsForMissingSections()
    {
        WriteSettingsFile("""{ "schemaVersion": 1 }""");

        Assert.Equal(new AppSettings(), _store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "general": { "theme": "Dark" """)]
    [InlineData("""{ "schemaVersion": "one" }""")]
    [InlineData("""{ "general": null }""")]
    [InlineData("""{ "general": { "theme": "Purple" } }""")]
    public void LoadReturnsDefaultsAndLogsAWarningForAnUnreadableFile(string json)
    {
        WriteSettingsFile(json);

        Assert.Equal(new AppSettings(), _store.Load());
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void LoadLeavesAnUnreadableFileAsItIs()
    {
        WriteSettingsFile("not json");

        _store.Load();

        Assert.Equal("not json", File.ReadAllText(_paths.SettingsFile));
        Assert.False(File.Exists(CorruptFile));
    }

    [Fact]
    public void SaveKeepsAnUnreadableFile()
    {
        WriteSettingsFile("not json");
        _store.Load();

        _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Dark } });

        Assert.Equal("not json", File.ReadAllText(CorruptFile));
        Assert.Equal(AppTheme.Dark, _store.Load().General.Theme);
    }

    [Fact]
    public void SaveKeepsAnUnreadableFileOnlyOnce()
    {
        WriteSettingsFile("not json");
        _store.Load();
        _store.Save(new AppSettings());

        _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Dark } });

        Assert.Equal("not json", File.ReadAllText(CorruptFile));
    }

    [Fact]
    public void SaveWorksWhenTheUnreadableFileWasDeleted()
    {
        WriteSettingsFile("not json");
        _store.Load();
        File.Delete(_paths.SettingsFile);

        _store.Save(new AppSettings());

        Assert.False(File.Exists(CorruptFile));
        Assert.Equal(new AppSettings(), _store.Load());
    }

    private void WriteSettingsFile(string json)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SettingsFile, json);
    }
}
