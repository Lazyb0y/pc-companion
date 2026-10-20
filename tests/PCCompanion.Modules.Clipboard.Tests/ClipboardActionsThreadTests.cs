using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
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

    private ClipboardActionsThread Start(ClipboardActionsSettings settings, ClipboardActionsThread? previous = null)
    {
        var thread = new ClipboardActionsThread(settings, _system, _commands, _logger, previous);
        _threads.Add(thread);
        return thread;
    }
}
