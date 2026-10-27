using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>What happened to a file sent to the Recycle Bin.</summary>
public enum RecycleOutcome
{
    /// <summary>It's in the Recycle Bin.</summary>
    Recycled,

    /// <summary>
    /// Its drive has no Recycle Bin, as network locations and most removable drives, or
    /// <b>Don't move files to the Recycle Bin</b> is on, so it stayed.
    /// </summary>
    NoRecycleBin,

    /// <summary>It was too big for the Recycle Bin, and the user answered <b>Yes</b> to Windows' question, which deleted it for good.</summary>
    DeletedForGood,

    /// <summary>It was too big for the Recycle Bin, and the user answered <b>No</b> to Windows' question or closed it, so it stayed.</summary>
    Kept,

    /// <summary>Windows couldn't delete it; the error code says why.</summary>
    Failed,
}

/// <param name="ErrorCode">For <see cref="RecycleOutcome.Failed"/>: Windows' HRESULT.</param>
public sealed record RecycleResult(RecycleOutcome Outcome, int ErrorCode = 0);

/// <summary>
/// Moves files to the Recycle Bin, one at a time, on an STA thread of its own, as
/// <c>IFileOperation</c> needs. It never deletes a file for good on its own: on a drive without a
/// Recycle Bin it stops the delete, and for a file too big for the Recycle Bin it has Windows ask
/// the user first, in Windows' own window, while the thread waits for the answer.
/// </summary>
public sealed class RecycleBin : IDisposable
{
    // The Windows error the copy engine gives when the user cancels, here by answering No to the
    // question or closing it.
    private const int UserCancelled = unchecked((int)0x80270000);

    // Recycle when the drive allows it (TSF_DELETE_RECYCLE_IF_POSSIBLE).
    private const uint RecycleIfPossible = 0x80;

    private readonly BlockingCollection<(string Path, Action Asking, Action<RecycleResult> Done)> _queue = [];
    private readonly Thread _thread;

    public RecycleBin()
    {
        // A background thread, so a question still open when the host exits doesn't keep it running.
        _thread = new Thread(Run) { IsBackground = true, Name = "Recycle Bin" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>
    /// Queues a file for the Recycle Bin. On the Recycle Bin's thread, <paramref name="asking"/>
    /// runs when Windows asks whether to delete the file for good, and <paramref name="done"/>
    /// when the file has gone or stayed; neither may throw.
    /// </summary>
    public void Recycle(string path, Action asking, Action<RecycleResult> done)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentNullException.ThrowIfNull(done);

        _queue.Add((path, asking, done));
    }

    /// <summary>
    /// Stops taking files and ends the thread once the file it's sending is done, without waiting
    /// for it. A question that's open stays until the user answers.
    /// </summary>
    public void Dispose() => _queue.CompleteAdding();

    /// <summary>Sends one file to the Recycle Bin on the current thread, which must be an STA thread.</summary>
    public static unsafe RecycleResult RecycleNow(string path, Action asking)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(asking);

        var sink = new Sink();
        IShellItem? item = null;
        IFileOperation? operation = null;
        try
        {
            operation = (IFileOperation)new FileOperation();
            var iid = typeof(IShellItem).GUID;
            fixed (char* name = path)
            {
                PInvoke.SHCreateItemFromParsingName(name, null, &iid, out var created).ThrowOnFailure();
                item = (IShellItem)created;
            }

            operation.Advise(sink, out var cookie);
            operation.SetOperationFlags(
                FILEOPERATION_FLAGS.FOF_NO_UI | FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING
                | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE);
            operation.DeleteItem(item, null);

            int result;
            using (new QuestionHook(asking))
            {
                // Windows' HRESULT comes as the exception .NET maps it to, which isn't always a
                // COMException: the sink's own refusal comes back as the exception it threw.
                try
                {
                    operation.PerformOperations();
                    result = 0;
                }
                catch (Exception exception)
                {
                    result = exception.HResult;
                }
            }

            operation.Unadvise(cookie);
            return Outcome(sink, result);
        }
        catch (Exception exception)
        {
            // A file that went away before Windows reached it, or a path Windows can't open.
            return new RecycleResult(RecycleOutcome.Failed, exception.HResult);
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }

            if (operation is not null)
            {
                Marshal.ReleaseComObject(operation);
            }
        }
    }

    // A file that went to the Recycle Bin has a new item there; one deleted for good has none,
    // which Windows does only after the user answered Yes to its question.
    private static RecycleResult Outcome(Sink sink, int result) => sink switch
    {
        { Refused: true } => new RecycleResult(RecycleOutcome.NoRecycleBin),
        { Recycled: true } => new RecycleResult(RecycleOutcome.Recycled),
        _ when result == UserCancelled || sink.DeleteResult == UserCancelled => new RecycleResult(RecycleOutcome.Kept),
        _ when result >= 0 && sink.DeleteResult >= 0 && sink.Deleted => new RecycleResult(RecycleOutcome.DeletedForGood),
        _ => new RecycleResult(RecycleOutcome.Failed, result < 0 ? result : sink.DeleteResult),
    };

    private void Run()
    {
        foreach (var (path, asking, done) in _queue.GetConsumingEnumerable())
        {
            done(RecycleNow(path, asking));
        }

        _queue.Dispose();
    }

    // Windows' question is a dialog box that the delete creates on this thread, which a hook on
    // this thread only sees as it's created.
    private sealed unsafe class QuestionHook : IDisposable
    {
        private const string DialogClass = "#32770";

        private readonly Action _asking;
        private readonly HOOKPROC _procedure;
        private readonly UnhookWindowsHookExSafeHandle _hook;
        private bool _asked;

        public QuestionHook(Action asking)
        {
            _asking = asking;
            _procedure = OnHook;
            _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_CBT, _procedure, null, PInvoke.GetCurrentThreadId());
        }

        public void Dispose()
        {
            _hook.Dispose();
            GC.KeepAlive(_procedure);
        }

        private LRESULT OnHook(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code == PInvoke.HCBT_CREATEWND && !_asked && IsDialog((HWND)(nint)wParam.Value))
            {
                _asked = true;
                _asking();
            }

            return PInvoke.CallNextHookEx(null, code, wParam, lParam);
        }

        private static bool IsDialog(HWND window)
        {
            Span<char> name = stackalloc char[16];
            var length = PInvoke.GetClassName(window, name);
            return name[..length].SequenceEqual(DialogClass);
        }
    }

    // What Windows tells the delete as it goes.
    [ComVisible(true)]
    private sealed class Sink : IFileOperationProgressSink
    {
        public bool Refused { get; private set; }

        public bool Deleted { get; private set; }

        public bool Recycled { get; private set; }

        public int DeleteResult { get; private set; }

        public void PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            // Without the flag, Windows would delete the file for good without asking. Any failure
            // stops the delete; the exception's HRESULT is what Windows gets.
            if ((dwFlags & RecycleIfPossible) == 0)
            {
                Refused = true;
                throw new OperationCanceledException();
            }
        }

        public void PostDeleteItem(uint dwFlags, IShellItem psiItem, HRESULT hrDelete, IShellItem psiNewlyCreated)
        {
            Deleted = true;
            DeleteResult = hrDelete.Value;
            Recycled = hrDelete.Succeeded && psiNewlyCreated is not null;
        }

        public void StartOperations()
        {
        }

        public void FinishOperations(HRESULT hrResult)
        {
        }

        public void PreRenameItem(uint dwFlags, IShellItem psiItem, PCWSTR pszNewName)
        {
        }

        public void PostRenameItem(uint dwFlags, IShellItem psiItem, PCWSTR pszNewName, HRESULT hrRename, IShellItem psiNewlyCreated)
        {
        }

        public void PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName)
        {
        }

        public void PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName, HRESULT hrMove, IShellItem psiNewlyCreated)
        {
        }

        public void PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName)
        {
        }

        public void PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName, HRESULT hrCopy, IShellItem psiNewlyCreated)
        {
        }

        public void PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, PCWSTR pszNewName)
        {
        }

        public void PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, PCWSTR pszNewName, PCWSTR pszTemplateName, uint dwFileAttributes, HRESULT hrNew, IShellItem psiNewItem)
        {
        }

        public void UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
        {
        }

        public void ResetTimer()
        {
        }

        public void PauseTimer()
        {
        }

        public void ResumeTimer()
        {
        }
    }
}
