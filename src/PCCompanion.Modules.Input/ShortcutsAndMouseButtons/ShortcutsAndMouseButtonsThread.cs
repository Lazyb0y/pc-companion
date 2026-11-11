using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// Runs Shortcuts & Mouse Buttons on a <see cref="MessageLoopThread{TSettings}"/> of its own, as
/// "Threads" in docs/features/input/shortcuts-and-mouse-buttons.md says: shortcuts need a message
/// loop. The host's UI thread starts it, passes it new plans and stops it, and never waits for it,
/// except for <see cref="MessageLoopThread{TSettings}.Join"/> when the host exits.
/// </summary>
public sealed partial class ShortcutsAndMouseButtonsThread : MessageLoopThread<TriggerPlan>
{
    private const string WindowClassName = "PCCompanion.ShortcutsAndMouseButtons";

    private readonly IHostActions _actions;
    private readonly ShortcutRefusals _refusals;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // The thread's own, while its message loop runs.
    private TriggerController? _controller;

    /// <param name="previous">A thread that was asked to stop, which this one waits for before it registers the shortcuts.</param>
    public ShortcutsAndMouseButtonsThread(TriggerPlan plan, IHostActions actions, ShortcutRefusals refusals, TimeProvider time, ILogger logger, ShortcutsAndMouseButtonsThread? previous = null)
        : base("Shortcuts & Mouse Buttons", WindowClassName, plan, previous)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _actions = actions;
        _refusals = refusals;
        _time = time;
        _logger = logger;
        Start();
    }

    /// <summary>Raw input reported a click, which the thread checks the shared pointer hook saw. Call it from any thread.</summary>
    public void RawPress(PointerButton button, uint time) => Post(() => _controller?.RawPress(button, time));

    protected override IDisposable? Open(MessageWindow window)
    {
        var system = new WindowsTriggerSystem(window, _actions, _refusals, ShortcutsAndMouseButtonsFeature.Definition.Id, new Events(this));
        var controller = new TriggerController(system, new WindowsShortcutEnvironment(window), _time, action => window.BeginInvoke(() => Guard(action)), _logger);
        WinEventHook foreground;
        try
        {
            // Another program's window coming to the front. Out-of-context, so it runs no code in other apps.
            foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
        }
        catch
        {
            controller.Dispose();
            system.Dispose();
            throw;
        }

        foreground.Raised += (_, _) => Guard(controller.OnForeground);
        _controller = controller;
        return new Opened(controller, foreground, system);
    }

    protected override void Apply(TriggerPlan settings) => _controller!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    // Runs after the loop ends, also after a failure, and before a next thread registers the shortcuts.
    protected override void OnStopped()
    {
        _refusals.Set(ShortcutsAndMouseButtonsFeature.Definition.Id, []);
        LogStopped(_logger);
    }

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Shortcuts & Mouse Buttons failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // Removes the shortcuts, the claim and the hooks as the message loop ends.
    private sealed class Opened(TriggerController controller, WinEventHook foreground, WindowsTriggerSystem system) : IDisposable
    {
        public void Dispose()
        {
            foreground.Dispose();
            controller.Dispose();
            system.Dispose();
        }
    }

    // What the system hears of, on this thread, passed to the controller inside the thread's error boundary.
    private sealed class Events(ShortcutsAndMouseButtonsThread thread) : ITriggerEvents
    {
        public void Kept(PointerTrigger trigger) => thread.Guard(() => thread._controller?.Kept(trigger));

        public void FrontMoved() => thread.Guard(() => thread._controller?.FrontMoved());

        public void HookFailed(Exception exception) => thread.Guard(() => thread._controller?.HookFailed(exception));
    }
}
