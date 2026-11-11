using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test puts text into a hidden Edit box of its own, on a thread of its own, as another app's
// box would be; no other window is touched.
public sealed class EditBoxesTests
{
    [Theory]
    [InlineData("Edit", EditBoxKind.Plain)]
    [InlineData("EDIT", EditBoxKind.Plain)]
    [InlineData("WindowsForms10.Edit.app.0.21b46d2_r3_ad1", EditBoxKind.Plain)]
    [InlineData("RICHEDIT50W", EditBoxKind.Rich)]
    [InlineData("RichEdit20W", EditBoxKind.Rich)]
    [InlineData("RichEditD2DPT", EditBoxKind.Rich)]
    [InlineData("WindowsForms10.RICHEDIT50W.app.0.21b46d2_r3_ad1", EditBoxKind.Rich)]
    [InlineData("EditBox", EditBoxKind.None)]
    [InlineData("TEdit", EditBoxKind.None)]
    [InlineData("Chrome_RenderWidgetHostHWND", EditBoxKind.None)]
    [InlineData("WindowsForms10.Window.8.app.0.21b46d2_r3_ad1", EditBoxKind.None)]
    public void WindowsOwnTextBoxesAreKnownByTheirClass(string className, EditBoxKind kind) =>
        Assert.Equal(kind, EditBoxes.KindOf(className));

    [Fact]
    public void APointOutsideTheTextIsNoCharacter()
    {
        Assert.Null(EditBoxes.CharacterAt(-1));
        Assert.Null(EditBoxes.CharacterAt(unchecked((nint)0xFFFFFFFF)));
        Assert.Equal(5, EditBoxes.CharacterAt((2 << 16) | 5));
    }

    [Fact]
    public void TheTextGoesInWhereThePointIsAndOneUndoTakesItOut()
    {
        using var box = new EditBoxThread("alpha beta gamma");
        var point = box.PointOfCharacter(6);

        var result = EditBoxes.InsertAt(box.Handle, point, "<مرحبا 中文 😀>");

        Assert.Equal(EditBoxResult.Inserted, result);
        Assert.Equal("alpha <مرحبا 中文 😀>beta gamma", box.Text);
        box.Undo();
        Assert.Equal("alpha beta gamma", box.Text);
    }

    [Fact]
    public void TheTextGoesInAtTheTextCursor()
    {
        using var box = new EditBoxThread("alpha beta");
        box.Select(5);

        Assert.Equal(EditBoxResult.Inserted, EditBoxes.InsertAtCursor(box.Handle, "!"));
        Assert.Equal("alpha! beta", box.Text);
    }

    // Once the app handles the message, the text goes in however long it takes, so the wait for it
    // must not end first and say the box didn't answer.
    [Fact]
    public void ABoxThatTakesAWhileToPutTheTextInSaysItWentIn()
    {
        using var box = new EditBoxThread("alpha beta", insertDelay: EditBoxes.MessageTimeout * 2);
        box.Select(5);

        Assert.Equal(EditBoxResult.Inserted, EditBoxes.InsertAtCursor(box.Handle, "!"));
        Assert.Equal("alpha! beta", box.Text);
    }

    [Fact]
    public void ABoxThatDoesntAnswerGetsNothingNowOrLater()
    {
        using var box = new EditBoxThread("alpha beta");
        var point = box.PointOfCharacter(6);

        box.Block(TimeSpan.FromSeconds(1.5));
        var result = EditBoxes.InsertAt(box.Handle, point, "<late>");
        Thread.Sleep(TimeSpan.FromSeconds(2));

        Assert.Equal(EditBoxResult.NotResponding, result);
        Assert.Equal("alpha beta", box.Text);
    }

    [Fact]
    public void AWindowAnswersUnlessItsThreadIsBusy()
    {
        using var box = new EditBoxThread("alpha");

        Assert.True(TopLevelWindows.Answers(box.Handle, TimeSpan.FromMilliseconds(500)));
        box.Block(TimeSpan.FromSeconds(1));
        Assert.False(TopLevelWindows.Answers(box.Handle, TimeSpan.FromMilliseconds(200)));
    }

    // A hidden Edit box with its own thread and message loop. With an insert delay, the box waits
    // that long before it handles EM_REPLACESEL, as a box with long text can.
    private sealed class EditBoxThread : IDisposable
    {
        private const int WindowProcedure = -4; // GWLP_WNDPROC
        private const uint ReplaceSelection = 0x00C2; // EM_REPLACESEL
        private const uint GetTextLength = 0x000E; // WM_GETTEXTLENGTH
        private const uint GetText = 0x000D; // WM_GETTEXT
        private const uint SetSelection = 0x00B1; // EM_SETSEL
        private const uint UndoMessage = 0x00C7; // EM_UNDO
        private const uint PositionOfCharacter = 0x00D6; // EM_POSFROMCHAR
        private const uint Quit = 0x0012; // WM_QUIT
        private const uint BlockMessage = 0x8001; // WM_APP + 1

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private uint _threadId;
        private WindowProc? _slowProcedure;
        private nint _editProcedure;

        public EditBoxThread(string text, TimeSpan? insertDelay = null)
        {
            _thread = new Thread(() => Run(text, insertDelay)) { IsBackground = true, Name = "Test Edit box" };
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(5));
            Assert.NotEqual(nint.Zero, Handle);
        }

        public nint Handle { get; private set; }

        public string Text
        {
            get
            {
                var length = (int)SendMessageW(Handle, GetTextLength, 0, 0);
                var buffer = new char[length + 1];
                var copied = (int)SendMessageW(Handle, GetText, length + 1, buffer);
                return new string(buffer, 0, copied);
            }
        }

        public void Select(int character) => SendMessageW(Handle, SetSelection, character, character);

        public void Undo() => SendMessageW(Handle, UndoMessage, 0, 0);

        // The screen point a pixel inside the character's left edge, half way down its line.
        public Point PointOfCharacter(int character)
        {
            var packed = (int)SendMessageW(Handle, PositionOfCharacter, character, 0);
            var point = new Point((short)(packed & 0xFFFF) + 1, (short)(packed >> 16) + 4);
            ClientToScreen(Handle, ref point);
            return point;
        }

        // Keeps the box's thread from handling messages, as an app that stopped responding. The
        // pause lets the thread take the request before the test sends anything.
        public void Block(TimeSpan time)
        {
            PostThreadMessageW(_threadId, BlockMessage, 0, (nint)time.TotalMilliseconds);
            Thread.Sleep(100);
        }

        public void Dispose()
        {
            PostThreadMessageW(_threadId, Quit, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
        }

        private void Run(string text, TimeSpan? insertDelay)
        {
            _threadId = GetCurrentThreadId();
            Handle = CreateWindowExW(0, "EDIT", text, 0x00CF0000, 100, 100, 400, 60, 0, 0, 0, 0);
            if (insertDelay is { } delay)
            {
                _slowProcedure = (window, message, wParam, lParam) =>
                {
                    if (message == ReplaceSelection)
                    {
                        Thread.Sleep(delay);
                    }

                    return CallWindowProcW(_editProcedure, window, message, wParam, lParam);
                };
                _editProcedure = SetWindowLongPtrW(Handle, WindowProcedure, Marshal.GetFunctionPointerForDelegate(_slowProcedure));
            }

            _ready.Set();
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == BlockMessage)
                {
                    Thread.Sleep((int)message.LParam);
                    continue;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            DestroyWindow(Handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public nint Window;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }

        private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

        [DllImport("user32.dll")]
        private static extern nint CallWindowProcW(nint procedure, nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        private static extern int GetMessageW(out Msg message, nint window, uint first, uint last);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Msg message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref Msg message);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint SendMessageW(nint window, uint message, nint wParam, [Out] char[] lParam);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(nint window, ref Point point);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
