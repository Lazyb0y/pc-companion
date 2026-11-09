using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

// The feature's thread uses one window class name, so its tests run one at a time.
[CollectionDefinition(nameof(ClipboardActionsThreads))]
public sealed class ClipboardActionsThreads;

// Each test runs the feature's thread with its own hidden window and a fake system, so no
// shortcut is registered, the clipboard isn't read, no menu opens and nothing starts.
[Collection(nameof(ClipboardActionsThreads))]
public sealed class ClipboardActionsThreadTests : IDisposable
{
    private const string Parcel = "1Z999AA10123456784";

    private static readonly ClipboardActionsSettings TrackParcels = new()
    {
        Enabled = true,
        Rules = [new ClipboardRule { Name = "Track the parcel", Pattern = "1Z[0-9A-Z]{16}", Address = "https://tracking.example.com/?number={text}" }],
    };

    private static readonly ClipboardActionsSettings SelectTrackParcels = TrackParcels with { SelectedText = true };

    private readonly FakeClipboardActionsSystem _system = new();
    private readonly FakeHostCommands _commands = new();
    private readonly RecordingLogger<ClipboardActionsThreadTests> _logger = new();
    private readonly List<ClipboardActionsThread> _threads = [];

    public void Dispose()
    {
        foreach (var thread in _threads)
        {
            StopAndJoin(thread);
        }

        // Clipboard Actions never exits PC Companion.
        Assert.Equal(0, _commands.Exits);
    }

    [Fact]
    public void APressShowsTheActionsForTheCopiedTextAndRunsTheChosenOne()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, $"  {Parcel}\r\n");
        _system.Choice = "Track the parcel";
        Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));
        var menu = Assert.Single(_system.Menus);
        Assert.Equal(["‪" + Parcel + "‬", string.Empty, "Track the parcel", string.Empty, Strings.TraySettings], menu.Select(item => item.Text));
        Assert.Equal(["register", "read", "menu at 10,20", "allow front", "background"], _system.Events);
        Assert.Equal([new Uri("https://tracking.example.com/?number=" + Parcel)], _commands.WebPages);
    }

    [Fact]
    public void APressOpensTheMenuBelowTheTextCursorAndLogsWhere()
    {
        _system.Front = new ForegroundTextCursor(42, new Rectangle(300, 200, 1, 18));
        Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Equal(["register", "read", "menu at 300,200 below 300,200,301,218"], _system.Events);
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.EndsWith("menu: AtTextCursor.", StringComparison.Ordinal))));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("300", StringComparison.Ordinal));
    }

    [Fact]
    public void AnActionThatDoesntStartGivesTheFrontBack()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        _system.Choice = "Track the parcel";
        _commands.OpensWebPages = false;
        Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));
        Assert.Equal(["register", "read", "menu at 10,20", "allow front", "front back to 7", "background"], _system.Events);
    }

    // Such as a program without a window, which leaves the front with the feature's hidden window.
    [Fact]
    public void AKeyAfterAnActionThatTookNoFrontGivesTheFrontBack()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        _system.Choice = "Track the parcel";
        var thread = Start(TrackParcels);
        Press();
        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));

        _system.Type();
        _system.Type();
        StopAndJoin(thread);

        Assert.Equal(["register", "read", "menu at 10,20", "allow front", "background"], _system.Events.Take(5));
        Assert.Single(_system.Events, happened => happened == "front back to 7");
        Assert.Contains(_logger.Entries, entry => entry.Message.StartsWith("A key reached Clipboard Actions' window", StringComparison.Ordinal));
    }

    [Fact]
    public void AKeyWithNoActionChosenGivesNothingBack()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        var thread = Start(TrackParcels);
        Press();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));

        _system.Type();
        StopAndJoin(thread);

        Assert.Equal(["register", "read", "menu at 10,20", "type", "dispose shortcut"], _system.Events);
    }

    [Fact]
    public void AnAppWhoseTextCursorRequestFailsGetsTheMenuAtThePointer()
    {
        _system.Front = new ForegroundTextCursor(42, null);
        _system.AppAnswer = Task.FromException<Rectangle?>(new NotImplementedException());
        var thread = Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Equal(["register", "ask 42", "read", "menu at 10,20"], _system.Events);
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void SettingsOpensTheSettingsApp()
    {
        _system.Choice = Strings.TraySettings;
        Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("allow front")));
        Assert.True(_system.WaitUntil(_ => _commands.SettingsOpened == 1));
    }

    [Fact]
    public void ClosingTheMenuWithoutAChoiceRunsNothing()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        Start(TrackParcels);

        Press();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Press();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 2));

        Assert.DoesNotContain("allow front", _system.Events);
        Assert.Empty(_commands.WebPages);
    }

    [Fact]
    public void NewSettingsReachTheThread()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        var thread = Start(TrackParcels with { Rules = [] });
        Assert.True(_system.WaitUntil(system => system.Shortcut is not null));

        thread.Update(TrackParcels);
        Press();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Contains(_system.Menus[0], item => item.Text == "Track the parcel");
    }

    [Fact]
    public void ARefusedShortcutIsLoggedAndTheThreadKeepsRunning()
    {
        _system.RefusesShortcut = true;
        var thread = Start(TrackParcels);

        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.Contains("started on its own thread", StringComparison.Ordinal))));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Windows refused Windows + Alt + V", StringComparison.Ordinal));
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void StoppingRemovesTheShortcut()
    {
        var thread = Start(TrackParcels);
        Assert.True(_system.WaitUntil(system => system.Shortcut is not null));

        StopAndJoin(thread);

        Assert.Equal("dispose shortcut", _system.Events[^1]);
    }

    [Fact]
    public void ANewThreadRegistersTheShortcutAfterTheOldOneRemovedIt()
    {
        var first = Start(TrackParcels);
        Assert.True(_system.WaitUntil(system => system.Shortcut is not null));

        first.Stop();
        var second = Start(TrackParcels, first);

        Assert.True(_system.WaitUntil(system => system.Events.Count(happened => happened == "register") == 2));
        Assert.Equal(["register", "dispose shortcut", "register"], _system.Events);
        Assert.False(second.HasFailed);
    }

    [Fact]
    public void AFailureStopsTheThreadAndIsLogged()
    {
        _system.ReadFailure = new InvalidOperationException("Can't read.");
        var thread = Start(TrackParcels);

        Press();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(thread.HasFailed);
        Assert.Contains((LogLevel.Error, "Clipboard Actions failed, so it's stopped until PC Companion restarts."), _logger.Entries);
        Assert.Equal("dispose shortcut", _system.Events[^1]);
    }

    [Fact]
    public void TheLogNeverHoldsTheCopiedText()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        _system.Choice = "Track the parcel";
        Start(TrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("1Z", StringComparison.Ordinal) || entry.Message.Contains("tracking", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsAltSIsRegisteredOnlyWhileSelectedTextIsOn()
    {
        var thread = Start(TrackParcels);
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.EndsWith("Selected text False.", StringComparison.Ordinal))));

        thread.Update(SelectTrackParcels);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("register selected")));

        thread.Update(TrackParcels);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("dispose selected shortcut")));
        Assert.Equal(["register", "register selected", "dispose selected shortcut"], _system.Events);
    }

    [Fact]
    public void APressOfWindowsAltSShowsTheActionsForTheSelectedTextBelowIt()
    {
        _system.Selection = Task.FromResult(new SelectedTextResult(SelectedTextStatus.Read, $" {Parcel}\r\n", new Rectangle(300, 200, 120, 18)));
        _system.Choice = "Track the parcel";
        Start(SelectTrackParcels);

        PressSelection();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));
        var menu = Assert.Single(_system.Menus);
        Assert.Equal(["‪" + Parcel + "‬", string.Empty, "Track the parcel", string.Empty, Strings.TraySettings], menu.Select(item => item.Text));
        Assert.Equal(["register", "register selected", "create reader", "read selection", "menu at 300,200 below 300,200,420,218", "allow front", "background"], _system.Events);
        Assert.Equal([new Uri("https://tracking.example.com/?number=" + Parcel)], _commands.WebPages);
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.Contains("selected text: Read in", StringComparison.Ordinal) && entry.Message.EndsWith("menu: BelowSelection.", StringComparison.Ordinal))));
    }

    [Fact]
    public void WithoutALineTheSelectedTextsMenuOpensAtTheTextCursor()
    {
        _system.Selection = Task.FromResult(new SelectedTextResult(SelectedTextStatus.Read, Parcel));
        _system.Front = new ForegroundTextCursor(42, new Rectangle(300, 200, 1, 18));
        Start(SelectTrackParcels);

        PressSelection();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Equal("menu at 300,200 below 300,200,301,218", _system.Events[^1]);
    }

    [Theory]
    [InlineData(SelectedTextApp.NotResponding)]
    [InlineData(SelectedTextApp.RunsAsAdministrator)]
    [InlineData(SelectedTextApp.RunsWithoutAdminRights)]
    [InlineData(SelectedTextApp.RunsWithFewerRights)]
    [InlineData(SelectedTextApp.Unknown)]
    public void AnAppThatCantBeReadIsntAskedAndTheMenuSaysWhy(SelectedTextApp app)
    {
        var expected = app switch
        {
            SelectedTextApp.NotResponding => Strings.ClipboardActionsSelectionNotResponding,
            SelectedTextApp.RunsAsAdministrator => Strings.ClipboardActionsSelectionAppAdministrator,
            SelectedTextApp.RunsWithoutAdminRights => Strings.ClipboardActionsSelectionOwnAdministrator,
            SelectedTextApp.RunsWithFewerRights => Strings.ClipboardActionsSelectionFewerRights,
            _ => Strings.ClipboardActionsSelectionNotShared,
        };
        _system.SelectionApp = app;
        Start(SelectTrackParcels);

        PressSelection();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Equal([expected, Strings.ClipboardActionsShowCopied], _system.Menus[0].Take(2).Select(item => item.Text));
        Assert.DoesNotContain("read selection", _system.Events);
    }

    [Theory]
    [InlineData(SelectedTextStatus.NothingSelected, nameof(Strings.ClipboardActionsSelectionNone))]
    [InlineData(SelectedTextStatus.NotShared, nameof(Strings.ClipboardActionsSelectionNotShared))]
    [InlineData(SelectedTextStatus.Password, nameof(Strings.ClipboardActionsSelectionPassword))]
    [InlineData(SelectedTextStatus.TooLong, nameof(Strings.ClipboardActionsSelectionTooLong))]
    [InlineData(SelectedTextStatus.TimedOut, nameof(Strings.ClipboardActionsSelectionNoAnswer))]
    public void WhatTheReaderGivesIsTheMenusNote(SelectedTextStatus status, string note)
    {
        _system.Selection = Task.FromResult(new SelectedTextResult(status));
        Start(SelectTrackParcels);

        PressSelection();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Equal(Strings.ResourceManager.GetString(note, Strings.Culture), _system.Menus[0][0].Text);
    }

    [Fact]
    public void ShowActionsForTheCopiedTextOpensTheirMenuInItsPlace()
    {
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        _system.Choice = Strings.ClipboardActionsShowCopied;
        Start(SelectTrackParcels);

        PressSelection();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 2));
        Assert.Equal("‪" + Parcel + "‬", _system.Menus[1][0].Text);
        Assert.Equal(["register", "register selected", "create reader", "read selection", "menu at 10,20", "front back to 7", "read", "menu at 10,20"], _system.Events);
    }

    // The reader's task never ends, as for an app that doesn't answer.
    [Fact]
    public void AReadThatDoesntEndInTimeSaysSoAndTheAppIsntAskedAgain()
    {
        _system.Selection = new TaskCompletionSource<SelectedTextResult>().Task;
        Start(SelectTrackParcels);

        PressSelection();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        _system.SelectionShortcut!.Press();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 2));
        Assert.All(_system.Menus, menu => Assert.Equal(Strings.ClipboardActionsSelectionNoAnswer, menu[0].Text));
        Assert.Single(_system.Events, happened => happened == "read selection");
    }

    [Fact]
    public void TurningSelectedTextOffEndsTheReader()
    {
        var thread = Start(SelectTrackParcels);
        PressSelection();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));

        thread.Update(TrackParcels);

        Assert.True(_system.WaitUntil(system => system.Events.Contains("dispose reader")));
    }

    [Fact]
    public void StoppingRemovesBothShortcutsAndEndsTheReader()
    {
        var thread = Start(SelectTrackParcels);
        PressSelection();
        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));

        StopAndJoin(thread);

        Assert.Equal(["dispose shortcut", "dispose selected shortcut", "dispose reader"], _system.Events.TakeLast(3));
    }

    [Fact]
    public void ARefusedWindowsAltSIsLoggedAndWindowsAltVStillWorks()
    {
        _system.RefusesSelectionShortcut = true;
        _system.Clipboard = new ClipboardTextResult(ClipboardTextStatus.Text, Parcel);
        Start(SelectTrackParcels);

        Press();

        Assert.True(_system.WaitUntil(system => system.Menus.Count == 1));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Windows refused Windows + Alt + S", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAppsErrorIsLoggedByItsCodeAndTheLogNeverHoldsTheSelectedText()
    {
        _system.Selection = Task.FromResult(new SelectedTextResult(SelectedTextStatus.NotShared, ErrorCode: unchecked((int)0x80004005)));
        Start(SelectTrackParcels);
        PressSelection();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.Contains("error 0x80004005", StringComparison.Ordinal))));

        _system.Selection = Task.FromResult(new SelectedTextResult(SelectedTextStatus.Read, Parcel));
        _system.Choice = "Track the parcel";
        _system.SelectionShortcut!.Press();

        Assert.True(_system.WaitUntil(system => system.Events.Contains("background")));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("1Z", StringComparison.Ordinal) || entry.Message.Contains("tracking", StringComparison.Ordinal));
    }

    private static void StopAndJoin(ClipboardActionsThread thread)
    {
        thread.Stop();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    }

    private void Press()
    {
        Assert.True(_system.WaitUntil(system => system.Shortcut is not null));
        _system.Shortcut!.Press();
    }

    private void PressSelection()
    {
        Assert.True(_system.WaitUntil(system => system.SelectionShortcut is not null));
        _system.SelectionShortcut!.Press();
    }

    private ClipboardActionsThread Start(ClipboardActionsSettings settings, ClipboardActionsThread? previous = null)
    {
        var thread = new ClipboardActionsThread(settings, _system, _commands, _logger, previous);
        _threads.Add(thread);
        return thread;
    }
}
