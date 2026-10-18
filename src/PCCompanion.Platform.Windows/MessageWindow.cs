using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A hidden window that receives messages, for example from a tray icon or another process.
/// Create, use and dispose it on one thread, and run a <see cref="MessageLoop"/> on that thread.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private static readonly Lazy<uint> InvokeMessage = new(() => RegisterMessage("PCCompanion.MessageWindow.Invoke"));

    // Kept in a field so the delegate lives as long as the window.
    private readonly WNDPROC _windowProcedure;
    private readonly string _className;
    private readonly ConcurrentQueue<Action> _actions = new();
    private HWND _handle;

    /// <param name="className">A window class name that is unique to the app, so other processes can find the window.</param>
    public unsafe MessageWindow(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        _className = className;
        _windowProcedure = WindowProcedure;
        var instance = PInvoke.GetModuleHandle((string?)null);

        fixed (char* name = className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = name,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        _handle = PInvoke.CreateWindowEx(0, className, string.Empty, 0, 0, 0, 0, 0, HWND.Null, null, instance, null);
        if (_handle.IsNull)
        {
            var exception = new Win32Exception();
            PInvoke.UnregisterClass(className, instance);
            throw exception;
        }
    }

    /// <summary>Raised on the window's thread for every message the window receives.</summary>
    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public nint Handle => _handle;

    /// <summary>
    /// Returns the ID of a message that is unique in the Windows session, for messages between processes.
    /// </summary>
    public static uint RegisterMessage(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var message = PInvoke.RegisterWindowMessage(name);
        return message != 0 ? message : throw new Win32Exception();
    }

    /// <summary>
    /// Posts a message to the window of another process, created with <paramref name="className"/>.
    /// Returns false when there is no such window.
    /// </summary>
    public static bool PostToWindow(string className, uint message)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        var window = PInvoke.FindWindow(className, null);
        return !window.IsNull && PInvoke.PostMessage(window, message, 0, 0);
    }

    /// <summary>
    /// Lets the process that owns the window created with <paramref name="className"/> bring a
    /// window to the front, when this process may: for example, so that a running instance this
    /// launch hands over to can show its window. Returns false when there is no such window or this
    /// process may not pass the right on.
    /// </summary>
    public static unsafe bool AllowOwnerToComeToFront(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        var window = PInvoke.FindWindow(className, null);
        uint processId;
        return !window.IsNull && PInvoke.GetWindowThreadProcessId(window, &processId) != 0 && PInvoke.AllowSetForegroundWindow(processId);
    }

    /// <summary>Posts a message to this window; it is received by the window's message loop.</summary>
    public void Post(uint message, nuint wParam = 0, nint lParam = 0)
    {
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        if (!PInvoke.PostMessage(_handle, message, wParam, lParam))
        {
            throw new Win32Exception();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the window's thread, from any thread. The action must not throw.
    /// Returns false, and the action doesn't run, when the window no longer exists.
    /// </summary>
    public bool BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _actions.Enqueue(action);
        var handle = _handle;
        return !handle.IsNull && PInvoke.PostMessage(handle, InvokeMessage.Value, 0, 0);
    }

    /// <summary>
    /// Lets processes without admin rights post <paramref name="message"/> to this window while
    /// this process runs as administrator; Windows blocks their messages otherwise. Returns false
    /// when Windows refuses.
    /// </summary>
    public unsafe bool AllowFromLowerIntegrity(uint message)
    {
        ObjectDisposedException.ThrowIf(_handle.IsNull, this);

        return PInvoke.ChangeWindowMessageFilterEx(_handle, message, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW, null);
    }

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
        if (window == _handle && message == InvokeMessage.Value)
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }

            return (LRESULT)0;
        }

        if (window == _handle && MessageReceived is { } handler)
        {
            var args = new WindowMessageEventArgs(message, wParam.Value, lParam.Value);
            handler(this, args);
            if (args.Handled)
            {
                return (LRESULT)0;
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
