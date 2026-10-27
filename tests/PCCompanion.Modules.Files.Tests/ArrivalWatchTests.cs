using System;
using System.Collections.Generic;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class ArrivalWatchTests
{
    private const string Report = @"D:\Downloads\report.pdf";

    private static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeProbe _probe = new();
    private readonly ArrivalWatch _watch;

    public ArrivalWatchTests()
    {
        _watch = new ArrivalWatch(_probe);
    }

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    [Fact]
    public void AFileThatStaysTheSameAndIsntInUseArrives()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _watch.Notice(Report, At(0));

        Assert.Equal(At(2), _watch.NextDue);
        Assert.Empty(_watch.Check(At(1.9)));
        Assert.Equal([new Arrival(Report, ArrivalOutcome.Arrived)], _watch.Check(At(2)));
        Assert.Null(_watch.NextDue);
    }

    [Fact]
    public void EachChangeStartsTheWaitAgain()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _watch.Notice(Report, At(0));
        _watch.Notice(Report, At(1.5));

        Assert.Empty(_watch.Check(At(2)));
        Assert.Equal(At(3.5), _watch.NextDue);
        Assert.Single(_watch.Check(At(3.5)));
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(100, 1)]
    public void AFileWhoseSizeOrTimeChangedWaitsAgain(long size, double modifiedLater)
    {
        _probe.Files[Report] = new FileState(100, Start);
        _watch.Notice(Report, At(0));
        _probe.Files[Report] = new FileState(size, Start.AddSeconds(modifiedLater));

        Assert.Empty(_watch.Check(At(2)));
        Assert.Equal(At(4), _watch.NextDue);
        Assert.Single(_watch.Check(At(4)));
    }

    [Theory]
    [InlineData(@"D:\Downloads\report.pdf.crdownload")]
    [InlineData(@"D:\Downloads\Unconfirmed 123456.crdownload")]
    [InlineData(@"D:\Downloads\Xy1Abc.report.pdf.part")]
    [InlineData(@"D:\Downloads\report.pdf.partial")]
    [InlineData(@"D:\Downloads\report.pdf.download")]
    [InlineData(@"D:\Downloads\report.pdf.opdownload")]
    [InlineData(@"D:\Downloads\8a5c0f1e-3b9d-4c2a-9e7f-0d6b1a2c3e4f.TMP")]
    [InlineData(@"D:\Downloads\~$budget.xlsx")]
    public void FilesStillBeingWrittenAreSkippedByTheirNames(string path)
    {
        _probe.Files[path] = new FileState(100, Start);
        _watch.Notice(path, At(0));

        Assert.Equal(0, _watch.Count);
        Assert.Empty(_watch.Check(At(10)));
    }

    [Fact]
    public void AFileThatsGoneIsDropped()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _watch.Notice(Report, At(0));
        _probe.Files.Remove(Report);

        Assert.Empty(_watch.Check(At(2)));
        Assert.Equal(0, _watch.Count);
    }

    [Fact]
    public void AFileThatsGoneWhenNoticedIsntWaitedFor()
    {
        _watch.Notice(Report, At(0));

        Assert.Equal(0, _watch.Count);
    }

    [Fact]
    public void AnEmptyFileIsDroppedUntilItsWritten()
    {
        _probe.Files[Report] = new FileState(0, Start);
        _watch.Notice(Report, At(0));

        Assert.Empty(_watch.Check(At(2)));
        Assert.Equal(0, _watch.Count);

        _probe.Files[Report] = new FileState(100, Start.AddSeconds(5));
        _watch.Notice(Report, At(5));

        Assert.Single(_watch.Check(At(7)));
    }

    [Fact]
    public void AFileInUseIsTriedAgainLaterWithLongerWaits()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _probe.Busy.Add(Report);
        _watch.Notice(Report, At(0));

        var waits = new List<double>();
        var now = At(2);
        for (var i = 0; i < 7; i++)
        {
            Assert.Empty(_watch.Check(now));
            var next = _watch.NextDue!.Value;
            waits.Add((next - now).TotalSeconds);
            now = next;
        }

        Assert.Equal([2, 4, 8, 16, 30, 30, 30], waits);

        _probe.Busy.Remove(Report);
        Assert.Equal([new Arrival(Report, ArrivalOutcome.Arrived)], _watch.Check(now));
    }

    [Fact]
    public void AFileInUseForFiveMinutesIsLeftWhereItIs()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _probe.Busy.Add(Report);
        _watch.Notice(Report, At(0));

        DateTimeOffset? leftAt = null;
        while (_watch.NextDue is { } due)
        {
            if (_watch.Check(due) is [var result])
            {
                Assert.Equal(new Arrival(Report, ArrivalOutcome.InUse), result);
                leftAt = due;
            }
        }

        // The first try to open it failed 2 seconds after it was noticed.
        Assert.InRange(leftAt!.Value, At(2) + ArrivalWatch.InUseTime, At(2) + ArrivalWatch.InUseTime + ArrivalWatch.LongestRetryWait);
    }

    [Fact]
    public void AChangeWhileInUseStartsOver()
    {
        _probe.Files[Report] = new FileState(100, Start);
        _probe.Busy.Add(Report);
        _watch.Notice(Report, At(0));
        Assert.Empty(_watch.Check(At(2)));

        _watch.Notice(Report, At(3));

        Assert.Equal(At(5), _watch.NextDue);
    }

    [Fact]
    public void ForgettingAFolderDropsOnlyItsFiles()
    {
        const string Scan = @"D:\Scans\scan.pdf";
        _probe.Files[Report] = new FileState(100, Start);
        _probe.Files[Scan] = new FileState(100, Start);
        _watch.Notice(Report, At(0));
        _watch.Notice(Scan, At(0));

        _watch.ForgetFolder(@"d:\downloads");

        Assert.Equal([new Arrival(Scan, ArrivalOutcome.Arrived)], _watch.Check(At(2)));
    }

    private sealed class FakeProbe : IArrivalProbe
    {
        public Dictionary<string, FileState> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Busy { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FileState? Read(string path) => Files.TryGetValue(path, out var state) ? state : null;

        public bool CanOpenAlone(string path) => !Busy.Contains(path);
    }
}
