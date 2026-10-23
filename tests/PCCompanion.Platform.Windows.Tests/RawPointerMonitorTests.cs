using System;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Registers for mouse input in this process only; nothing moves the pointer.
public sealed class RawPointerMonitorTests
{
    [Fact]
    public void AMonitorCanBeCreatedAgainAfterTheLastOneIsDisposed()
    {
        new RawPointerMonitor().Dispose();
        using var monitor = new RawPointerMonitor();

        Assert.Equal(1u, RegisteredDevices());
    }

    [Fact]
    public void MonitorsShareOneRegistrationUntilTheLastIsDisposed()
    {
        using var first = new RawPointerMonitor();
        using var second = new RawPointerMonitor();
        Assert.Equal(1u, RegisteredDevices());

        first.Dispose();
        Assert.Equal(1u, RegisteredDevices());

        second.Dispose();
        second.Dispose();
        Assert.Equal(0u, RegisteredDevices());
    }

    [Fact]
    public void AMonitorOnAnotherThreadIsRefusedWhileOneExists()
    {
        using var monitor = new RawPointerMonitor();

        Exception? refused = null;
        var thread = new Thread(() => refused = Record.Exception(() => new RawPointerMonitor().Dispose()));
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(refused);
        Assert.Equal(1u, RegisteredDevices());
    }

    // How many devices this process gets raw input from.
    private static uint RegisteredDevices()
    {
        uint count = 0;
        _ = GetRegisteredRawInputDevices(0, ref count, (uint)Marshal.SizeOf<RawInputDevice>());
        return count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices(nint devices, ref uint count, uint size);
}
