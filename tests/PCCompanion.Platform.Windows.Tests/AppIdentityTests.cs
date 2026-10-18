using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the identity of this test process and its own hidden window only.
public sealed class AppIdentityTests
{
    private static readonly uint ThisProcess = (uint)Environment.ProcessId;

    [Fact]
    public void ProgramPathIsThisProcessesProgramFile()
    {
        Assert.Equal(Environment.ProcessPath, AppIdentity.ProgramPath(ThisProcess), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThisProcessIsNotAStoreApp()
    {
        Assert.Null(AppIdentity.OfPackagedProcess(ThisProcess));
    }

    [Fact]
    public void AWindowThatSetsNoAppIdHasNone()
    {
        using var window = new MessageWindow("PCCompanion.Tests." + Guid.NewGuid().ToString("N"));

        Assert.Null(AppIdentity.OfWindow(window.Handle));
    }

    [Fact]
    public void AProcessThatCanNotBeOpenedHasNoIdentity()
    {
        // Process 0 is the System Idle Process, which no program can open.
        Assert.Null(AppIdentity.ProgramPath(0));
        Assert.Null(AppIdentity.OfPackagedProcess(0));
    }
}
