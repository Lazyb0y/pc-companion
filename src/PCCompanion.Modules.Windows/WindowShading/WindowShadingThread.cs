using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Runs Window Shading on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads" in
/// docs/features/windows/window-shading.md says: the strips are windows with their own input, and
/// the hooks need a message loop. The host's UI thread starts it, passes it new settings and
/// requests, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class WindowShadingThread : MessageLoopThread<WindowShadingSettings>
{
    private const string WindowClassName = "PCCompanion.WindowShading";

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ShadedWindows _set;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Shader? _shader;
    private volatile int _count;

    public WindowShadingThread(WindowShadingSettings settings, TimeProvider time, ILogger logger, ShadedWindows set, DropDownWindows dropDown, WindowTabsWindows tabs, WindowShadingThread? previous = null)
        : base("Window Shading", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);

        _time = time;
        _logger = logger;
        _set = set;
        _dropDown = dropDown;
        _tabs = tabs;
        Start();
    }

    /// <summary>How many windows are rolled up, for the PC Companion icon's menu; any thread can read it.</summary>
    public int Count => _count;

    /// <summary>
    /// Rolls a window up, for a click or the wheel on its title bar, on the feature's thread.
    /// Returns false while the feature's thread doesn't run, as when it's starting or failed.
    /// </summary>
    public bool RollUp(nint window)
    {
        if (_shader is null)
        {
            return false;
        }

        Post(() => _shader?.Run(controller => controller.RollUp(window)));
        return true;
    }

    /// <summary>Runs one of Window Shading's actions, for a shortcut, a mouse button or the menu, on the feature's thread.</summary>
    public void Run(ShadeAction action) => Post(() => _shader?.Run(controller =>
    {
        switch (action)
        {
            case ShadeAction.RollUpFront:
                controller.RollUpFront();
                break;
            case ShadeAction.RollDownLast:
                controller.RollDownLast();
                break;
            case ShadeAction.RollDownAll:
                controller.RollDownAll();
                break;
        }
    }));

    /// <summary>Rolls every window down at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _shader?.Controller.Release();

    protected override IDisposable Open(MessageWindow window)
    {
        var shader = new Shader(this, window);
        _shader = shader;
        return shader;
    }

    protected override void Apply(WindowShadingSettings settings)
    {
        _shader!.Controller.Apply(settings);
        LogApplied(_logger, settings.KeepOnTop, settings.Hover.Enabled);
    }

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    // A failure rolls every window down, as stopping does.
    protected override void OnFailed(Exception exception)
    {
        LogFailed(_logger, exception);
        try
        {
            _shader?.Controller.Release();
        }
        catch (Exception release)
        {
            LogReleaseFailed(_logger, release);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading's thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading applied its settings: keep on top {KeepOnTop}, hover {Hover}.")]
    private static partial void LogApplied(ILogger logger, bool keepOnTop, bool hover);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading rolled down {Count} windows as it stopped.")]
    private static partial void LogReleased(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Shading failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Shading couldn't roll the windows down after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller and its hooks, set only while a window is rolled up.
    private sealed class Shader : IDisposable
    {
        private readonly WindowShadingThread _owner;
        private readonly WindowsShadingSystem _system;
        private readonly List<WinEventHook> _hooks = [];

        public Shader(WindowShadingThread owner, MessageWindow window)
        {
            _owner = owner;
            _system = new WindowsShadingSystem(window, owner.Guard, owner._dropDown, owner._tabs);
            Controller = new ShadingController(_system, owner._time, action => window.BeginInvoke(() => owner.Guard(() => Run(_ => action()))), owner._logger, owner._set);
            _system.WatchingChanged += (_, _) =>
            {
                if (_system.IsWatching)
                {
                    Hook();
                }
                else
                {
                    Unhook();
                }
            };
        }

        public ShadingController Controller { get; }

        // Runs work on the controller, then keeps the count the menu reads up to date.
        public void Run(Action<ShadingController> work)
        {
            work(Controller);
            _owner._count = Controller.Count;
        }

        public void Dispose()
        {
            Unhook();
            var released = Controller.Release();
            if (released > 0)
            {
                LogReleased(_owner._logger, released);
            }

            _owner._count = 0;
            _owner._shader = null;
        }

        // Out-of-context hooks for every process, which run no code in other apps.
        private void Hook()
        {
            if (_hooks.Count > 0)
            {
                return;
            }

            Add(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, window => Controller.OnForeground(window));
            Add(WinEventHook.MinimizeEndEvent, WinEventHook.MinimizeEndEvent, window => Controller.OnRestored(window));
            Add(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectDestroyEvent, window => Controller.OnDestroyed(window));
            Add(WinEventHook.ObjectHideEvent, WinEventHook.ObjectHideEvent, window => Controller.OnHidden(window));
        }

        private void Add(uint first, uint last, Action<nint> handle)
        {
            var hook = new WinEventHook(first, last, skipOwnProcess: true);
            hook.Raised += (_, e) => _owner.Guard(() =>
            {
                if (e.IsAboutWindow && _system.IsWatched(e.Window))
                {
                    Run(_ => handle(e.Window));
                }
            });
            _hooks.Add(hook);
        }

        private void Unhook()
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            _hooks.Clear();
        }
    }
}

/// <summary>Window Shading's actions for shortcuts, mouse buttons and the menu.</summary>
public enum ShadeAction
{
    RollUpFront,
    RollDownLast,
    RollDownAll,
}
