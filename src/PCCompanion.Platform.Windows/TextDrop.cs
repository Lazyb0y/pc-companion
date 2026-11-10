using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using DataObjectInterface = System.Runtime.InteropServices.ComTypes.IDataObject;
using FormatEnumerator = System.Runtime.InteropServices.ComTypes.IEnumFORMATETC;

namespace PCCompanion.Platform.Windows;

/// <summary>What dropping text gave; logged by name.</summary>
public enum TextDropResult
{
    /// <summary>The app under the pointer took the text.</summary>
    Dropped,

    /// <summary>The app under the pointer didn't say within half a second that it would take the text.</summary>
    Refused,

    /// <summary>The app under the pointer didn't handle a message in time, or answered the drag only after a second.</summary>
    NotResponding,

    /// <summary>The drop's own window wasn't the one under the pointer, or didn't get the button press.</summary>
    NotUnderPointer,

    /// <summary>Another drop was still running.</summary>
    Busy,

    /// <summary>Windows refused something the drop needs, such as its window or OLE.</summary>
    Failed,
}

/// <summary>What the drag does after a question from Windows' drag loop.</summary>
/// <param name="Release">Whether the primary button is released now.</param>
public readonly record struct DragStep(DragAction Action, bool Release);

/// <summary>The answers <see cref="TextDrop"/>'s drop source gives Windows' drag loop.</summary>
public enum DragAction
{
    Continue,
    Drop,
    Cancel,
}

/// <summary>
/// How a drop goes on, as "Dropping the text" in docs/features/clipboard/middle-click-paste.md
/// says: the button is released once the app says it would copy the text, or after half a second
/// without that, and an answer that comes after a second cancels the drop, so text never arrives late.
/// </summary>
public static class DropDecisions
{
    /// <summary>How long the app under the pointer has to say it would take the text.</summary>
    public static readonly TimeSpan ConsentLimit = TimeSpan.FromMilliseconds(500);

    /// <summary>A drop the app answers after this is cancelled.</summary>
    public static readonly TimeSpan LateLimit = TimeSpan.FromSeconds(1);

    /// <summary>Windows' drag loop asks whether to go on, after a key or the button changed, or regularly while the button is held.</summary>
    public static DragStep Next(TimeSpan elapsed, bool escape, bool buttonHeld, bool copyOffered) =>
        escape ? new DragStep(DragAction.Cancel, true)
        : elapsed > LateLimit ? new DragStep(DragAction.Cancel, true)
        : buttonHeld ? new DragStep(DragAction.Continue, !copyOffered && elapsed > ConsentLimit)
        : new DragStep(copyOffered ? DragAction.Drop : DragAction.Cancel, false);

    /// <summary>
    /// The app under the pointer said what a drop would do. Returns whether it offers a copy that
    /// counts, which also releases the button.
    /// </summary>
    public static bool OffersCopy(TimeSpan elapsed, bool copy) => copy && elapsed <= LateLimit;
}

/// <summary>
/// Drops text at the pointer as a drag does, as "Dropping the text" in
/// docs/features/clipboard/middle-click-paste.md says. A small, nearly transparent window of its
/// own appears under the pointer and gets a press of the primary button by <c>SendInput</c>, only
/// when it's the window there, so no other app gets the press. It hides and starts OLE drag and
/// drop with the text as plain text only, offering a copy; once the app under the pointer says it
/// would copy, the button is released and the app inserts the text. Windows' drag loop needs a
/// pressed button: without one it never asks the app.
/// </summary>
/// <remarks>
/// It runs on a thread of its own, in a single-threaded apartment with OLE started, with its own
/// message loop, since the drag loop waits for the app. One drop runs at a time. A drop whose
/// app doesn't answer still releases the button within a second, from a timer.
/// </remarks>
public sealed class TextDrop : IDisposable
{
    /// <summary>How long the app under the pointer has to handle a message before a drop.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>The width and height of the drop's window, in physical pixels.</summary>
    public const int WindowSize = 24;

    // How long the press may take to reach the drop's window.
    private static readonly TimeSpan PressLimit = TimeSpan.FromSeconds(1);

    private const string WindowClassName = "PCCompanion.TextDrop";
    private const byte WindowOpacity = 5;

    // A feature turned off and on again has a new drop before the old one's thread has ended, so
    // each gets class names of its own.
    private static int s_instances;

    private readonly string _classPrefix = WindowClassName + "." + Interlocked.Increment(ref s_instances).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private MessageWindow? _requests;
    private Catcher? _catcher;
    private Exception? _startFailure;
    private Drag? _current;
    private bool _disposed;

    /// <exception cref="Win32Exception">The drop's thread couldn't start OLE or create its windows.</exception>
    public TextDrop()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Text drop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
        _ready.Dispose();
        if (_startFailure is not null)
        {
            throw _startFailure;
        }
    }

    /// <summary>
    /// Drops <paramref name="text"/> at <paramref name="point"/>, in physical pixels, where the
    /// pointer should be. The task never fails.
    /// </summary>
    public Task<TextDropResult> DropAsync(Point point, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var answer = new TaskCompletionSource<TextDropResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_requests is not { } requests || !requests.BeginInvoke(() => Begin(point, text, answer)))
        {
            answer.SetResult(TextDropResult.Failed);
        }

        return answer.Task;
    }

    /// <summary>Ends the drop's thread, without waiting for it; a drop still running is cancelled.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests?.BeginInvoke(MessageLoop.Quit);
    }

    private unsafe void Run()
    {
        try
        {
            var started = PInvoke.OleInitialize();
            if (started.Failed)
            {
                throw new Win32Exception(started.Value);
            }

            _requests = new MessageWindow(_classPrefix + ".Requests");
            _catcher = new Catcher(_classPrefix, this);
        }
        catch (Exception exception)
        {
            _startFailure = exception;
            _requests?.Dispose();
            _ready.Set();
            return;
        }

        _ready.Set();
        try
        {
            MessageLoop.Run();
        }
        finally
        {
            Finish(TextDropResult.Failed);
            _catcher!.Dispose();
            _requests!.Dispose();
            PInvoke.OleUninitialize();
        }
    }

    // On the drop's thread.
    private void Begin(Point point, string text, TaskCompletionSource<TextDropResult> answer)
    {
        if (_current is not null)
        {
            answer.SetResult(TextDropResult.Busy);
            return;
        }

        var target = WindowHitTest.Root(WindowHitTest.WindowAt(point));
        if (target == 0 || !TopLevelWindows.Answers(target, AnswerTimeout))
        {
            answer.SetResult(TextDropResult.NotResponding);
            return;
        }

        if (!_catcher!.ShowAt(point))
        {
            answer.SetResult(TextDropResult.NotUnderPointer);
            return;
        }

        var drag = new Drag(text, answer);
        _current = drag;
        drag.Watchdog = new Timer(_ => Watch(drag), null, DropDecisions.ConsentLimit + TimeSpan.FromMilliseconds(100), PressLimit);
        drag.Press();
    }

    // On a thread-pool thread: releases the button when the drag loop doesn't, as when the app
    // under the pointer stopped responding, and gives up when the press never came.
    private void Watch(Drag drag)
    {
        drag.Release();
        if (!drag.Started && drag.Clock.Elapsed > PressLimit)
        {
            _requests?.BeginInvoke(() =>
            {
                if (_current == drag && !drag.Started)
                {
                    _catcher?.Hide();
                    Finish(TextDropResult.NotUnderPointer);
                }
            });
        }
    }

    // On the drop's thread, as the drop's window gets the press.
    private void Pressed()
    {
        if (_current is not { Started: false } drag)
        {
            return;
        }

        drag.Started = true;
        _catcher!.Hide();
        var source = new DropSource(drag);
        var data = new TextDataObject(drag.Text);
        var result = PInvoke.DoDragDrop(data, source, DROPEFFECT.DROPEFFECT_COPY, out var effect);
        drag.Release();
        Finish(result == HRESULT.DRAGDROP_S_DROP && effect.HasFlag(DROPEFFECT.DROPEFFECT_COPY) ? TextDropResult.Dropped
            : source.Late ? TextDropResult.NotResponding
            : TextDropResult.Refused);
    }

    private void Finish(TextDropResult result)
    {
        if (_current is not { } drag)
        {
            return;
        }

        _current = null;
        drag.Release();
        drag.Watchdog?.Dispose();
        drag.Answer.TrySetResult(result);
    }

    private static unsafe void SendButton(bool down)
    {
        var swapped = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        var flags = (down, swapped) switch
        {
            (true, false) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN,
            (false, false) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
            (true, true) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN,
            (false, true) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
        };

        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union { mi = new MOUSEINPUT { dwFlags = flags } },
        };
        PInvoke.SendInput(new ReadOnlySpan<INPUT>(&input, 1), sizeof(INPUT));
    }

    // One drop, from the press to the answer.
    private sealed class Drag(string text, TaskCompletionSource<TextDropResult> answer)
    {
        private int _released;

        public string Text { get; } = text;

        public TaskCompletionSource<TextDropResult> Answer { get; } = answer;

        public Stopwatch Clock { get; } = new();

        public Timer? Watchdog { get; set; }

        public bool Started { get; set; }

        public bool CopyOffered { get; set; }

        public void Press()
        {
            Clock.Start();
            SendButton(down: true);
        }

        // Once, from whichever thread gets there first.
        public void Release()
        {
            if (Clock.IsRunning && Interlocked.Exchange(ref _released, 1) == 0)
            {
                SendButton(down: false);
            }
        }
    }

    // The drag loop's questions, on the drop's thread.
    [ComVisible(true)]
    private sealed class DropSource(Drag drag) : IDropSource
    {
        public bool Late { get; private set; }

        public HRESULT QueryContinueDrag(BOOL fEscapePressed, MODIFIERKEYS_FLAGS grfKeyState)
        {
            var elapsed = drag.Clock.Elapsed;
            var step = DropDecisions.Next(elapsed, fEscapePressed, grfKeyState.HasFlag(MODIFIERKEYS_FLAGS.MK_LBUTTON), drag.CopyOffered);
            Late |= step.Action == DragAction.Cancel && elapsed > DropDecisions.LateLimit;
            if (step.Release)
            {
                drag.Release();
            }

            return step.Action switch
            {
                DragAction.Drop => HRESULT.DRAGDROP_S_DROP,
                DragAction.Cancel => HRESULT.DRAGDROP_S_CANCEL,
                _ => (HRESULT)0,
            };
        }

        public HRESULT GiveFeedback(DROPEFFECT dwEffect)
        {
            if (!drag.CopyOffered && DropDecisions.OffersCopy(drag.Clock.Elapsed, dwEffect.HasFlag(DROPEFFECT.DROPEFFECT_COPY)))
            {
                drag.CopyOffered = true;
                drag.Release();
            }

            return HRESULT.DRAGDROP_S_USEDEFAULTCURSORS;
        }
    }

    // The text, as plain text only.
    [ComVisible(true)]
    private sealed class TextDataObject(string text) : DataObjectInterface
    {
        private const int NotImplemented = unchecked((int)0x80004001); // E_NOTIMPL
        private const int OutOfMemory = unchecked((int)0x8007000E); // E_OUTOFMEMORY

        private static readonly FORMATETC Format = new()
        {
            cfFormat = (short)CLIPBOARD_FORMAT.CF_UNICODETEXT,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        public unsafe void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (!IsText(format))
            {
                Marshal.ThrowExceptionForHR(HRESULT.DV_E_FORMATETC.Value);
            }

            var bytes = (nuint)((text.Length + 1) * sizeof(char));
            var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, bytes);
            var target = (char*)PInvoke.GlobalLock(memory);
            if (target is null)
            {
                PInvoke.GlobalFree(memory);
                Marshal.ThrowExceptionForHR(OutOfMemory);
            }

            text.AsSpan().CopyTo(new Span<char>(target, text.Length));
            target[text.Length] = '\0';
            PInvoke.GlobalUnlock(memory);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = (nint)memory.Value };
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => Marshal.ThrowExceptionForHR(NotImplemented);

        public int QueryGetData(ref FORMATETC format) => IsText(format) ? 0 : HRESULT.DV_E_FORMATETC.Value;

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = 0;
            return NotImplemented;
        }

        // Drop targets may store notes of their own; this object keeps none.
        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) => Marshal.ThrowExceptionForHR(NotImplemented);

        public FormatEnumerator EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET)
            {
                Marshal.ThrowExceptionForHR(NotImplemented);
            }

            return new OneFormat(Format, 0);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return HRESULT.OLE_E_ADVISENOTSUPPORTED.Value;
        }

        public void DUnadvise(int connection) => Marshal.ThrowExceptionForHR(HRESULT.OLE_E_ADVISENOTSUPPORTED.Value);

        public int EnumDAdvise(out IEnumSTATDATA? enumAdvise)
        {
            enumAdvise = null;
            return HRESULT.OLE_E_ADVISENOTSUPPORTED.Value;
        }

        private static bool IsText(FORMATETC format) =>
            format.cfFormat == Format.cfFormat && format.dwAspect == DVASPECT.DVASPECT_CONTENT && (format.tymed & TYMED.TYMED_HGLOBAL) != 0;
    }

    // Lists the one format the text comes in.
    [ComVisible(true)]
    private sealed class OneFormat(FORMATETC format, int position) : FormatEnumerator
    {
        private int _position = position;

        public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)
        {
            var fetched = 0;
            if (celt > 0 && _position == 0 && rgelt is { Length: > 0 })
            {
                rgelt[0] = format;
                _position = 1;
                fetched = 1;
            }

            if (pceltFetched is { Length: > 0 })
            {
                pceltFetched[0] = fetched;
            }

            return fetched == celt ? 0 : 1; // S_OK or S_FALSE
        }

        public int Skip(int celt)
        {
            _position = Math.Min(1, _position + celt);
            return _position == 1 && celt > 0 ? 0 : 1;
        }

        public int Reset()
        {
            _position = 0;
            return 0;
        }

        public void Clone(out FormatEnumerator newEnum) => newEnum = new OneFormat(format, _position);
    }

    // The small window that takes the press, nearly transparent, on top of other windows.
    private sealed class Catcher : IDisposable
    {
        // Kept in a field so the delegate lives as long as the window.
        private readonly WNDPROC _windowProcedure;
        private readonly TextDrop _owner;
        private readonly string _className;
        private HWND _handle;

        public unsafe Catcher(string className, TextDrop owner)
        {
            _owner = owner;
            _className = className;
            _windowProcedure = WindowProcedure;
            var instance = PInvoke.GetModuleHandle((string?)null);

            fixed (char* name = className)
            {
                // The I-beam, so the pointer doesn't change while the window is under it.
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _windowProcedure,
                    hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                    hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_IBEAM),
                    lpszClassName = name,
                };

                if (PInvoke.RegisterClassEx(windowClass) == 0)
                {
                    throw new Win32Exception();
                }
            }

            _handle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                string.Empty,
                WINDOW_STYLE.WS_POPUP,
                0,
                0,
                WindowSize,
                WindowSize,
                HWND.Null,
                null,
                instance,
                null);

            if (_handle.IsNull)
            {
                var exception = new Win32Exception();
                PInvoke.UnregisterClass(className, instance);
                throw exception;
            }

            PInvoke.SetLayeredWindowAttributes(_handle, new COLORREF(0), WindowOpacity, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        }

        // Shows the window centered on the point, and says whether it's the window there now.
        public bool ShowAt(Point point)
        {
            PInvoke.SetWindowPos(_handle, HWND.HWND_TOPMOST, point.X - (WindowSize / 2), point.Y - (WindowSize / 2), WindowSize, WindowSize,
                SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            if (WindowHitTest.WindowAt(point) == (nint)_handle)
            {
                return true;
            }

            Hide();
            return false;
        }

        public void Hide() => PInvoke.ShowWindow(_handle, SHOW_WINDOW_CMD.SW_HIDE);

        public void Dispose()
        {
            if (_handle.IsNull)
            {
                return;
            }

            PInvoke.DestroyWindow(_handle);
            _handle = HWND.Null;
            PInvoke.UnregisterClass(_className, PInvoke.GetModuleHandle((string?)null));
        }

        private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
        {
            if (message == PInvoke.WM_LBUTTONDOWN)
            {
                try
                {
                    _owner.Pressed();
                }
                catch (Exception)
                {
                    // An exception here would end the host; the drop counts as failed.
                    _owner.Finish(TextDropResult.Failed);
                }

                return (LRESULT)0;
            }

            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }
}
