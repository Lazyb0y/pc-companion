using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Runs Clipboard Actions on a thread of its own, with its own hidden window and message loop, as
/// "Threads" in docs/features/clipboard/clipboard-actions.md says: it registers the shortcut,
/// compiles the rules, and at each press reads the clipboard, matches the rules and shows the
/// menu. The host's UI thread starts it, passes it new settings and stops it, and never waits for
/// it, except for <see cref="Join"/> when the host exits.
/// </summary>
public sealed partial class ClipboardActionsThread
{
    private const string WindowClassName = "PCCompanion.ClipboardActions";

    private readonly IClipboardActionsSystem _system;
    private readonly IHostCommands _commands;
    private readonly ILogger _logger;
    private readonly ClipboardActionsThread? _previous;
    private readonly ClipboardActionRunner _runner;
    private readonly Thread _thread;

    // Shared with the host's UI thread.
    private readonly Lock _gate = new();
    private ClipboardActionsSettings _latest;
    private MessageWindow? _window;
    private bool _stopping;
    private volatile bool _failed;

    // The thread's own, while its message loop runs.
    private ClipboardActionsSettings? _applied;
    private ClipboardRules? _rules;
    private bool _menuOpen;

    /// <summary>Starts the thread, which registers the shortcut and compiles the rules in <paramref name="settings"/>.</summary>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it registers the shortcut,
    /// which Windows would refuse while the old thread has it.
    /// </param>
    public ClipboardActionsThread(ClipboardActionsSettings settings, IClipboardActionsSystem system, IHostCommands commands, ILogger logger, ClipboardActionsThread? previous = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);

        _latest = settings;
        _system = system;
        _commands = commands;
        _logger = logger;
        _previous = previous;
        _runner = new ClipboardActionRunner(commands, () => system.IsAdministrator, system.FileExists, system.StartProcess, logger);

        // A background thread, so one that doesn't end doesn't keep the host running.
        _thread = new Thread(Run) { IsBackground = true, Name = "Clipboard Actions" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Whether the thread stopped because of a failure, which it logged.</summary>
    public bool HasFailed => _failed;

    private ClipboardActionsSettings Latest
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
    public void Update(ClipboardActionsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MessageWindow? window;
        lock (_gate)
        {
            _latest = settings;
            window = _window;
        }

        // Without a window yet, the thread takes the latest settings when it creates one.
        window?.BeginInvoke(() => Guard(() => Apply(Latest)));
    }

    /// <summary>Asks the thread to close its menu, remove the shortcut and end, without waiting for it.</summary>
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
            if (_menuOpen)
            {
                PopupMenu.CloseOpenMenu();
            }

            MessageLoop.Quit();
        });
    }

    /// <summary>Waits for the thread to end. Returns false when it didn't in time.</summary>
    public bool Join(TimeSpan timeout) => _thread.Join(timeout);

    private void Run()
    {
        try
        {
            _previous?.Join(Timeout.InfiniteTimeSpan);

            using var window = new MessageWindow(WindowClassName);
            try
            {
                if (!Attach(window))
                {
                    return;
                }

                using var shortcut = _system.RegisterShortcut(window);
                if (shortcut is null)
                {
                    LogShortcutRefused(_logger);
                }
                else
                {
                    shortcut.Pressed += (_, _) => Guard(() => Pressed(window));
                }

                Apply(Latest);
                LogStarted(_logger);
                MessageLoop.Run();
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

        LogStopped(_logger);
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

    private void Apply(ClipboardActionsSettings settings)
    {
        if (settings == _applied)
        {
            return;
        }

        _applied = settings;
        _rules = ClipboardRules.Compile(settings, _logger);
        LogApplied(_logger, _rules.Count, settings.Rules.Count, settings.OpenLinks);
    }

    private void Pressed(MessageWindow window)
    {
        // A press while the menu is open arrives through the menu's own loop; its keys close the menu.
        if (_menuOpen || _rules is null)
        {
            return;
        }

        var read = _system.ReadClipboard(window);
        var text = read.Status == ClipboardTextStatus.Text ? read.Text!.Trim() : null;
        var offers = string.IsNullOrEmpty(text) ? [] : _rules.Match(text);
        LogPressed(_logger, read.Status, offers.Count);

        var plan = ClipboardMenu.Build(read, offers, _system.IsAdministrator);
        uint chosen;
        _menuOpen = true;
        try
        {
            chosen = _system.ShowMenu(window, plan.Items, _system.PointerPosition());
        }
        finally
        {
            _menuOpen = false;
        }

        if (chosen == ClipboardMenu.SettingsCommand)
        {
            _system.AllowFront();
            _commands.OpenSettings();
        }
        else if (plan.Offers.TryGetValue(chosen, out var offer))
        {
            LogChosen(_logger, offer.Rule, offer.Kind);
            _system.AllowFront();
            _system.RunInBackground(() => RunGuarded(offer));
        }
    }

    // On the thread pool, where an exception wouldn't reach the feature's error boundary.
    private void RunGuarded(ClipboardOffer offer)
    {
        try
        {
            _runner.Run(offer);
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, exception, offer.Rule);
        }
    }

    // Runs on the thread, where an exception would end the host. A failure ends the thread,
    // which removes the shortcut.
    private void Guard(Action action)
    {
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

    private void Fail(Exception exception)
    {
        _failed = true;
        LogFailed(_logger, exception);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Windows + Alt + V, for example because another program has it, so Clipboard Actions' menu doesn't open.")]
    private static partial void LogShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions applied its settings: {Usable} of {Rules} rules can be used; Open copied links {OpenLinks}.")]
    private static partial void LogApplied(ILogger logger, int usable, int rules, bool openLinks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' shortcut was pressed: the clipboard has {Status}, with {Offers} actions.")]
    private static partial void LogPressed(ILogger logger, ClipboardTextStatus status, int offers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions runs rule {Rule} (0 is Open in browser): {Kind}.")]
    private static partial void LogChosen(ILogger logger, int rule, OfferKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions rule {Rule} failed to run.")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, int rule);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
