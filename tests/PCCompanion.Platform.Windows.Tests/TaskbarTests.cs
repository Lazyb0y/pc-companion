using System;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the taskbar, when there is one, without changing it, and lists the UI Automation elements
// of windows the tests create.
public sealed class TaskbarTests
{
    [Fact]
    public void TheTaskbarIsReadWhenItRuns()
    {
        var taskbar = Taskbar.Read();

        if (taskbar is not null)
        {
            Assert.NotEqual(0, taskbar.Handle);
            Assert.NotEqual(0u, taskbar.ProcessId);
            Assert.True(taskbar.Bounds.Width > 0 && taskbar.Bounds.Height > 0);
            Assert.InRange(taskbar.Dpi, 96u, 960u);
        }
    }

    [Fact]
    public void ALayoutReaderListsAWindowsElements()
    {
        // UI Automation asks the window's thread, so the window runs on a thread of its own.
        using var ready = new ManualResetEventSlim();
        nint handle = 0;
        MessageWindow? window = null;
        var thread = new Thread(() =>
        {
            window = new MessageWindow($"PCCompanion.Tests.{Guid.NewGuid():N}");
            handle = window.Handle;
            ready.Set();
            MessageLoop.Run();
            window.Dispose();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        try
        {
            using var reader = new TaskbarLayoutReader();
            var elements = reader.Read(handle);

            // Only elements with a size, such as the parts of the window's title bar.
            Assert.NotNull(elements);
            Assert.All(elements, element => Assert.True(element.Bounds.Width > 0 && element.Bounds.Height > 0));
        }
        finally
        {
            window!.BeginInvoke(MessageLoop.Quit);
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public void ALayoutReaderReturnsNothingForAWindowThatIsGone()
    {
        using var reader = new TaskbarLayoutReader();

        Assert.Null(reader.Read(0));
    }

    [Fact]
    public void ALayoutReaderCantBeUsedOnceDisposed()
    {
        var reader = new TaskbarLayoutReader();
        reader.Dispose();
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.Read(0));
    }
}
