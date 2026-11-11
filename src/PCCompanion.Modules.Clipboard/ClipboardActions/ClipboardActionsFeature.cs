using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Shows actions for the copied text with Windows + Alt + V, and for the selected text with
/// Windows + Alt + S. See docs/features/clipboard/clipboard-actions.md.
/// </summary>
public sealed partial class ClipboardActionsFeature(IClipboardActionsSystem system, IHostCommands commands, ShortcutRefusals refusals, ILogger<ClipboardActionsFeature> logger) : IFeature, IFeatureActions
{
    /// <summary>Windows + Alt + V's name in <see cref="ShortcutRefusals"/>, and the copied text's action ID.</summary>
    public const string CopiedTextShortcut = "copiedText";

    /// <summary>Windows + Alt + S's name in <see cref="ShortcutRefusals"/>, and the selected text's action ID.</summary>
    public const string SelectedTextShortcut = "selectedText";

    // How long the host waits as it exits for the thread to remove the shortcuts.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private ClipboardActionsThread? _thread;
    private ClipboardActionsSettings _settings = new();
    private ClipboardActionsThread? _stopped;
    private bool _failed;

    public ClipboardActionsFeature(IHostCommands commands, ShortcutRefusals refusals, ILogger<ClipboardActionsFeature> logger)
        : this(new WindowsClipboardActionsSystem(), commands, refusals, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("clipboardActions", ClipboardModule.Id, "ClipboardActionsName", "ClipboardActionsDescription", "");

    /// <summary>
    /// Whether this PC can run Clipboard Actions, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    /// <summary>
    /// The actions Clipboard Actions offers to shortcuts and mouse buttons, named in the user's
    /// language: the menu for the copied text, and for the selected text while Selected text is on.
    /// </summary>
    public static IReadOnlyList<OfferedAction> OfferedActions(ClipboardActionsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var copied = new OfferedAction(new FeatureAction(Definition.Id, CopiedTextShortcut), Strings.ClipboardActionsActionCopiedText);
        return settings.SelectedText
            ? [copied, new OfferedAction(new FeatureAction(Definition.Id, SelectedTextShortcut), Strings.ClipboardActionsActionSelectedText)]
            : [copied];
    }

    /// <summary>
    /// The shortcuts Clipboard Actions registers with <paramref name="settings"/> while it's on,
    /// each with the action it runs: the selected text's only while Selected text is on.
    /// </summary>
    public static IReadOnlyList<(FeatureAction Action, ShortcutKeys Keys)> Shortcuts(ClipboardActionsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var shortcuts = new List<(FeatureAction, ShortcutKeys)>(2);
        if (settings.Shortcut == ClipboardActionsShortcut.WindowsAltV)
        {
            shortcuts.Add((new FeatureAction(Definition.Id, CopiedTextShortcut), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows | HotKeyModifiers.Alt, WindowsClipboardActionsSystem.VirtualKeyV)));
        }

        if (settings.SelectedText && settings.SelectedTextShortcut == ClipboardActionsSelectionShortcut.WindowsAltS)
        {
            shortcuts.Add((new FeatureAction(Definition.Id, SelectedTextShortcut), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows | HotKeyModifiers.Alt, WindowsClipboardActionsSystem.VirtualKeyS)));
        }

        return shortcuts;
    }

    public bool CanRun(FeatureAction action) =>
        _thread is not null
        && action.FeatureId == Definition.Id
        && action.Target is null
        && (action.ActionId == CopiedTextShortcut || (action.ActionId == SelectedTextShortcut && _settings.SelectedText));

    public void Run(FeatureAction action)
    {
        if (!CanRun(action))
        {
            return;
        }

        if (action.ActionId == CopiedTextShortcut)
        {
            _thread?.ShowCopiedText();
        }
        else
        {
            _thread?.ShowSelectedText();
        }
    }

    public void Apply(JsonElement? settings)
    {
        // A failure on the feature's thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        ClipboardActionsSettings read;
        try
        {
            read = ClipboardActionsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        _settings = read;

        if (_thread is null)
        {
            _thread = new ClipboardActionsThread(read, system, commands, logger, _stopped, refusals);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    // Doesn't wait: a new thread waits for this one before it registers the shortcuts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Clipboard Actions settings can't be read, so Clipboard Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions didn't stop within {Seconds} s, for example while it waited for the app that copied the text.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
