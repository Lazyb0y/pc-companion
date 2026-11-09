using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads this test process only.
public sealed class ProcessListTests
{
    private static readonly uint ThisProcess = (uint)Environment.ProcessId;

    [Fact]
    public void ListsThisProcessWithAnotherAsItsParent()
    {
        var entry = Assert.Single(ProcessList.List(), p => p.Id == ThisProcess);

        Assert.NotEqual(ThisProcess, entry.ParentId);
    }

    [Fact]
    public void TheStartTimeIsThisProcessesStartTime()
    {
        using var process = Process.GetCurrentProcess();

        Assert.Equal(process.StartTime.ToUniversalTime(), ProcessList.StartTime(ThisProcess));
    }

    [Fact]
    public void AProcessThatDoesntExistHasNoStartTime()
    {
        var unused = Enumerable.Range(1, 1000).Select(i => (uint)(i * 4)).Except(ProcessList.List().Select(p => p.Id)).Last();

        Assert.Null(ProcessList.StartTime(unused));
    }
}
