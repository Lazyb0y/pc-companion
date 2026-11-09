using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What a <see cref="QuestionDialog"/> shows, in the user's language: a heading, a text, an
/// optional line at the bottom, and its buttons, the one at <paramref name="DefaultButton"/> taking
/// Enter.
/// </summary>
public sealed record QuestionPage(string Heading, string Text, string? Footer, IReadOnlyList<string> Buttons, int DefaultButton);

/// <summary>
/// A question in a Windows task dialog, on an STA thread of its own, so the caller's thread keeps
/// running. It shows in the middle of a given area, such as the work area of an app's display, in
/// front of other windows, with the keyboard focus on the default button. Its text and buttons can
/// change while it's open. Windows draws task dialogs light, also in dark mode.
/// </summary>
public sealed class QuestionDialog
{
    // How often the dialog looks for a changed page or a request to close.
    private const uint TimerInterval = 200;

    private const int FirstButtonId = 100;

    private readonly string _title;
    private readonly AppIcon? _icon;
    private readonly Rectangle? _area;
    private readonly bool _rightToLeft;
    private readonly Action<int> _answered;
    private readonly Action<Exception> _failed;

    // The dialog calls back through this, so it lives as long as the question.
    private readonly PFTASKDIALOGCALLBACK _callback;

    private readonly Lock _gate = new();
    private HWND _dialog;
    private QuestionPage? _pending;
    private bool _closing;

    // The question's own thread's: texts and buttons the dialog reads until it closes.
    private readonly List<nint> _memory = [];
    private IconImage? _iconImage;

    private QuestionDialog(string title, AppIcon? icon, Rectangle? area, bool rightToLeft, Action<int> answered, Action<Exception> failed)
    {
        _title = title;
        _icon = icon;
        _area = area;
        _rightToLeft = rightToLeft;
        _answered = answered;
        _failed = failed;
        _callback = OnNotification;
    }

    /// <summary>Shows the question on a thread of its own, and returns at once.</summary>
    /// <param name="title">The window's title.</param>
    /// <param name="icon">The icon shown beside the heading, square, or <see langword="null"/> for none.</param>
    /// <param name="area">Where the dialog is centered, in physical pixels; the main display's work area when <see langword="null"/>.</param>
    /// <param name="rightToLeft">Whether the dialog is laid out from right to left.</param>
    /// <param name="answered">
    /// Called once on the question's thread when the dialog closes, with the index of the button
    /// chosen, or -1 when the user canceled it with Esc or by closing it, or it was closed with
    /// <see cref="Close"/>. It mustn't throw.
    /// </param>
    /// <param name="failed">
    /// Called on the question's thread, in place of <paramref name="answered"/>, when the dialog
    /// can't be shown. It mustn't throw.
    /// </param>
    public static QuestionDialog Show(string title, QuestionPage page, AppIcon? icon, Rectangle? area, bool rightToLeft, Action<int> answered, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(title);
        Validate(page);
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(failed);

        var question = new QuestionDialog(title, icon is { } square && square.Width == square.Height ? square : null, area, rightToLeft, answered, failed);

        // A background thread, so a dialog that's still open doesn't keep the process running.
        var thread = new Thread(() => question.Run(page)) { IsBackground = true, Name = "Question" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return question;
    }

    /// <summary>
    /// Changes the dialog's heading, text and buttons, from any thread; the dialog shows the page
    /// within a fifth of a second, with the keyboard focus on its default button.
    /// </summary>
    public void Change(QuestionPage page)
    {
        Validate(page);
        lock (_gate)
        {
            _pending = page;
        }
    }

    /// <summary>Closes the dialog as if canceled, from any thread; nothing happens once it's closed.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closing = true;
            if (!_dialog.IsNull)
            {
                PInvoke.PostMessage(_dialog, PInvoke.WM_CLOSE, 0, 0);
            }
        }
    }

    private static void Validate(QuestionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(page.Buttons);
        ArgumentOutOfRangeException.ThrowIfZero(page.Buttons.Count, nameof(page));
        ArgumentOutOfRangeException.ThrowIfNegative(page.DefaultButton, nameof(page));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page.DefaultButton, page.Buttons.Count, nameof(page));
    }

    private void Run(QuestionPage page)
    {
        try
        {
            if (_icon is { } icon)
            {
                _iconImage = IconImage.FromPixels(MemoryMarshal.Cast<byte, uint>(icon.Pixels.Span), icon.Width);
            }

            var config = Config(page);
            PInvoke.TaskDialogIndirect(config, out var button, out _, out _).ThrowOnFailure();
            _answered(button >= FirstButtonId ? button - FirstButtonId : -1);
        }
        catch (Exception exception)
        {
            _failed(exception);
        }
        finally
        {
            foreach (var memory in _memory)
            {
                Marshal.FreeHGlobal(memory);
            }

            _iconImage?.Dispose();
        }
    }

    private unsafe TASKDIALOGCONFIG Config(QuestionPage page)
    {
        var buttons = (TASKDIALOG_BUTTON*)Allocate(sizeof(TASKDIALOG_BUTTON) * page.Buttons.Count);
        for (var i = 0; i < page.Buttons.Count; i++)
        {
            buttons[i] = new TASKDIALOG_BUTTON { nButtonID = FirstButtonId + i, pszButtonText = Text(page.Buttons[i]) };
        }

        var flags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_CALLBACK_TIMER;
        if (_rightToLeft)
        {
            flags |= TASKDIALOG_FLAGS.TDF_RTL_LAYOUT;
        }

        if (_iconImage is not null)
        {
            flags |= TASKDIALOG_FLAGS.TDF_USE_HICON_MAIN;
        }

        var config = new TASKDIALOGCONFIG
        {
            cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
            dwFlags = flags,
            pszWindowTitle = Text(_title),
            pszMainInstruction = Text(page.Heading),
            pszContent = Text(page.Text),
            pszFooter = page.Footer is null ? default : Text(page.Footer),
            cButtons = (uint)page.Buttons.Count,
            pButtons = buttons,
            nDefaultButton = FirstButtonId + page.DefaultButton,
            pfCallback = _callback,
        };

        if (_iconImage is not null)
        {
            config.hMainIcon = new HICON(_iconImage.Handle);
        }

        return config;
    }

    // Kept until the dialog closes, since it reads the texts of every page it showed.
    private unsafe PCWSTR Text(string text) => new((char*)Allocate(text));

    private nint Allocate(int size)
    {
        var memory = Marshal.AllocHGlobal(size);
        _memory.Add(memory);
        return memory;
    }

    private nint Allocate(string text)
    {
        var memory = Marshal.StringToHGlobalUni(text);
        _memory.Add(memory);
        return memory;
    }

    private HRESULT OnNotification(HWND window, TASKDIALOG_NOTIFICATIONS notification, WPARAM wParam, LPARAM lParam, nint data)
    {
        switch (notification)
        {
            case TASKDIALOG_NOTIFICATIONS.TDN_CREATED:
                Place(window);
                lock (_gate)
                {
                    _dialog = window;
                    if (_closing)
                    {
                        PInvoke.PostMessage(window, PInvoke.WM_CLOSE, 0, 0);
                    }
                }

                break;
            case TASKDIALOG_NOTIFICATIONS.TDN_NAVIGATED:
                Place(window);
                break;
            case TASKDIALOG_NOTIFICATIONS.TDN_TIMER:
                ShowPending(window);

                // Zero keeps the timer's count going.
                return (HRESULT)0;
            case TASKDIALOG_NOTIFICATIONS.TDN_DESTROYED:
                lock (_gate)
                {
                    _dialog = HWND.Null;
                }

                break;
        }

        return (HRESULT)0;
    }

    private void ShowPending(HWND window)
    {
        QuestionPage? page;
        lock (_gate)
        {
            (page, _pending) = (_pending, null);
            if (_closing)
            {
                return;
            }
        }

        if (page is not null)
        {
            // The dialog takes the page as Windows lays it out in memory.
            var config = Allocate(Marshal.SizeOf<TASKDIALOGCONFIG>());
            Marshal.StructureToPtr(Config(page), config, fDeleteOld: false);
            PInvoke.SendMessage(window, (uint)TASKDIALOG_MESSAGES.TDM_NAVIGATE_PAGE, 0, config);
        }
    }

    private unsafe void Place(HWND window)
    {
        // A process that Windows started with a hidden window shows its first window hidden.
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNORMAL);

        var area = _area ?? MainWorkArea();
        if (area is { } work && PInvoke.GetWindowRect(window, out var bounds))
        {
            var x = work.Left + ((work.Width - (bounds.right - bounds.left)) / 2);
            var y = work.Top + ((work.Height - (bounds.bottom - bounds.top)) / 2);
            PInvoke.SetWindowPos(window, HWND.HWND_TOPMOST, x, y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
        }

        PInvoke.SetForegroundWindow(window);
    }

    private static unsafe Rectangle? MainWorkArea()
    {
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        return PInvoke.GetMonitorInfo(monitor, &info)
            ? Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom)
            : null;
    }
}
