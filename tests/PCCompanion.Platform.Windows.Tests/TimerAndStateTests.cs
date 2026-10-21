using System;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test uses its own hidden window and posts messages to that window only; the display and
// the theme are only read.
public sealed class TimerAndStateTests
{
    private const uint PowerBroadcast = 0x0218; // WM_POWERBROADCAST
    private const nuint PowerSettingChange = 0x8013; // PBT_POWERSETTINGCHANGE
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [Fact]
    public void ATimerRepeats()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var timer = new CoalescableTimer(window, TimeSpan.FromMilliseconds(10), TimeSpan.Zero);
        var count = 0;
        timer.Elapsed += (_, _) =>
        {
            if (++count == 3)
            {
                MessageLoop.Quit();
            }
        };

        RunUntilQuitOrTimeout(window);

        Assert.Equal(3, count);
    }

    [Fact]
    public void ADisposedTimerStops()
    {
        using var window = new MessageWindow(UniqueClassName());
        var timer = new CoalescableTimer(window, TimeSpan.FromMilliseconds(10), TimeSpan.Zero);
        var count = 0;
        timer.Elapsed += (_, _) => count++;

        timer.Dispose();
        timer.Dispose();
        RunFor(window, TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(0u, 1, 0)] // off
    [InlineData(1u, 0, 1)] // on
    [InlineData(2u, 0, 1)] // dimmed
    public void TheDisplayStateIsReported(uint state, int offs, int ons)
    {
        using var window = new MessageWindow(UniqueClassName());
        using var monitor = new DisplayStateMonitor(window);
        RunPostedMessages(); // Windows reports the current state first.
        int offCount = 0, onCount = 0;
        monitor.TurnedOff += (_, _) => offCount++;
        monitor.TurnedOn += (_, _) => onCount++;

        PostDisplayState(window, ConsoleDisplayState, state);

        Assert.Equal(offs, offCount);
        Assert.Equal(ons, onCount);
    }

    [Fact]
    public void OtherPowerSettingsAreIgnored()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var monitor = new DisplayStateMonitor(window);
        RunPostedMessages();
        var count = 0;
        monitor.TurnedOff += (_, _) => count++;
        monitor.TurnedOn += (_, _) => count++;

        PostDisplayState(window, Guid.NewGuid(), 0);

        Assert.Equal(0, count);
    }

    [Fact]
    public void TheThemeIsRead()
    {
        _ = WindowsTheme.IsDark();
        _ = WindowsTheme.IsHighContrast();

        Assert.InRange(WindowsTheme.TextColor(), 0u, 0xFFFFFFu);
    }

    // Sends what Windows sends: a POWERBROADCAST_SETTING with the setting's GUID, the data's length and the data.
    private static void PostDisplayState(MessageWindow window, Guid setting, uint state)
    {
        var buffer = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.Copy(setting.ToByteArray(), 0, buffer, 16);
            Marshal.WriteInt32(buffer, 16, 4);
            Marshal.WriteInt32(buffer, 20, (int)state);
            // Windows lets it be sent only, not posted, since it carries a pointer.
            SendMessage(window.Handle, PowerBroadcast, PowerSettingChange, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    // Runs the loop until something on this thread quits it, or for 5 seconds at most.
    private static void RunUntilQuitOrTimeout(MessageWindow window)
    {
        using var timeout = new Timer(_ => window.BeginInvoke(MessageLoop.Quit), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        MessageLoop.Run();
    }
}
