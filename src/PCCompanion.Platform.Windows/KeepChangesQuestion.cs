using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>The texts of a <see cref="KeepChangesQuestion"/>, in the user's language.</summary>
/// <param name="Title">The window's title.</param>
/// <param name="Question">The question, such as "Keep these display settings?".</param>
/// <param name="SecondsLeft">The text below the question for the seconds left.</param>
/// <param name="Keep">The button that keeps the change.</param>
/// <param name="Revert">The button that reverts it.</param>
public sealed record KeepChangesTexts(string Title, string Question, Func<int, string> SecondsLeft, string Keep, string Revert);

/// <summary>
/// Asks whether to keep a change that could leave a display black, as Windows Settings does: a
/// Windows task dialog with a button that keeps the change, one that reverts it, and the seconds
/// left. It runs on an STA thread of its own, so the caller's thread keeps running, and shows in
/// the middle of the main display, in front of other windows, with the keyboard focus on the keep
/// button. Enter keeps; the revert button, Esc and closing the dialog revert. The caller keeps the
/// time, and closes the question when time is up.
/// </summary>
public sealed class KeepChangesQuestion
{
    private const int KeepButton = 100;
    private const int RevertButton = 101;

    private readonly KeepChangesTexts _texts;
    private readonly long _deadline;
    private readonly bool _rightToLeft;
    private readonly Action<bool> _answered;
    private readonly Action<Exception> _failed;

    // The dialog calls back through this, so it lives as long as the question.
    private readonly PFTASKDIALOGCALLBACK _callback;

    private readonly Lock _gate = new();
    private HWND _dialog;
    private bool _closing;

    // The question's own thread's.
    private int _shownSeconds = -1;
    private nint _shownText;

    private KeepChangesQuestion(KeepChangesTexts texts, TimeSpan time, bool rightToLeft, Action<bool> answered, Action<Exception> failed)
    {
        _texts = texts;
        _deadline = Environment.TickCount64 + (long)time.TotalMilliseconds;
        _rightToLeft = rightToLeft;
        _answered = answered;
        _failed = failed;
        _callback = OnNotification;
    }

    /// <summary>Shows the question on a thread of its own, and returns at once.</summary>
    /// <param name="time">The time the dialog counts down from.</param>
    /// <param name="rightToLeft">Whether the dialog is laid out from right to left.</param>
    /// <param name="answered">
    /// Called once on the question's thread when the dialog closes: true when the user kept the
    /// change, false otherwise, also after <see cref="Close"/>. It mustn't throw.
    /// </param>
    /// <param name="failed">
    /// Called on the question's thread, in place of <paramref name="answered"/>, when the dialog
    /// can't be shown. It mustn't throw.
    /// </param>
    public static KeepChangesQuestion Show(KeepChangesTexts texts, TimeSpan time, bool rightToLeft, Action<bool> answered, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(failed);

        var question = new KeepChangesQuestion(texts, time, rightToLeft, answered, failed);

        // A background thread, so a dialog that's still open doesn't keep the process running.
        var thread = new Thread(question.Run) { IsBackground = true, Name = "Keep changes question" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return question;
    }

    /// <summary>
    /// Closes the question as if the user reverted the change, from any thread; nothing happens
    /// once it's closed.
    /// </summary>
    public void Close()
    {
        lock (_gate)
        {
            _closing = true;
            if (!_dialog.IsNull)
            {
                PInvoke.PostMessage(_dialog, (uint)TASKDIALOG_MESSAGES.TDM_CLICK_BUTTON, RevertButton, 0);
            }
        }
    }

    private int SecondsLeft() => (int)Math.Max(0, Math.Ceiling((_deadline - Environment.TickCount64) / 1000.0));

    private void Run()
    {
        try
        {
            var button = ShowDialog();
            _answered(button == KeepButton);
        }
        catch (Exception exception)
        {
            _failed(exception);
        }
        finally
        {
            Marshal.FreeHGlobal(_shownText);
        }
    }

    private unsafe int ShowDialog()
    {
        fixed (char* title = _texts.Title)
        fixed (char* question = _texts.Question)
        fixed (char* secondsLeft = _texts.SecondsLeft(SecondsLeft()))
        fixed (char* keep = _texts.Keep)
        fixed (char* revert = _texts.Revert)
        {
            var buttons = stackalloc TASKDIALOG_BUTTON[]
            {
                new() { nButtonID = KeepButton, pszButtonText = keep },
                new() { nButtonID = RevertButton, pszButtonText = revert },
            };
            var flags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_CALLBACK_TIMER;
            if (_rightToLeft)
            {
                flags |= TASKDIALOG_FLAGS.TDF_RTL_LAYOUT;
            }

            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
                dwFlags = flags,
                pszWindowTitle = title,
                pszMainInstruction = question,
                pszContent = secondsLeft,
                cButtons = 2,
                pButtons = buttons,
                nDefaultButton = KeepButton,
                pfCallback = _callback,
            };

            var result = PInvoke.TaskDialogIndirect(config, out var button, out _, out _);
            result.ThrowOnFailure();
            return button;
        }
    }

    private HRESULT OnNotification(HWND window, TASKDIALOG_NOTIFICATIONS notification, WPARAM wParam, LPARAM lParam, nint data)
    {
        switch (notification)
        {
            case TASKDIALOG_NOTIFICATIONS.TDN_CREATED:
                Created(window);
                break;
            case TASKDIALOG_NOTIFICATIONS.TDN_TIMER:
                ShowSecondsLeft(window);
                break;
            case TASKDIALOG_NOTIFICATIONS.TDN_DESTROYED:
                lock (_gate)
                {
                    _dialog = HWND.Null;
                }

                break;
        }

        return (HRESULT)0;
    }

    private unsafe void Created(HWND window)
    {
        // A process that Windows started with a hidden window shows its first window hidden.
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNORMAL);

        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (PInvoke.GetMonitorInfo(monitor, &info) && PInvoke.GetWindowRect(window, out var bounds))
        {
            var work = info.rcWork;
            var x = work.left + ((work.right - work.left - (bounds.right - bounds.left)) / 2);
            var y = work.top + ((work.bottom - work.top - (bounds.bottom - bounds.top)) / 2);
            PInvoke.SetWindowPos(window, HWND.HWND_TOPMOST, x, y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
        }

        PInvoke.SetForegroundWindow(window);

        lock (_gate)
        {
            _dialog = window;
            if (_closing)
            {
                PInvoke.PostMessage(window, (uint)TASKDIALOG_MESSAGES.TDM_CLICK_BUTTON, RevertButton, 0);
            }
        }
    }

    private void ShowSecondsLeft(HWND window)
    {
        var seconds = SecondsLeft();
        if (seconds == _shownSeconds)
        {
            return;
        }

        // The dialog keeps showing the text it was given, so it stays until the next one replaces it.
        var text = Marshal.StringToHGlobalUni(_texts.SecondsLeft(seconds));
        PInvoke.SendMessage(window, (uint)TASKDIALOG_MESSAGES.TDM_UPDATE_ELEMENT_TEXT, (nuint)TASKDIALOG_ELEMENTS.TDE_CONTENT, text);
        Marshal.FreeHGlobal(_shownText);
        _shownText = text;
        _shownSeconds = seconds;
    }
}
