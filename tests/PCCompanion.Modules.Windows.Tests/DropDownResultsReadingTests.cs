using System;
using PCCompanion.Modules.Windows.DropDownApps;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownResultsReadingTests
{
    private static readonly DropDownResult Shown = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), 0, DropDownOutcome.Shown, 1);

    private readonly DropDownResultsReading _reading = new();

    [Fact]
    public void WhileTheFeatureIsOffThePageSaysResultsShowOnceItsOn()
    {
        Assert.True(_reading.Read(isOn: false, null));

        Assert.Equal((DropDownResultsNote.FeatureOff, 0), (_reading.Note, _reading.Results.Count));
    }

    [Fact]
    public void ResultsShowWithoutANote()
    {
        Assert.True(_reading.Read(isOn: true, [Shown]));

        Assert.Equal(DropDownResultsNote.None, _reading.Note);
        Assert.Equal([Shown], _reading.Results);
    }

    [Fact]
    public void WithoutResultsYetThePageSaysNothingWasPressed()
    {
        Assert.True(_reading.Read(isOn: true, []));

        Assert.Equal(DropDownResultsNote.NothingPressed, _reading.Note);
    }

    [Fact]
    public void ReadsWithoutAnAnswerKeepWhatsShownUntilThreeInARow()
    {
        _reading.Read(isOn: true, [Shown]);

        Assert.False(_reading.Read(isOn: true, null));
        Assert.False(_reading.Read(isOn: true, null));
        Assert.Equal((DropDownResultsNote.None, 1), (_reading.Note, _reading.Results.Count));

        Assert.True(_reading.Read(isOn: true, null));
        Assert.Equal((DropDownResultsNote.NotAvailable, 0), (_reading.Note, _reading.Results.Count));
    }

    [Fact]
    public void TheSameResultsAgainChangeNothing()
    {
        _reading.Read(isOn: true, [Shown]);

        Assert.False(_reading.Read(isOn: true, [Shown with { }]));
    }
}
