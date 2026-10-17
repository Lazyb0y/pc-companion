using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Registers for mouse input in this process only; nothing moves the pointer.
public sealed class RawPointerMonitorTests
{
    [Fact]
    public void AMonitorCanBeCreatedAgainAfterTheLastOneIsDisposed()
    {
        using var window = new MessageWindow("PCCompanion.Tests." + Guid.NewGuid().ToString("N"));

        new RawPointerMonitor(window).Dispose();
        using var monitor = new RawPointerMonitor(window);
    }
}
