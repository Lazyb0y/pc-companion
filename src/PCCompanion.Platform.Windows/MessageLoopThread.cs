using System;
using System.Collections.Generic;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A feature's own STA thread, with a hidden window and a message loop. The thread that creates it
/// passes it new settings and stops it without waiting for it, except for <see cref="Join"/>.
/// Everything on the thread runs inside its error boundary: a failure ends the thread, which lets go
/// of what <see cref="Open"/> created and of the window.
/// </summary>
/// <remarks>
/// The derived class sets its fields, then calls <see cref="Start"/> last in its constructor. It
/// fills in what it creates on the thread, how it applies settings, and its log messages; a handler
/// of its own runs inside <see cref="Guard"/>.
/// </remarks>
/// <typeparam name="TSettings">The settings, which are immutable, so the threads share no state.</typeparam>
public abstract class MessageLoopThread<TSettings>
    where TSettings : class
{
    private readonly string _windowClassName;
    private readonly MessageLoopThread<TSettings>? _previous;
    private readonly Thread _thread;

    // Shared with the thread that created it.
    private readonly Lock _gate = new();
    private TSettings _latest;
    private MessageWindow? _window;
    private bool _stopping;
    private volatile bool _failed;

    // The thread's own, while its message loop runs.
    private TSettings? _applied;
    private bool _opened;

    /// <param name="name">The thread's name, which debuggers show.</param>
    /// <param name="windowClassName">The window's class name, which only one window at a time can have.</param>
    /// <param name="settings">The settings the thread applies first.</param>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it creates its window.
    /// </param>
    protected MessageLoopThread(string name, string windowClassName, TSettings settings, MessageLoopThread<TSettings>? previous)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(windowClassName);
        ArgumentNullException.ThrowIfNull(settings);

        _windowClassName = windowClassName;
        _latest = settings;
        _previous = previous;

        // A background thread, so one that doesn't end doesn't keep the process running.
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Whether the thread stopped because of a failure, which <see cref="OnFailed"/> was told.</summary>
    public bool HasFailed => _failed;

    private TSettings Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Passes new settings to the thread, without waiting for it. Call it from one thread.</summary>
    public void Update(TSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MessageWindow? window;
        lock (_gate)
        {
            _latest = settings;
            window = _window;
        }

        // Without a window yet, the thread takes the latest settings when it creates one.
        window?.BeginInvoke(() => Guard(ApplyLatest));
    }

    /// <summary>Asks the thread to let go of what it created and end, without waiting for it.</summary>
    public void Stop()
    {
        MessageWindow? window;
        lock (_gate)
        {
            _stopping = true;
            window = _window;
        }

        window?.BeginInvoke(() =>
        {
            Guard(OnStopping);
            MessageLoop.Quit();
        });
    }

    /// <summary>Waits for the thread to end. Returns false when it didn't in time.</summary>
    public bool Join(TimeSpan timeout) => _thread.Join(timeout);

    /// <summary>Starts the thread. The derived class calls it last in its constructor, once its fields are set.</summary>
    protected void Start() => _thread.Start();

    /// <summary>
    /// Runs on the thread once it has its window, unless it was asked to stop first, and creates what
    /// the feature needs there. What it returns is disposed when the thread ends, before the window.
    /// </summary>
    protected abstract IDisposable? Open(MessageWindow window);

    /// <summary>Runs on the thread after <see cref="Open"/>, and again each time the settings change.</summary>
    protected abstract void Apply(TSettings settings);

    /// <summary>Runs on the thread when it's asked to stop, before its message loop ends; for example, it closes an open menu.</summary>
    protected virtual void OnStopping()
    {
    }

    /// <summary>Runs on the thread once its message loop starts.</summary>
    protected abstract void OnStarted();

    /// <summary>Runs on the thread as it ends, after a failure too.</summary>
    protected abstract void OnStopped();

    /// <summary>Runs on the thread when something there failed, which ends the thread.</summary>
    protected abstract void OnFailed(Exception exception);

    /// <summary>
    /// Runs <paramref name="action"/> on the thread inside its error boundary, where an exception
    /// would end the process. A failure ends the thread; after one, nothing more runs.
    /// </summary>
    protected void Guard(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_failed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Fail(exception);
            MessageLoop.Quit();
        }
    }

    private void Run()
    {
        try
        {
            _previous?.Join(Timeout.InfiniteTimeSpan);

            using var window = new MessageWindow(_windowClassName);
            try
            {
                if (Attach(window))
                {
                    using var opened = Open(window);
                    _opened = true;
                    ApplyLatest();
                    OnStarted();
                    MessageLoop.Run();
                }
            }
            finally
            {
                Detach();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }

        OnStopped();
    }

    // Returns false when the thread was asked to stop before it had a window.
    private bool Attach(MessageWindow window)
    {
        lock (_gate)
        {
            _window = window;
            return !_stopping;
        }
    }

    private void Detach()
    {
        lock (_gate)
        {
            _window = null;
        }
    }

    // Settings that arrive while Open runs wait for it; it applies the latest ones.
    private void ApplyLatest()
    {
        var settings = Latest;
        if (!_opened || EqualityComparer<TSettings>.Default.Equals(settings, _applied))
        {
            return;
        }

        _applied = settings;
        Apply(settings);
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        OnFailed(exception);
    }
}
