using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Modules.Display.Tests.TestDisplays;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Display.Tests;

// A Dell display with HDR off as the main display and a Samsung display to its right, saved in
// Windows as they are. Desk is that setup; Small gives the Samsung display a lower resolution;
// Moved puts it to the left; Laptop needs a built-in display that isn't connected.
public sealed class DisplayProfilesFeatureTests : IDisposable
{
    private static readonly string Settings = $"{DisplayProfilesMenu.SettingsItem} {Strings.DisplayProfilesMenuSettings}";
    private static readonly string[] Names = ["Desk", "Small", "Laptop", "DELL", "SAMSUNG"];

    private readonly FakeDisplayProfilesSystem _system = new();
    private readonly FakeHostCommands _commands = new();
    private readonly RecordingLogger<DisplayProfilesFeature> _logger = new();
    private readonly DisplayProfilesFeature _feature;
    private readonly DisplayProfile _desk;
    private readonly DisplayProfile _small;
    private readonly DisplayProfile _moved;
    private readonly DisplayProfile _laptop;
    private readonly List<DisplayPath> _original;

    public DisplayProfilesFeatureTests()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        _system.Displays.Displays.AddRange([main, side]);
        _system.Displays.Current = [Path(main, 0, 0, 0, hdr: DisplayHdr.Off), Path(side, 1, 1920, 0)];
        _system.Displays.Saved = [.. _system.Displays.Current];
        _original = [.. _system.Displays.Current];

        _desk = _system.Displays.Read().ToProfile("Desk");
        _small = _desk with { Name = "Small", Displays = [_desk.Displays[0], _desk.Displays[1] with { Width = 1680, Height = 1050 }] };
        _moved = _desk with { Name = "Moved", Displays = [_desk.Displays[0], _desk.Displays[1] with { X = -1920 }] };
        _laptop = _desk with
        {
            Name = "Laptop",
            Displays = [_desk.Displays[0], _desk.Displays[1] with { Model = "BOE0A1E", Connection = Connected(3, "BOE0A1E").DevicePath, BuiltIn = true }],
        };
        _feature = new DisplayProfilesFeature(_system, _commands, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsOffWithoutASection()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.False(_system.HasWindow);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void TurnedOnItReadsTheDisplaysAndMarksTheProfileInUse()
    {
        _feature.Apply(On(_desk, _small, _laptop));

        Assert.True(_system.HasWindow);
        Assert.Equal(["1 Desk", "2 Small", "3 Laptop", "-", Settings], Menu());

        _system.RunBackground();

        Assert.Equal(["1 Desk (checked)", "2 Small", "3 Laptop (dimmed)", "-", Settings], Menu());
        Assert.Contains((LogLevel.Debug, "The display setup matches display profile 1."), _logger.Entries);
    }

    [Fact]
    public void TurnedOffItLetsGoOfItsWindowAndAddsNoMenu()
    {
        _feature.Apply(On(_desk));
        _system.RunBackground();

        _feature.Apply(Section("""{ "enabled": false, "profiles": [] }"""));

        Assert.False(_feature.IsRunning);
        Assert.False(_system.HasWindow);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void ChoosingAProfileThatChangesAResolutionAsksToKeepIt()
    {
        StartWith(_desk, _small);

        Choose(2);

        var question = Assert.Single(_system.Questions);
        Assert.True(question.IsOpen);
        Assert.Equal(DisplayProfilesFeature.QuestionTime, question.Time);
        Assert.Equal(DisplayProfilesFeature.QuestionTime, _system.Timer?.Delay);
        Assert.Equal(1680, _system.Displays.Current[1].Area.Width);
        Assert.Equal(_original, _system.Displays.Saved);
        Assert.Equal(["1 Desk", "2 Small (checked)", "-", Settings], Menu());
    }

    [Fact]
    public void KeepingTheSwitchSavesIt()
    {
        StartWith(_desk, _small);
        Choose(2);

        _system.Questions[0].Answer(kept: true);
        _system.RunBackground();

        Assert.Equal(_system.Displays.Current, _system.Displays.Saved);
        Assert.Null(_system.Timer);
        Assert.Contains((LogLevel.Debug, "The switch ended: Kept."), _logger.Entries);
    }

    [Fact]
    public void RevertingTheSwitchBringsBackTheSetupFromBefore()
    {
        StartWith(_desk, _small);
        Choose(2);

        _system.Questions[0].Answer(kept: false);
        _system.RunBackground();

        Assert.Equal(_original, _system.Displays.Current);
        Assert.Equal(["1 Desk (checked)", "2 Small", "-", Settings], Menu());
    }

    [Fact]
    public void WithoutAnAnswerInTimeTheSwitchIsReverted()
    {
        StartWith(_desk, _small);
        Choose(2);

        _system.TimeUp();
        _system.RunBackground();

        Assert.False(_system.Questions[0].IsOpen);
        Assert.Equal(_original, _system.Displays.Current);
        Assert.Contains((LogLevel.Debug, "The question wasn't answered in time, so the switch is reverted."), _logger.Entries);
    }

    [Fact]
    public void WhenTheSessionLocksTheSwitchIsReverted()
    {
        StartWith(_desk, _small);
        Choose(2);

        _system.SignalLocked();
        _system.RunBackground();

        Assert.False(_system.Questions[0].IsOpen);
        Assert.Equal(_original, _system.Displays.Current);
    }

    [Fact]
    public void TurningTheFeatureOffRevertsASwitchThatWaits()
    {
        StartWith(_desk, _small);
        Choose(2);

        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_system.Questions[0].IsOpen);
        Assert.Equal(_original, _system.Displays.Current);
        Assert.Equal(_original, _system.Displays.Saved);
    }

    [Fact]
    public void ExitingRevertsASwitchThatsStillRunning()
    {
        StartWith(_desk, _small);
        _feature.ChooseMenuItem(2);

        // The switch hasn't run yet; stopping runs it first, and then reverts it.
        _feature.Dispose();

        Assert.Empty(_system.Questions);
        Assert.Equal(_original, _system.Displays.Current);
    }

    [Fact]
    public void AnotherProfileWhileAskingClosesTheQuestionAndAsksAgain()
    {
        var large = _desk with { Name = "Large", Displays = [_desk.Displays[0], _desk.Displays[1] with { Width = 3840, Height = 2160 }] };
        StartWith(_desk, _small, large);
        Choose(2);

        Choose(3);

        Assert.Equal(2, _system.Questions.Count);
        Assert.False(_system.Questions[0].IsOpen);
        _system.Questions[1].Answer(kept: false);
        _system.RunBackground();
        Assert.Equal(_original, _system.Displays.Current);
    }

    [Fact]
    public void MovingADisplayIsKeptWithoutAsking()
    {
        StartWith(_desk, _moved);

        Choose(2);

        Assert.Empty(_system.Questions);
        Assert.Equal(-1920, _system.Displays.Saved[1].Area.X);
        Assert.Equal(["1 Desk", "2 Moved (checked)", "-", Settings], Menu());
    }

    [Fact]
    public void AProfileWhoseDisplayIsntConnectedChangesNothing()
    {
        StartWith(_desk, _laptop);

        Choose(2);

        Assert.Empty(_system.Questions);
        Assert.Equal(_original, _system.Displays.Current);
        Assert.Contains((LogLevel.Warning, "Display profile 2 can't be used: a display that's on in it isn't connected."), _logger.Entries);
    }

    [Fact]
    public void ASetupWindowsRefusesIsLoggedWithTheDisplay()
    {
        var odd = _desk with { Name = "Odd", Displays = [_desk.Displays[0], _desk.Displays[1] with { Width = 1234, Height = 567 }] };
        StartWith(_desk, odd);

        Choose(2);

        Assert.Empty(_system.Questions);
        Assert.Contains((LogLevel.Warning, "Windows refused display profile 2 (error 1610); displays whose mode Windows doesn't list: 2."), _logger.Entries);
    }

    [Fact]
    public void InARemoteSessionProfilesAreDimmedAndNotSwitched()
    {
        _system.IsRemoteSession = true;
        StartWith(_desk, _small);

        _feature.GetMenuItems();
        _feature.ChooseMenuItem(2);
        _system.RunBackground();

        Assert.Equal([$"0 {Strings.DisplayProfilesMenuRemote} (dimmed)", "1 Desk (dimmed)", "2 Small (dimmed)", "-", Settings], Menu());
        Assert.Equal(0, _system.Reads);
        Assert.Empty(_system.Displays.Calls);
    }

    [Fact]
    public void ProfilesThatCantBeUsedAreLeftOutWithAWarning()
    {
        var broken = _desk with { Name = "Broken", Displays = [_desk.Displays[0] with { X = 10 }, _desk.Displays[1]] };
        StartWith(_desk, broken, _small);

        Assert.Equal(["1 Desk (checked)", "3 Small", "-", Settings], Menu());
        Assert.Contains((LogLevel.Warning, "Display profile 2 can't be used (MainNotAtOrigin), so it's left out of the menu."), _logger.Entries);
    }

    [Fact]
    public void ThePageOpensFromTheMenu()
    {
        StartWith(_desk);

        _feature.GetMenuItems();
        _feature.ChooseMenuItem(DisplayProfilesMenu.SettingsItem);

        Assert.Equal(["displayProfiles"], _commands.FeaturePagesOpened);
    }

    [Fact]
    public void AProfileChangedWhileTheMenuIsOpenIsntApplied()
    {
        StartWith(_desk, _small);
        _feature.GetMenuItems();

        _feature.Apply(On(_desk, _small with { Displays = [_desk.Displays[0], _desk.Displays[1] with { Width = 3840, Height = 2160 }] }));
        _feature.ChooseMenuItem(2);
        _system.RunBackground();

        Assert.Empty(_system.Questions);
        Assert.Contains((LogLevel.Debug, "Display profile 2 was chosen, but there's no such profile any more."), _logger.Entries);
    }

    [Fact]
    public void ADisplayChangeIsReadAndLogged()
    {
        StartWith(_desk, _laptop);
        var laptop = Connected(3, "BOE0A1E", DisplayConnection.BuiltIn);
        _system.Displays.Displays.Add(laptop);

        _system.SignalChange();
        _system.RunBackground();

        Assert.Equal(["1 Desk (checked)", "2 Laptop", "-", Settings], Menu());
        Assert.Contains((LogLevel.Debug, "A display was connected: BOE0A1E on BuiltIn."), _logger.Entries);
    }

    [Fact]
    public void AQuestionThatCantBeShownTurnsTheFeatureOffAndReverts()
    {
        StartWith(_desk, _small);
        Choose(2);

        _system.Questions[0].Fail(new InvalidOperationException("No dialog."));

        Assert.False(_feature.IsRunning);
        Assert.Equal(_original, _system.Displays.Current);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void TheLogNeverHasProfileOrDisplayNames()
    {
        StartWith(_desk, _small, _laptop);
        Choose(2);
        _system.Questions[0].Answer(kept: false);
        _system.RunBackground();

        Assert.DoesNotContain(_logger.Entries, entry => Names.Any(name => entry.Message.Contains(name, StringComparison.Ordinal)));
    }

    private static JsonElement On(params DisplayProfile[] profiles) => new DisplayProfilesSettings { Enabled = true, Profiles = profiles }.Write();

    private void StartWith(params DisplayProfile[] profiles)
    {
        _feature.Apply(On(profiles));
        _system.RunBackground();
    }

    // Chooses a profile as the menu shows it, and lets the switch run.
    private void Choose(int number)
    {
        _feature.GetMenuItems();
        _feature.ChooseMenuItem(number);
        _system.RunBackground();
    }

    private List<string> Menu() => [.. DisplayProfilesMenuTests.Describe(_feature.GetMenuItems())];
}
