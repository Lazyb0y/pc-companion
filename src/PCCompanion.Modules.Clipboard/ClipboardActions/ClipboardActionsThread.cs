using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Runs Clipboard Actions on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads"
/// in docs/features/clipboard/clipboard-actions.md says: it registers the shortcut, compiles the
/// rules, and at each press reads the clipboard, matches the rules and shows the menu. The host's
/// UI thread starts it, passes it new settings and stops it, and never waits for it, except for
/// <see cref="Join"/> when the host exits.
/// </summary>
public sealed partial class ClipboardActionsThread : MessageLoopThread<ClipboardActionsSettings>
{
    private const string WindowClassName = "PCCompanion.ClipboardActions";

    private readonly IClipboardActionsSystem _system;
    private readonly IHostCommands _commands;
    private readonly ILogger _logger;
    private readonly ClipboardActionRunner _runner;
    private readonly MenuPlacer _placer;

    // The thread's own, while its message loop runs.
    private ClipboardRules? _rules;
    private bool _pressed;
    private bool _menuOpen;

    // The window that was in front before the menu whose action or Settings was chosen last. The
    // thread's window keeps the front when what that started doesn't come to the front, such as a
    // program without a window, and gives it back at the first key.
    private nint _frontToGiveBack;

    /// <summary>Starts the thread, which registers the shortcut and compiles the rules in <paramref name="settings"/>.</summary>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it registers the shortcut,
    /// which Windows would refuse while the old thread has it.
    /// </param>
    public ClipboardActionsThread(ClipboardActionsSettings settings, IClipboardActionsSystem system, IHostCommands commands, ILogger logger, ClipboardActionsThread? previous = null)
        : base("Clipboard Actions", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _commands = commands;
        _logger = logger;
        _runner = new ClipboardActionRunner(commands, system.FileExists, logger);
        _placer = new MenuPlacer(system);
        Start();
    }

    protected override IDisposable? Open(MessageWindow window)
    {
        _system.WatchTyping(window, () => Guard(() => Typed(window)));
        var shortcut = _system.RegisterShortcut(window);
        if (shortcut is null)
        {
            LogShortcutRefused(_logger);
        }
        else
        {
            shortcut.Pressed += (_, _) => Guard(() => Pressed(window));
        }

        return shortcut;
    }

    protected override void Apply(ClipboardActionsSettings settings)
    {
        _rules = ClipboardRules.Compile(settings, _logger);
        LogApplied(_logger, _rules.Count, settings.Rules.Count, settings.OpenLinks);
    }

    protected override void OnStopping()
    {
        if (_menuOpen)
        {
            PopupMenu.CloseOpenMenu();
        }
    }

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    private void Pressed(MessageWindow window)
    {
        // A press while the menu is open arrives through the menu's own loop; its keys close the
        // menu. One while this press waits for the app's text cursor is left out too.
        if (_pressed || _rules is null)
        {
            return;
        }

        _pressed = true;
        try
        {
            ShowActions(window, _rules);
        }
        finally
        {
            _pressed = false;
        }
    }

    private void ShowActions(MessageWindow window, ClipboardRules rules)
    {
        var position = _placer.Find();
        var read = _system.ReadClipboard(window);
        var text = read.Status == ClipboardTextStatus.Text ? read.Text!.Trim() : null;
        var offers = string.IsNullOrEmpty(text) ? [] : rules.Match(text);
        LogPressed(_logger, read.Status, offers.Count, position.Place);

        var plan = ClipboardMenu.Build(read, offers);
        var previous = _system.FrontWindow();
        uint chosen;
        _menuOpen = true;
        try
        {
            chosen = _system.ShowMenu(window, plan.Items, position.Point, position.KeepClear);
        }
        finally
        {
            _menuOpen = false;
        }

        if (chosen == ClipboardMenu.SettingsCommand)
        {
            RememberFront(window, previous);
            _system.AllowFront();
            _commands.OpenSettings();
        }
        else if (plan.Offers.TryGetValue(chosen, out var offer))
        {
            LogChosen(_logger, offer.Rule, offer.Kind);
            RememberFront(window, previous);
            _system.AllowFront();
            _system.RunInBackground(() => RunGuarded(window, offer, previous));
        }
    }

    // While the thread's window still has the front from an earlier action, the window before that
    // one stays the one to give it back to.
    private void RememberFront(MessageWindow window, nint previous)
    {
        if (previous != window.Handle)
        {
            _frontToGiveBack = previous;
        }
    }

    // A key reached the thread's window, which kept the front after the menu closed because nothing
    // the action started came to the front: typing would go nowhere, so the window that was in front
    // gets it back. That key is lost; the next ones go there. Keys during a press are the menu's.
    private void Typed(MessageWindow window)
    {
        if (_pressed || _frontToGiveBack == 0)
        {
            return;
        }

        LogTypedToWindow(_logger);
        _system.GiveFrontBack(window, _frontToGiveBack);
        _frontToGiveBack = 0;
    }

    // On the thread pool, where an exception wouldn't reach the feature's error boundary. An action
    // that didn't start gives the front back, as Esc does, so typing doesn't go to the hidden window.
    private void RunGuarded(MessageWindow window, ClipboardOffer offer, nint previous)
    {
        var started = false;
        try
        {
            started = _runner.Run(offer);
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, exception, offer.Rule);
        }
        finally
        {
            if (!started)
            {
                _system.GiveFrontBack(window, previous);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Windows + Alt + V, for example because another program has it, so Clipboard Actions' menu doesn't open.")]
    private static partial void LogShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions applied its settings: {Usable} of {Rules} rules can be used; Open copied links {OpenLinks}.")]
    private static partial void LogApplied(ILogger logger, int usable, int rules, bool openLinks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' shortcut was pressed; clipboard: {Status}, actions offered: {Offers}, menu: {Place}.")]
    private static partial void LogPressed(ILogger logger, ClipboardTextStatus status, int offers, MenuPlace place);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions runs rule {Rule} (0 is Open in browser): {Kind}.")]
    private static partial void LogChosen(ILogger logger, int rule, OfferKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A key reached Clipboard Actions' window, which kept the front after its menu, so the window that was in front gets it back.")]
    private static partial void LogTypedToWindow(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions rule {Rule} failed to run.")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, int rule);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
