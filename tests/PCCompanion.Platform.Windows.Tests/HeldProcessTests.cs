using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Opens this test process only, and never ends any process: ending is checked in Windows Sandbox,
// on test programs the checks start there.
public sealed class HeldProcessTests
{
    private static readonly uint ThisProcess = (uint)Environment.ProcessId;

    [Fact]
    public void HoldingThisProcessReadsItsDetails()
    {
        using var held = HeldProcess.Open(ThisProcess, out var errorCode);

        Assert.NotNull(held);
        Assert.Equal(0, errorCode);
        Assert.Equal(ThisProcess, held.Id);
        Assert.Equal(Environment.ProcessPath, held.ProgramPath, StringComparer.OrdinalIgnoreCase);
        using var process = Process.GetCurrentProcess();
        Assert.Equal(process.StartTime.ToUniversalTime(), held.StartTime);
        Assert.False(held.IsCritical);
    }

    [Fact]
    public void ARunningProcessHasNotExited()
    {
        using var held = HeldProcess.Open(ThisProcess, out _);

        Assert.NotNull(held);
        Assert.False(held.HasExited);
        Assert.False(held.WaitForExit(TimeSpan.Zero));
    }

    [Fact]
    public void AProcessThatDoesntExistIsNotHeld()
    {
        var unused = Enumerable.Range(1, 1000).Select(i => (uint)(i * 4)).Except(ProcessList.List().Select(p => p.Id)).Last();

        using var held = HeldProcess.Open(unused, out var errorCode);

        Assert.Null(held);
        Assert.NotEqual(0, errorCode);
    }
}
