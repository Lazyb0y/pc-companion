using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window on a thread of its own, and hooks while a rule
// can be used. The tests' rules name a made-up program, so no window is ever matched or placed.
public sealed class WindowRulesFeatureTests : IDisposable
{
    private const string MadeUp = """{ "name": "Nothing", "program": "C:\\PCCompanionTests\\NoSuchApp.exe" }""";

    private readonly RecordingLogger<WindowRulesFeature> _logger = new();
    private readonly string _pipe = SnapshotPipe.ForSession($"PCCompanion.WindowRules.Tests.{Guid.NewGuid():N}");
    private readonly FakeRulesSystem _left = new();
    private readonly WindowRulesFeature _feature;
    private int _recoveries;

    public WindowRulesFeatureTests()
    {
        _feature = new WindowRulesFeature(TimeProvider.System, _logger, new DropDownWindows(), new WindowTabsWindows(), new WindowRequests(), _pipe, () =>
        {
            _recoveries++;
            return _left;
        });
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("windowRules", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void WindowsLeftChangedArePutBackOnceOnOrOff()
    {
        var left = _left.Add(1, new System.Drawing.Rectangle(100, 100, 800, 600));
        left.Topmost = true;
        left.HasButton = false;
        _left.Marks[1] = 1 | 2 | 4;

        _feature.Apply(null);
        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_feature.IsRunning);
        Assert.False(left.Topmost);
        Assert.True(left.HasButton);
        Assert.Empty(_left.Marks);
        Assert.Equal(1, _recoveries);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("put back 1 windows", StringComparison.Ordinal));
    }

    [Fact]
    public void TurningItOnStartsItAndTurningItOffStopsIt()
    {
        _feature.Apply(Section($$"""{ "enabled": true, "rules": [ { "app": {{MadeUp}}, "onTop": true } ] }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void ASectionThatCantBeReadTurnsItOffWithAWarning()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": true, "rules": "all" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyNowGoesThroughThePipeToTheFeaturesThread()
    {
        _feature.Apply(Section($$"""{ "enabled": true, "rules": [ { "app": {{MadeUp}}, "onTop": true } ] }"""));
        var rule = new WindowRule { App = new ListedApp { Name = "Nothing", Program = @"C:\PCCompanionTests\NoSuchApp.exe" }, OnTop = true };

        var answer = await AskUntilServedAsync(new WindowRulesRequest { Rule = rule });

        Assert.NotNull(answer);
        Assert.Equal(0, answer.Windows);
    }

    [Fact]
    public async Task WhileItsOffThePipeIsntServed()
    {
        var answer = await WindowRulesPipe.AskAsync(_pipe, new WindowRulesRequest(), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.Null(answer);
    }

    // The feature's thread serves the pipe once it has started.
    private async Task<WindowRulesAnswer?> AskUntilServedAsync(WindowRulesRequest request)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await WindowRulesPipe.AskAsync(_pipe, request, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken) is { } answer)
            {
                return answer;
            }

            Thread.Sleep(100);
        }

        return null;
    }
}
