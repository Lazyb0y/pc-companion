using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Modules.Input.Notice;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

// Starting the feature creates its own hidden windows; it reads and asks through a fake system, so
// no app is switched, and keeps its file in a folder of its own under the temp folder.
[Collection(nameof(NoticeWindows))]
public sealed class InputLanguagePerAppFeatureTests : IDisposable
{
    private readonly RecordingLogger<InputLanguagePerAppFeature> _logger = new();
    private readonly SharedNotice _notice = new(TimeProvider.System);
    private readonly TemporaryFolder _folder = new();
    private readonly FakeInputLanguageSystem _system = new();
    private readonly InputLanguagePerAppFeature _feature;

    public InputLanguagePerAppFeatureTests()
    {
        _feature = Create(isUacElevated: false);
    }

    private string FilePath => Path.Combine(_folder.FullPath, RememberedLanguagesStore.FileName);

    public void Dispose()
    {
        _feature.Dispose();
        _notice.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void TheFeatureIsInTheInputModule()
    {
        Assert.Equal(("inputLanguagePerApp", "input"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.Equal(("InputLanguagePerAppName", "InputLanguagePerAppDescription"), (_feature.Info.NameKey, _feature.Info.DescriptionKey));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void TurningTheFeatureOnAndOffStartsAndStopsIt()
    {
        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Keep" }"""));
        Assert.True(_feature.IsRunning);
        Assert.True(_notice.IsOpen);

        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Keep", "showNotice": false }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
        Assert.False(_notice.IsOpen);

        Assert.Equal(
            [
                "Input Language per App started.",
                "Input Language per App applied its settings: 0 listed apps, other apps Keep, notice True.",
                "Input Language per App applied its settings: 0 listed apps, other apps Keep, notice False.",
                "Input Language per App stopped.",
            ],
            _logger.Entries.Select(entry => entry.Message).Where(message => !message.StartsWith("Input Language per App:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSameSettingsAgainChangeNothing()
    {
        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Keep" }"""));
        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Keep" }"""));

        Assert.Single(_logger.Entries, entry => entry.Message.StartsWith("Input Language per App applied its settings", StringComparison.Ordinal));
    }

    [Fact]
    public void ASectionThatCantBeReadTurnsTheFeatureOffWithAWarning()
    {
        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Sometimes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message == "The Input Language per App settings can't be read, so Input Language per App is off.");
    }

    [Fact]
    public void TurningTheFeatureOffDeletesTheRememberedInputMethods()
    {
        WriteFile();

        _feature.Apply(Section("""{ "enabled": true }"""));
        _feature.Apply(Section("""{ "enabled": false }"""));
        _feature.Dispose();

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ChoosingAnotherBehaviorForOtherAppsDeletesTheRememberedInputMethods()
    {
        WriteFile();

        _feature.Apply(Section("""{ "enabled": true, "otherApps": "Use", "otherAppsInputMethod": "0409:00000409" }"""));
        _feature.Dispose();

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void AsAdministratorAfterAUacPromptTheFileIsntTouched()
    {
        WriteFile();
        var elevated = Create(isUacElevated: true);

        elevated.Apply(Section("""{ "enabled": true }"""));
        elevated.Apply(Section("""{ "enabled": false }"""));
        elevated.Dispose();

        Assert.True(File.Exists(FilePath));
    }

    private InputLanguagePerAppFeature Create(bool isUacElevated) =>
        new(_logger, TimeProvider.System, _notice, new DataFolder(_folder.FullPath), isUacElevated, _ => _system);

    private void WriteFile() =>
        File.WriteAllText(FilePath, """{ "version": 1, "apps": [{ "program": "C:\\Apps\\App.exe", "inputMethod": "0407:00000407" }] }""");
}
