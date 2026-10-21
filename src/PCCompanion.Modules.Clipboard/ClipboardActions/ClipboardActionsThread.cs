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

    // The thread's own, while its message loop runs.
    private ClipboardRules? _rules;
    private bool _menuOpen;

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
        _runner = new ClipboardActionRunner(commands, () => system.IsAdministrator, system.FileExists, system.StartProcess, logger);
        Start();
    }

    protected override IDisposable? Open(MessageWindow window)
    {
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Windows + Alt + V, for example because another program has it, so Clipboard Actions' menu doesn't open.")]
    private static partial void LogShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions applied its settings: {Usable} of {Rules} rules can be used; Open copied links {OpenLinks}.")]
    private static partial void LogApplied(ILogger logger, int usable, int rules, bool openLinks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' shortcut was pressed; clipboard: {Status}, actions offered: {Offers}.")]
    private static partial void LogPressed(ILogger logger, ClipboardTextStatus status, int offers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions runs rule {Rule} (0 is Open in browser): {Kind}.")]
    private static partial void LogChosen(ILogger logger, int rule, OfferKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions rule {Rule} failed to run.")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, int rule);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
