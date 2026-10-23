using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test asks only a hidden window of its own; the window in front isn't read or changed.
public sealed class TextCursorTests
{
    private const uint GetObjectMessage = 0x003D; // WM_GETOBJECT
    private const nint TextCursorObject = -8; // OBJID_CARET
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void NoWindowCantBeAsked()
    {
        Assert.False(TextCursor.CanAsk(0));
    }

    [Fact]
    public void AWindowOfAProcessAtTheSameIntegrityLevelCanBeAsked()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.True(TextCursor.CanAsk(window.Handle));
    }

    // The window is disposed on its own thread before the test waits for the answer.
    [Fact]
    public async Task AWindowThatDoesntAnswerItselfGivesNoTextCursor()
    {
        var asked = new List<nint>();
        Task<Rectangle?> answer;
        using (var window = new MessageWindow(UniqueClassName()))
        {
            window.MessageReceived += (_, e) =>
            {
                if (e.Message == GetObjectMessage)
                {
                    asked.Add(e.LParam);
                }
            };

            answer = TextCursor.AskAsync(window.Handle, Timeout);
            RunFor(window, TimeSpan.FromMilliseconds(500));
        }

        Assert.Null(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal([TextCursorObject], asked);
    }

    // The window's thread runs no message loop while the request waits, like an app that stopped responding.
    [Fact]
    public async Task AWindowThatDoesntAnswerInTimeGivesNoTextCursor()
    {
        Task<Rectangle?> answer;
        using (var window = new MessageWindow(UniqueClassName()))
        {
            answer = TextCursor.AskAsync(window.Handle, Timeout);
            Thread.Sleep(Timeout * 3);
        }

        Assert.Null(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
