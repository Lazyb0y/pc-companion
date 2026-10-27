using System;
using System.IO;
using System.Runtime.InteropServices;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own hidden window and sends it the messages Windows would; no drive is
// ejected or connected.
public sealed class DeviceNotificationsTests
{
    private const uint DeviceChange = 0x0219;
    private const uint PowerBroadcast = 0x0218;
    private const nuint DeviceArrival = 0x8000;
    private const nuint ResumeAutomatic = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeBroadcast
    {
        public uint Size;
        public uint DeviceType;
        public uint Reserved;
        public uint UnitMask;
        public ushort Flags;
    }

    [Fact]
    public void AFolderIsWatchedUntilItsRegistrationIsDisposed()
    {
        using var folder = new TemporaryFolder();
        using var window = new MessageWindow(UniqueClassName());
        using var devices = new DeviceNotifications(window);

        var registration = devices.WatchFolder(folder.FullPath, () => { });
        registration.Dispose();

        // Its handle is closed, so the folder can go.
        Directory.Delete(folder.FullPath);
    }

    [Fact]
    public void AFolderThatDoesntExistCantBeWatched()
    {
        using var folder = new TemporaryFolder();
        using var window = new MessageWindow(UniqueClassName());
        using var devices = new DeviceNotifications(window);

        Assert.Throws<System.ComponentModel.Win32Exception>(() => devices.WatchFolder(Path.Combine(folder.FullPath, "Missing"), () => { }));
    }

    [Fact]
    public void AVolumeThatArrivesIsReported()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var devices = new DeviceNotifications(window);
        var arrived = 0;
        devices.DriveArrived += (_, _) => arrived++;

        var volume = Marshal.AllocHGlobal(Marshal.SizeOf<VolumeBroadcast>());
        try
        {
            // Windows sends this message, with the volume's details, rather than posting it.
            Marshal.StructureToPtr(new VolumeBroadcast { Size = (uint)Marshal.SizeOf<VolumeBroadcast>(), DeviceType = 2, UnitMask = 1 << 23 }, volume, false);
            SendMessage(window.Handle, DeviceChange, DeviceArrival, volume);
        }
        finally
        {
            Marshal.FreeHGlobal(volume);
        }

        Assert.Equal(1, arrived);
    }

    [Fact]
    public void WakingFromSleepIsReported()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var devices = new DeviceNotifications(window);
        var resumed = 0;
        devices.Resumed += (_, _) => resumed++;

        window.Post(PowerBroadcast, ResumeAutomatic);
        RunPostedMessages();

        Assert.Equal(1, resumed);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
