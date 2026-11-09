using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Runs Clipboard Actions on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads"
/// in docs/features/clipboard/clipboard-actions.md says: it registers the shortcuts, compiles the
/// rules, and at each press reads the clipboard or the selected text, matches the rules and shows
/// the menu. The host's UI thread starts it, passes it new settings and stops it, and never waits
/// for it, except for <see cref="Join"/> when the host exits.
/// </summary>
public sealed partial class ClipboardActionsThread : MessageLoopThread<ClipboardActionsSettings>
{
    /// <summary>How long a press of Windows + Alt + S waits for the selected text.</summary>
    public static readonly TimeSpan SelectionLimit = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How long a browser that is turning on its accessibility support is asked again, so that its
    /// last answer still comes within <see cref="SelectionLimit"/>.
    /// </summary>
    public static readonly TimeSpan SelectionRetryFor = TimeSpan.FromSeconds(1.3);

    private const string WindowClassName = "PCCompanion.ClipboardActions";

    private readonly IClipboardActionsSystem _system;
    private readonly IHostCommands _commands;
    private readonly ILogger _logger;
    private readonly ClipboardActionRunner _runner;
    private readonly MenuPlacer _placer;

    // The thread's own, while its message loop runs.
    private MessageWindow? _window;
    private IShortcut? _copiedShortcut;
    private IShortcut? _selectionShortcut;
    private ISelectionReader? _reader;
    private Task<SelectedTextResult>? _reading;
    private ClipboardRules? _rules;
    private bool _pressed;
    private bool _menuOpen;

    // The window that was in front before the menu whose action or Settings was chosen last. The
    // thread's window keeps the front when what that started doesn't come to the front, such as a
    // program without a window, and gives it back at the first key.
    private nint _frontToGiveBack;

    /// <summary>Starts the thread, which registers the shortcuts and compiles the rules in <paramref name="settings"/>.</summary>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it registers the shortcuts,
    /// which Windows would refuse while the old thread has them.
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
        _window = window;
        _system.WatchTyping(window, () => Guard(() => Typed(window)));
        _copiedShortcut = _system.RegisterShortcut(window, ClipboardShortcut.CopiedText);
        if (_copiedShortcut is null)
        {
            LogShortcutRefused(_logger);
        }
        else
        {
            _copiedShortcut.Pressed += (_, _) => Guard(() => Pressed(window));
        }

        return new Opened(this);
    }

    protected override void Apply(ClipboardActionsSettings settings)
    {
        _rules = ClipboardRules.Compile(settings, _logger);
        UpdateSelectionShortcut(settings.SelectedText);
        LogApplied(_logger, _rules.Count, settings.Rules.Count, settings.OpenLinks, settings.SelectedText);
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

    // Windows + Alt + S is registered only while Selected text is on, and turning it off also ends
    // the reader's thread.
    private void UpdateSelectionShortcut(bool on)
    {
        if (on && _selectionShortcut is null && _window is { } window)
        {
            _selectionShortcut = _system.RegisterShortcut(window, ClipboardShortcut.SelectedText);
            if (_selectionShortcut is null)
            {
                LogSelectionShortcutRefused(_logger);
            }
            else
            {
                _selectionShortcut.Pressed += (_, _) => Guard(() => PressedSelection(window));
            }
        }
        else if (!on)
        {
            _selectionShortcut?.Dispose();
            _selectionShortcut = null;
            _reader?.Dispose();
            _reader = null;
            _reading = null;
        }
    }

    // Removes the shortcuts and ends the reader's thread as the message loop ends.
    private void Close()
    {
        _copiedShortcut?.Dispose();
        _selectionShortcut?.Dispose();
        _reader?.Dispose();
        _copiedShortcut = _selectionShortcut = null;
        _reader = null;
    }

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
            ShowActions(window, _rules, null);
        }
        finally
        {
            _pressed = false;
        }
    }

    private void PressedSelection(MessageWindow window)
    {
        // As for Windows + Alt + V; a press while this one waits for the selected text is left out.
        if (_pressed || _rules is null)
        {
            return;
        }

        _pressed = true;
        try
        {
            ShowSelectionActions(window, _rules);
        }
        finally
        {
            _pressed = false;
        }
    }

    // The copied text's menu, at the place found now, or where the selected text's menu was.
    private void ShowActions(MessageWindow window, ClipboardRules rules, MenuPosition? at)
    {
        var position = at ?? _placer.Find();
        var read = _system.ReadClipboard(window);
        var text = read.Status == ClipboardTextStatus.Text ? read.Text!.Trim() : null;
        var offers = string.IsNullOrEmpty(text) ? [] : rules.Match(text);
        LogPressed(_logger, read.Status, offers.Count, position.Place);

        ShowAndRun(window, ClipboardMenu.Build(read, offers), position, _system.FrontWindow());
    }

    // The text is read before the thread's window comes to the front for the menu, which would
    // move the keyboard focus away from the selection.
    private void ShowSelectionActions(MessageWindow window, ClipboardRules rules)
    {
        var started = Stopwatch.GetTimestamp();
        var (status, text, line) = ReadSelection();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var selected = status == SelectionStatus.Read ? text?.Trim() : null;
        var offers = string.IsNullOrEmpty(selected) ? [] : rules.Match(selected);
        var position = line is { } below ? new MenuPosition(below.Location, below, MenuPlace.BelowSelection) : _placer.Find();
        LogSelectionPressed(_logger, status, (long)elapsed.TotalMilliseconds, offers.Count, position.Place);

        var previous = _system.FrontWindow();
        if (ShowAndRun(window, ClipboardMenu.BuildForSelection(status, text, offers), position, previous) == ClipboardMenu.ShowCopiedCommand)
        {
            // The app gets the front back first, so the copied text's menu gives it back to the app too.
            _system.GiveFrontBack(window, previous);
            ShowActions(window, rules, position);
        }
    }

    private (SelectionStatus Status, string? Text, Rectangle? LastLine) ReadSelection()
    {
        var app = _system.CheckSelectionApp(_system.ReadTextCursor().Window);
        if (app != SelectedTextApp.Readable)
        {
            return (StatusOf(app), null, null);
        }

        // A read that its app hasn't answered still holds the reader; the app isn't asked again.
        if (_reading is { IsCompleted: false })
        {
            return (SelectionStatus.NoAnswer, null, null);
        }

        _reading = null;
        _reader ??= _system.CreateSelectionReader();
        var reading = _reader.ReadAsync(SelectionRetryFor);
        if (!reading.Wait(SelectionLimit))
        {
            _reading = reading;
            return (SelectionStatus.NoAnswer, null, null);
        }

        var result = reading.Result;
        if (result.ErrorCode != 0)
        {
            LogSelectionError(_logger, result.ErrorCode);
        }

        return (StatusOf(result.Status), result.Text, result.LastLine);
    }

    private static SelectionStatus StatusOf(SelectedTextApp app) => app switch
    {
        SelectedTextApp.NoWindow => SelectionStatus.NothingSelected,
        SelectedTextApp.NotResponding => SelectionStatus.NotResponding,
        SelectedTextApp.RunsAsAdministrator => SelectionStatus.AppRunsAsAdministrator,
        SelectedTextApp.RunsWithoutAdminRights => SelectionStatus.RunsAsAdministrator,
        SelectedTextApp.RunsWithFewerRights => SelectionStatus.AppHasFewerRights,
        _ => SelectionStatus.NotShared,
    };

    private static SelectionStatus StatusOf(SelectedTextStatus status) => status switch
    {
        SelectedTextStatus.Read => SelectionStatus.Read,
        SelectedTextStatus.NothingSelected => SelectionStatus.NothingSelected,
        SelectedTextStatus.Password => SelectionStatus.Password,
        SelectedTextStatus.TooLong => SelectionStatus.TooLong,
        SelectedTextStatus.TimedOut => SelectionStatus.NoAnswer,
        _ => SelectionStatus.NotShared,
    };

    // Shows the menu and runs what was chosen. Returns the chosen item's ID, or 0.
    private uint ShowAndRun(MessageWindow window, ClipboardMenuPlan plan, MenuPosition position, nint previous)
    {
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

        return chosen;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Windows + Alt + S, for example because another program has it, so Clipboard Actions' menu for the selected text doesn't open.")]
    private static partial void LogSelectionShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions applied its settings: {Usable} of {Rules} rules can be used; Open copied links {OpenLinks}; Selected text {SelectedText}.")]
    private static partial void LogApplied(ILogger logger, int usable, int rules, bool openLinks, bool selectedText);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' shortcut was pressed; clipboard: {Status}, actions offered: {Offers}, menu: {Place}.")]
    private static partial void LogPressed(ILogger logger, ClipboardTextStatus status, int offers, MenuPlace place);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions' shortcut for the selected text was pressed; selected text: {Status} in {Milliseconds} ms, actions offered: {Offers}, menu: {Place}.")]
    private static partial void LogSelectionPressed(ILogger logger, SelectionStatus status, long milliseconds, int offers, MenuPlace place);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The app in front answered with error 0x{ErrorCode:X8} when its selected text was read.")]
    private static partial void LogSelectionError(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions runs rule {Rule} (0 is Open in browser): {Kind}.")]
    private static partial void LogChosen(ILogger logger, int rule, OfferKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A key reached Clipboard Actions' window, which kept the front after its menu, so the window that was in front gets it back.")]
    private static partial void LogTypedToWindow(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions rule {Rule} failed to run.")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, int rule);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard Actions failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    private sealed class Opened(ClipboardActionsThread thread) : IDisposable
    {
        public void Dispose() => thread.Close();
    }
}
