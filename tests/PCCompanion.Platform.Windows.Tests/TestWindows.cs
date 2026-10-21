using System;
using System.Threading;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Helpers for tests that create windows and run the message loop on the test's thread. Import it
/// with <c>using static PCCompanion.Platform.Windows.Tests.TestWindows;</c>.
/// </summary>
internal static class TestWindows
{
    /// <summary>A window class name that no other test uses.</summary>
    public static string UniqueClassName() => "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    /// <summary>Delivers the messages posted to this thread so far: those posted before Quit come before the loop ends.</summary>
    public static void RunPostedMessages()
    {
        MessageLoop.Quit();
        MessageLoop.Run();
    }

    /// <summary>Runs this thread's message loop for <paramref name="time"/>.</summary>
    public static void RunFor(MessageWindow window, TimeSpan time)
    {
        using var end = new Timer(_ => window.BeginInvoke(MessageLoop.Quit), null, time, Timeout.InfiniteTimeSpan);
        MessageLoop.Run();
    }
}
