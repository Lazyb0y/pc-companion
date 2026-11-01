using System;
using System.Linq;
using System.Text;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownResultsTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly DropDownResults _results;

    public DropDownResultsTests()
    {
        _results = new DropDownResults(_clock);
    }

    [Fact]
    public void ResultsAreListedNewestFirst()
    {
        _results.Add(0, DropDownOutcome.Shown);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _results.Add(1, DropDownOutcome.RunsAsAdministrator);

        Assert.Equal(
            [
                new DropDownResult(_clock.Now, 1, DropDownOutcome.RunsAsAdministrator, 1),
                new DropDownResult(_clock.Now - TimeSpan.FromSeconds(1), 0, DropDownOutcome.Shown, 1),
            ],
            _results.List());
    }

    [Fact]
    public void TheSameResultAgainIsCountedWithItsLatestTime()
    {
        _results.Add(0, DropDownOutcome.DialogOpen);
        _clock.Advance(TimeSpan.FromSeconds(3));
        _results.Add(0, DropDownOutcome.DialogOpen);

        Assert.Equal([new DropDownResult(_clock.Now, 0, DropDownOutcome.DialogOpen, 2)], _results.List());
    }

    [Fact]
    public void OnlyTheLastFiveAreKept()
    {
        for (var index = 0; index < 7; index++)
        {
            _results.Add(index, DropDownOutcome.Shown);
        }

        Assert.Equal([6, 5, 4, 3, 2], _results.List().Select(result => result.AppIndex));
    }

    [Fact]
    public void ClearingRemovesThemAll()
    {
        _results.Add(0, DropDownOutcome.Shown);

        _results.Clear();

        Assert.Empty(_results.List());
    }

    [Fact]
    public void ThePipesJsonReadsBackAndNamesOnlyThePlaceInTheList()
    {
        _results.Add(2, DropDownOutcome.ShortcutTaken);
        _results.Add(0, DropDownOutcome.NoWindowInTime);

        var json = _results.ToJson();

        Assert.Equal(_results.List(), DropDownResults.FromJson(json));
        Assert.Contains("\"appIndex\":2", Encoding.UTF8.GetString(json), StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"ShortcutTaken\"", Encoding.UTF8.GetString(json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "appIndex": 1 }""")]
    public void WhatIsntResultsIsNotRead(string json)
    {
        Assert.Null(DropDownResults.FromJson(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void AtMostFiveResultsAreRead()
    {
        var many = string.Join(',', Enumerable.Range(0, 8).Select(index => $$"""{ "time": "2026-11-01T09:00:00Z", "appIndex": {{index}}, "outcome": "Shown", "count": 1 }"""));

        Assert.Equal(5, DropDownResults.FromJson(Encoding.UTF8.GetBytes($"[{many}]"))!.Count);
    }
}
