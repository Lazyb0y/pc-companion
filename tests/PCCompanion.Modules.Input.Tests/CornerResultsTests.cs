using System;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class CornerResultsTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly CornerResults _results;

    public CornerResultsTests()
    {
        _results = new CornerResults(_clock);
    }

    [Fact]
    public void TheLastFiveAreKeptNewestFirst()
    {
        foreach (var corner in Enum.GetValues<ScreenCorner>())
        {
            _results.Add(corner, HotCornerAction.TaskView, CornerResultKind.Ran);
            _results.Add(corner, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        }

        Assert.Equal(
            [
                (ScreenCorner.BottomRight, CornerResultKind.LeftEarly),
                (ScreenCorner.BottomRight, CornerResultKind.Ran),
                (ScreenCorner.BottomLeft, CornerResultKind.LeftEarly),
                (ScreenCorner.BottomLeft, CornerResultKind.Ran),
                (ScreenCorner.TopRight, CornerResultKind.LeftEarly),
            ],
            _results.List().Select(result => (result.Corner, result.Kind)));
    }

    [Fact]
    public void TheSameResultInARowIsCountedWithItsLatestTime()
    {
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        _clock.Advance(TimeSpan.FromSeconds(3));
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.LeftEarly);

        var result = Assert.Single(_results.List());
        Assert.Equal((3, _clock.GetUtcNow()), (result.Count, result.Time));
    }

    [Fact]
    public void AnotherResultInBetweenStartsAgain()
    {
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.Ran);
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        _results.Add(ScreenCorner.TopRight, HotCornerAction.TaskView, CornerResultKind.LeftEarly);
        _results.Add(ScreenCorner.TopRight, HotCornerAction.Start, CornerResultKind.LeftEarly);

        Assert.Equal([1, 1, 1, 1, 1], _results.List().Select(result => result.Count));
    }

    [Fact]
    public void ClearingRemovesTheResults()
    {
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.Ran);

        _results.Clear();

        Assert.Empty(_results.List());
    }

    [Fact]
    public void ThePipesJsonGivesTheSameResults()
    {
        _results.Add(ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.FullScreenApp);
        _results.Add(ScreenCorner.BottomRight, HotCornerAction.RunProgram, CornerResultKind.ProgramNotFound);

        var json = _results.ToJson();

        Assert.Equal(_results.List(), CornerResults.FromJson(json));
        Assert.Contains("\"kind\":\"ProgramNotFound\"", Encoding.UTF8.GetString(json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""[{ "time": "2026-10-23T10:00:00+00:00", "corner": "Middle", "action": "TaskView", "kind": "Ran", "count": 1 }]""")]
    [InlineData("{}")]
    public void JsonThatIsntResultsGivesNone(string json)
    {
        Assert.Null(CornerResults.FromJson(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void OnlyTheFirstFiveResultsAreRead()
    {
        const string Result = """{ "time": "2026-10-23T10:00:00+00:00", "corner": "TopLeft", "action": "TaskView", "kind": "Ran", "count": 1 }""";
        var json = $"[{string.Join(',', Enumerable.Repeat(Result, 8))}]";

        Assert.Equal(5, CornerResults.FromJson(Encoding.UTF8.GetBytes(json))!.Count);
    }

    [Fact]
    public void EachSkippedOutcomeHasItsKind()
    {
        CornerOutcome[] skipped = [CornerOutcome.LeftEarly, CornerOutcome.MouseButtonHeld, CornerOutcome.KeyNotHeld, CornerOutcome.PointerConfined, CornerOutcome.PointerHidden, CornerOutcome.FullScreenApp, CornerOutcome.AppExcluded];

        Assert.Equal(skipped.Select(outcome => outcome.ToString()), skipped.Select(outcome => CornerResults.KindOf(outcome).ToString()));
    }

    [Fact]
    public void AResultReadsAsTheCornerItsActionAndWhatHappened()
    {
        using var language = new Language("en-US");

        Assert.Equal(
            "Top left, Task View: skipped because an app was full screen",
            CornerResultText.Describe(new CornerResult(_clock.GetUtcNow(), ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.FullScreenApp, 1)));
        Assert.Equal(
            "Bottom right, Run a program: didn't start the program, because it's missing",
            CornerResultText.Describe(new CornerResult(_clock.GetUtcNow(), ScreenCorner.BottomRight, HotCornerAction.RunProgram, CornerResultKind.ProgramNotFound, 1)));
    }

    [Fact]
    public void EveryKindHasItsOwnText()
    {
        using var language = new Language("en-US");

        var texts = Enum.GetValues<CornerResultKind>()
            .Select(kind => CornerResultText.Describe(new CornerResult(_clock.GetUtcNow(), ScreenCorner.TopLeft, HotCornerAction.TaskView, kind, 1)))
            .ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void AResultsTimeIsInTheUsersFormatWithHowManyTimes()
    {
        using var language = new Language("en-US");
        var time = new DateTimeOffset(2026, 10, 23, 12, 41, 5, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 23, 12, 41, 5, DateTimeKind.Local)));

        Assert.Equal("12:41:05 PM", CornerResultText.When(new CornerResult(time, ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.Ran, 1)));
        Assert.Equal("12:41:05 PM (3 times)", CornerResultText.When(new CornerResult(time, ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.Ran, 3)));
    }

    // Sets the user interface language and the regional format on this thread, until disposed.
    private sealed class Language : IDisposable
    {
        private readonly CultureInfo _previousLanguage = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _previousFormat = CultureInfo.CurrentCulture;

        public Language(string name)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _previousLanguage;
            CultureInfo.CurrentCulture = _previousFormat;
        }
    }
}
