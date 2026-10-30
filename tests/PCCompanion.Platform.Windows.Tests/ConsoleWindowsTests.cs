using System;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Uses only a hidden window of its own; the Sandbox checks look up real console windows.
public sealed class ConsoleWindowsTests
{
    [Fact]
    public void AnotherWindowIsNoConsole()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.False(ConsoleWindows.IsConsole(window.Handle));
    }

    [Fact]
    public void AWindowNoChildProcessOwnsHasNoConsoleHostThread()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.Null(ConsoleWindows.HostThreadOf(window.Handle, (uint)Environment.ProcessId));
    }
}
