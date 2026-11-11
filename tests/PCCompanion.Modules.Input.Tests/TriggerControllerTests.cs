using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class TriggerControllerTests : IDisposable
{
    private const HotKeyModifiers ControlAlt = HotKeyModifiers.Control | HotKeyModifiers.Alt;

    private static readonly FeatureAction HideOthers = new("hideApps", "hideOthers");
    private static readonly AppOfWindow Game = new(null, null, @"C:\Games\Game.exe", 7);
    private static readonly ListedApp ListedGame = new() { Name = "Game", Program = @"C:\Games\Game.exe" };

    private readonly FakeTriggerSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<TriggerControllerTests> _logger = new();
    private readonly TriggerController _controller;

    public TriggerControllerTests()
    {
        _controller = new TriggerController(_system, _system, _clock, action => action(), _logger);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void EachShortcutIsRegisteredByItsKeysPosition()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH"), Shortcut(1, Own(ShortcutsAndMouseButtonsFeature.LockAction), HotKeyModifiers.None, "F13")));

        Assert.Equal([(ControlAlt, 0x48u), (HotKeyModifiers.None, 0x7Cu)], _system.Registered.Keys.OrderBy(key => key.Key));
    }

    [Fact]
    public void APressRunsTheActionThroughTheHostOrItsOwn()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, HotKeyModifiers.Windows, "KeyH"), Shortcut(1, Own(ShortcutsAndMouseButtonsFeature.LockAction), HotKeyModifiers.None, "F13")));

        _system.Press(HotKeyModifiers.Windows, 0x48);
        _system.Press(HotKeyModifiers.None, 0x7C);

        Assert.Equal(["run hideApps.hideOthers", "lock"], _system.Events);
    }

    [Fact]
    public void AnAltShortcutPressesTheUnassignedKeyWhileAltIsHeld()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));
        _system.AltHeld = true;

        _system.Press(ControlAlt, 0x48);

        Assert.Equal(["mask", "run hideApps.hideOthers"], _system.Events);
    }

    [Fact]
    public void WithAltReleasedNoUnassignedKeyIsPressed()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));

        _system.Press(ControlAlt, 0x48);

        Assert.Equal(["run hideApps.hideOthers"], _system.Events);
    }

    [Fact]
    public void ACtrlAltShortcutThatTypesWithAltGrIsntRegistered()
    {
        _system.Layouts.Add(FakeTriggerSystem.German);

        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyE"), Shortcut(1, HideOthers with { ActionId = "hideApp" }, ControlAlt, "KeyH")));

        Assert.Equal([(ControlAlt, 0x48u)], _system.Registered.Keys);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Control+Alt+KeyE", StringComparison.Ordinal));
    }

    [Fact]
    public void ALayoutAddedLaterTakesTheShortcutAwayAtTheNextWindowInFront()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyE")));
        Assert.Single(_system.Registered);

        _system.Layouts.Add(FakeTriggerSystem.German);
        _controller.OnForeground();

        Assert.Empty(_system.Registered);
    }

    [Fact]
    public void WhileAListedAppIsInFrontItsShortcutIsLeftToIt()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH", ListedGame), Shortcut(1, Own(ShortcutsAndMouseButtonsFeature.LockAction), HotKeyModifiers.None, "F13")));
        Assert.Equal(2, _system.Registered.Count);

        _system.Front = Game;
        _controller.OnForeground();
        Assert.Equal([(HotKeyModifiers.None, 0x7Cu)], _system.Registered.Keys);

        _system.Front = new AppOfWindow(null, null, @"C:\Windows\notepad.exe", 9);
        _controller.OnForeground();
        Assert.Equal(2, _system.Registered.Count);
    }

    [Fact]
    public void WithoutListedAppsTheAppInFrontIsntRead()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));

        _controller.OnForeground();

        Assert.Equal(0, _system.FrontReads);
    }

    [Fact]
    public void ARefusedShortcutIsReportedByItsName()
    {
        _system.Taken.Add((ControlAlt, 0x48));

        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));

        Assert.Equal(["Control+Alt+KeyH"], _system.Refused);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Control+Alt+KeyH", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedShortcutIsTriedAgainWithTheNextPlan()
    {
        _system.Taken.Add((ControlAlt, 0x48));
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));

        _system.Taken.Clear();
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH")));

        Assert.Single(_system.Registered);
        Assert.Empty(_system.Refused);
    }

    [Theory]
    [InlineData(ShortcutsAndMouseButtonsFeature.TurnOffDisplaysAction, "displays off")]
    [InlineData(ShortcutsAndMouseButtonsFeature.ScreenSaverAction, "screen saver")]
    public void TurningOffTheDisplaysWaitsUntilTheKeysAreReleased(string action, string done)
    {
        _controller.Apply(Plan(Shortcut(0, Own(action), HotKeyModifiers.None, "F13")));
        _system.KeysHeld = true;

        _system.Press(HotKeyModifiers.None, 0x7C);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(_system.Events);

        _system.KeysHeld = false;
        _clock.Advance(TriggerController.ReleasePoll);
        Assert.Empty(_system.Events);
        _clock.Advance(TriggerController.AfterRelease);

        Assert.Equal([done], _system.Events);
    }

    [Fact]
    public void KeysHeldTooLongDontStopTheAction()
    {
        _controller.Apply(Plan(Shortcut(0, Own(ShortcutsAndMouseButtonsFeature.TurnOffDisplaysAction), HotKeyModifiers.None, "F13")));
        _system.KeysHeld = true;

        _system.Press(HotKeyModifiers.None, 0x7C);
        _clock.Advance(TriggerController.LongestHold + TriggerController.ReleasePoll);
        _clock.Advance(TriggerController.AfterRelease);

        Assert.Equal(["displays off"], _system.Events);
    }

    [Fact]
    public void AnotherPlanRemovesTheShortcutsItDoesntHave()
    {
        _controller.Apply(Plan(Shortcut(0, HideOthers, ControlAlt, "KeyH"), Shortcut(1, HideOthers with { ActionId = "hideApp" }, HotKeyModifiers.Windows, "KeyH")));

        _controller.Apply(Plan(Shortcut(1, HideOthers with { ActionId = "hideApp" }, HotKeyModifiers.Windows, "KeyH")));

        Assert.Equal([(HotKeyModifiers.Windows, 0x48u)], _system.Registered.Keys);
    }

    [Fact]
    public void DisposingRemovesTheShortcutsAndStopsAWait()
    {
        _controller.Apply(Plan(Shortcut(0, Own(ShortcutsAndMouseButtonsFeature.ScreenSaverAction), HotKeyModifiers.None, "F13")));
        _system.Press(HotKeyModifiers.None, 0x7C);

        _controller.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(_system.Registered);
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void MouseButtonsAreClaimedEverywhereWithTheirKeysExceptWhileThePointerIsCaptured()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back), Button(1, HideOthers with { ActionId = "hideApp" }, TriggerButton.Middle, HotKeyModifiers.Windows)));

        var claim = _system.Claimed!;
        Assert.True(claim.Triggers.SetEquals([BackAlone, new PointerTrigger(PointerButton.Middle, PointerKeys.Windows)]));
        Assert.True(claim.NotWhileCaptured);
        Assert.True(claim.Area.Contains(-3000, 2000));
        Assert.Empty(_system.Registered);
    }

    [Fact]
    public void AKeptClickRunsItsActionAfterTheUnassignedKey()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));

        _controller.Kept(BackAlone);
        _controller.Kept(new PointerTrigger(PointerButton.Back, PointerKeys.Control));

        Assert.Equal(["mask", "run hideApps.hideOthers"], _system.Events);
    }

    [Fact]
    public void WhileTheWindowInFrontIsFullScreenNoButtonIsClaimed()
    {
        _system.FullScreen = true;
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));
        Assert.Null(_system.Claimed);
        Assert.True(_system.Watching);

        _system.FullScreen = false;
        _controller.OnForeground();

        Assert.NotNull(_system.Claimed);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("paused", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("resumed", StringComparison.Ordinal));
    }

    [Fact]
    public void WithThePauseOffFullScreenIsntChecked()
    {
        _system.FullScreen = true;

        _controller.Apply(new TriggerPlan([Button(0, HideOthers, TriggerButton.Back)], PauseMouseButtonsInFullScreen: false));
        _controller.OnForeground();

        Assert.NotNull(_system.Claimed);
        Assert.Equal(0, _system.FullScreenReads);
        Assert.False(_system.Watching);
    }

    [Fact]
    public void MovesOfTheWindowInFrontAreCheckedOnceAMomentLater()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));
        var reads = _system.FullScreenReads;
        _system.FullScreen = true;

        _controller.FrontMoved();
        _controller.FrontMoved();
        Assert.NotNull(_system.Claimed);
        _clock.Advance(TriggerController.MoveCheckDelay);

        Assert.Null(_system.Claimed);
        Assert.Equal(reads + 1, _system.FullScreenReads);
    }

    [Fact]
    public void TheWindowsOfAButtonsListedAppsAreLeftOutOfItsClaim()
    {
        _system.Taskbar.Add((5, Game));
        _system.Taskbar.Add((6, new AppOfWindow(null, null, @"C:\Windows\notepad.exe", 9)));

        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back, HotKeyModifiers.None, ListedGame)));
        Assert.True(_system.Claimed!.ExceptIn[BackAlone].SetEquals([5]));

        _system.FrontWindowHandle = 8;
        _system.AppsOfWindows[8] = Game;
        _controller.OnForeground();
        Assert.True(_system.Claimed!.ExceptIn[BackAlone].SetEquals([5, 8]));

        _system.Closed.Add(5);
        _controller.OnForeground();
        Assert.True(_system.Claimed!.ExceptIn[BackAlone].SetEquals([8]));
        Assert.Equal(1, _system.TaskbarReads);
    }

    [Fact]
    public void AnUnchangedClaimIsntMadeAgain()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));

        _controller.OnForeground();
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));

        Assert.Equal(1, _system.Claims);
    }

    [Fact]
    public void AClickTheHookNeverSawSetsItAgainAMomentLater()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));
        _system.Outcomes[77] = PointerInputOutcome.NotSeen;
        _system.Outcomes[78] = PointerInputOutcome.NotSeen;

        _controller.RawPress(PointerButton.Back, 77);
        _controller.RawPress(PointerButton.Forward, 78);
        Assert.Empty(_system.Events);
        _clock.Advance(TriggerController.HookCheckDelay);

        Assert.Equal(["hook again"], _system.Events);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("removed the pointer hook", StringComparison.Ordinal));
    }

    [Fact]
    public void AClickTheHookSawChangesNothing()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));

        _controller.RawPress(PointerButton.Back, 77);
        _clock.Advance(TriggerController.HookCheckDelay);

        Assert.Empty(_system.Events);
    }

    [Fact]
    public void TurningOffTheDisplaysFromAButtonWaitsUntilItsReleased()
    {
        _controller.Apply(Plan(Button(0, Own(ShortcutsAndMouseButtonsFeature.TurnOffDisplaysAction), TriggerButton.Forward)));
        _system.ButtonHeld = true;

        _controller.Kept(new PointerTrigger(PointerButton.Forward, PointerKeys.None));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["mask"], _system.Events);

        _system.ButtonHeld = false;
        _clock.Advance(TriggerController.ReleasePoll);
        _clock.Advance(TriggerController.AfterRelease);

        Assert.Equal(["mask", "displays off"], _system.Events);
    }

    [Fact]
    public void DisposingTakesTheClaimBackAndStopsWatching()
    {
        _controller.Apply(Plan(Button(0, HideOthers, TriggerButton.Back)));

        _controller.Dispose();

        Assert.Null(_system.Claimed);
        Assert.False(_system.Watching);
    }

    private static readonly PointerTrigger BackAlone = new(PointerButton.Back, PointerKeys.None);

    private static PlannedTrigger Button(int index, FeatureAction action, TriggerButton button, HotKeyModifiers modifiers = HotKeyModifiers.None, params ListedApp[] excluded) =>
        new(index, action, new TriggerInput(modifiers, null, button), excluded);

    private static FeatureAction Own(string actionId) => new(ShortcutsAndMouseButtonsFeature.Definition.Id, actionId);

    private static PlannedTrigger Shortcut(int index, FeatureAction action, HotKeyModifiers modifiers, string key, params ListedApp[] excluded) =>
        new(index, action, new TriggerInput(modifiers, key, null), excluded);

    private static TriggerPlan Plan(params PlannedTrigger[] triggers) => new(triggers, PauseMouseButtonsInFullScreen: true);
}
