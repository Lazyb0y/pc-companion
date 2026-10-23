using System;
using PCCompanion.Modules.Input.HotCorners;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class CornerResultsReadingTests
{
    private static readonly CornerResult TaskViewRan = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), ScreenCorner.TopLeft, HotCornerAction.TaskView, CornerResultKind.Ran, 1);

    private readonly CornerResultsReading _reading = new();

    [Fact]
    public void WhileHotCornersIsOffThePageSaysResultsShowOnceItsOn()
    {
        Assert.True(_reading.Read(isOn: false, null));

        Assert.Equal((CornerResultsNote.HotCornersOff, 0), (_reading.Note, _reading.Results.Count));
    }

    [Fact]
    public void ResultsShowWithoutANote()
    {
        Assert.True(_reading.Read(isOn: true, [TaskViewRan]));

        Assert.Equal(CornerResultsNote.None, _reading.Note);
        Assert.Equal([TaskViewRan], _reading.Results);
    }

    [Fact]
    public void WithoutResultsYetThePageSaysNoCornerHasBeenReached()
    {
        Assert.True(_reading.Read(isOn: true, []));

        Assert.Equal(CornerResultsNote.NoCornerReached, _reading.Note);
    }

    [Fact]
    public void ReadsWithoutAnAnswerKeepWhatsShownUntilThreeInARow()
    {
        _reading.Read(isOn: true, [TaskViewRan]);

        Assert.False(_reading.Read(isOn: true, null));
        Assert.False(_reading.Read(isOn: true, null));
        Assert.Equal((CornerResultsNote.None, 1), (_reading.Note, _reading.Results.Count));

        Assert.True(_reading.Read(isOn: true, null));
        Assert.Equal((CornerResultsNote.NotAvailable, 0), (_reading.Note, _reading.Results.Count));

        Assert.False(_reading.Read(isOn: true, null));
    }

    [Fact]
    public void AnAnswerAfterThatShowsTheResultsAgain()
    {
        for (var read = 0; read < CornerResultsReading.MissedReadsBeforeNote; read++)
        {
            _reading.Read(isOn: true, null);
        }

        Assert.True(_reading.Read(isOn: true, [TaskViewRan]));

        Assert.Equal(CornerResultsNote.None, _reading.Note);
        Assert.Equal([TaskViewRan], _reading.Results);
    }

    [Fact]
    public void AnAnswerStartsTheCountOfReadsWithoutOneAgain()
    {
        _reading.Read(isOn: true, null);
        _reading.Read(isOn: true, null);
        _reading.Read(isOn: true, [TaskViewRan]);

        _reading.Read(isOn: true, null);
        _reading.Read(isOn: true, null);

        Assert.Equal(CornerResultsNote.None, _reading.Note);
    }

    [Fact]
    public void TurningHotCornersOffShowsItsNoteAtOnce()
    {
        _reading.Read(isOn: true, [TaskViewRan]);

        Assert.True(_reading.Read(isOn: false, null));

        Assert.Equal((CornerResultsNote.HotCornersOff, 0), (_reading.Note, _reading.Results.Count));
    }

    [Fact]
    public void TheSameResultsAgainChangeNothing()
    {
        _reading.Read(isOn: true, [TaskViewRan]);

        Assert.False(_reading.Read(isOn: true, [TaskViewRan with { }]));
    }
}
