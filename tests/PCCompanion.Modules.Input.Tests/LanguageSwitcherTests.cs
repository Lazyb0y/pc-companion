using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class LanguageSwitcherTests : IDisposable
{
    private const string AppA = @"C:\Apps\A.exe";
    private const string AppB = @"C:\Apps\B.exe";
    private const string AppC = @"C:\Apps\C.exe";
    private const string CommandPrompt = @"C:\Windows\System32\cmd.exe";
    private const nint WindowA = 1;
    private const nint WindowB = 2;
    private const nint WindowC = 3;

    private static readonly nint English = FakeInputLanguageSystem.UnitedStates.Handle;
    private static readonly nint German = FakeInputLanguageSystem.German.Handle;
    private static readonly nint Japanese = FakeInputLanguageSystem.Japanese.Handle;

    private readonly FakeInputLanguageSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<LanguageSwitcher> _logger = new();
    private readonly LanguageSwitcher _switcher;

    public LanguageSwitcherTests()
    {
        _switcher = new LanguageSwitcher(_system, _clock, action => action(), _logger);
        _system.Add(WindowA, 10, 100, AppA);
        _system.Add(WindowB, 20, 200, AppB);
        _system.Add(WindowC, 30, 300, AppC);
    }

    public void Dispose() => _switcher.Dispose();

    [Fact]
    public void AListedAppSwitchesToItsInputMethodWhenItComesToTheFrontAndTheNoticeShowsOnceItHeld()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);

        Assert.Equal([(WindowB, German)], _system.Requests);
        Assert.Empty(_system.Notices);

        Confirm();

        Assert.Equal([FakeInputLanguageSystem.German], _system.Notices);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: a switch from Rule held after 50 ms.");
    }

    [Fact]
    public void StartTakesTheAppInFrontWithoutSwitchingIt()
    {
        Start(WindowA, Rules((AppA, FakeInputLanguageSystem.German)));

        Assert.Empty(_system.Requests);
        Assert.Equal(1, _system.Reads);
    }

    [Fact]
    public void ADialogInFrontIsTheAppOfTheWindowThatOwnsItAndGetsTheRequest()
    {
        _system.Windows.Add(new FakeLanguageWindow { Handle = 4, ProcessId = 20, Thread = 200, Program = AppB, Owner = WindowB });
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(4);
        Confirm();
        Front(WindowB);

        Assert.Equal([(4, German)], _system.Requests);
    }

    [Fact]
    public void TheRequestGoesToTheWindowThatTypingGoesToAndItsThreadIsRead()
    {
        // A Store app's window in its frame belongs to the app's own process.
        _system.Windows.Add(new FakeLanguageWindow { Handle = 7, ProcessId = 70, Thread = 700, AppId = "Store.App", Focus = 8 });
        _system.Add(8, 80, 800, @"C:\Program Files\WindowsApps\Store.App\App.exe").TaskbarButton = false;
        Start(WindowA, new InputLanguagePerAppSettings
        {
            Apps = [new AppLanguage { Name = "Store app", AppId = "Store.App", InputMethod = "0407:00000407" }],
        });

        Front(7);
        Confirm();

        Assert.Equal([(8, German)], _system.Requests);
        Assert.Equal(German, _system.Layouts[800]);
        Assert.Equal([FakeInputLanguageSystem.German], _system.Notices);
    }

    [Fact]
    public void WindowsTheTaskbarDoesntShowArentAppsAndGoingToOneAndBackIsStayingInTheApp()
    {
        _system.Add(9, 90, 900, @"C:\Windows\explorer.exe").TaskbarButton = false;
        Start(WindowA, Rules((AppA, FakeInputLanguageSystem.German)));

        Front(9);
        _system.Layouts[900] = English;
        Front(WindowA);

        Assert.Empty(_system.Requests);
    }

    [Fact]
    public void RememberGivesAnAppBackTheInputMethodItHadWhenTheUserLeftIt()
    {
        _system.Layouts[100] = German;
        Start(WindowA, new InputLanguagePerAppSettings());

        // B gets the language in use, German, and the user switches it to English by hand.
        Front(WindowB);
        _system.Layouts[200] = English;
        Front(WindowA);
        Confirm();
        Front(WindowB);

        Assert.Equal([(WindowA, German), (WindowB, English)], _system.Requests);
        Assert.Equal([FakeInputLanguageSystem.German], _system.Notices);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: a switch from Remembered held after 50 ms.");
    }

    [Fact]
    public void AnAppWithNothingRememberedIsntSwitchedAndItsInputMethodIsRememberedWhenTheUserLeavesIt()
    {
        Start(WindowA, new InputLanguagePerAppSettings());

        Front(WindowB);
        _system.Layouts[200] = German;
        Front(WindowC);

        Assert.Empty(_system.Requests);
        Assert.Equal(FakeInputLanguageSystem.German.Id, _switcher.Remembered.Of(App(AppB, 20)));
        Assert.Equal(FakeInputLanguageSystem.UnitedStates.Id, _switcher.Remembered.Of(App(AppA, 10)));
        Assert.Null(_switcher.Remembered.Of(App(AppC, 30)));
    }

    [Fact]
    public void TheSwitcherSaysWhenWhatsRememberedChanged()
    {
        var changes = 0;
        _switcher.RememberedChanged += (_, _) => changes++;
        Start(WindowA, new InputLanguagePerAppSettings());

        Front(WindowB);
        _switcher.OnSecond();
        _switcher.OnSecond();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void RememberSwitchesNothingUntilTheFileIsRead()
    {
        _switcher.Remembered.WaitForLoad();
        _switcher.Remembered.Remember(App(AppB, 20), FakeInputLanguageSystem.German.Id);
        Start(WindowA, new InputLanguagePerAppSettings());

        Front(WindowB);

        Assert.Empty(_system.Requests);

        _switcher.Remembered.Load([new RememberedLanguage { Program = AppC, InputMethod = "0407:00000407" }]);
        Front(WindowC);

        Assert.Equal([(WindowC, German)], _system.Requests);
    }

    [Fact]
    public void AppsWithARuleArentRemembered()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        Confirm();
        Front(WindowA);

        Assert.Null(_switcher.Remembered.Of(App(AppB, 20)));
        Assert.Equal(1, _switcher.Remembered.Count);
    }

    [Fact]
    public void KeepSwitchesNothingAndRemembersNothing()
    {
        _system.Layouts[100] = German;
        Start(WindowA, new InputLanguagePerAppSettings { OtherApps = OtherApps.Keep });

        Front(WindowB);
        _system.Layouts[200] = English;
        Front(WindowA);

        Assert.Empty(_system.Requests);
        Assert.Equal(0, _switcher.Remembered.Count);
        Assert.False(_system.ReadsEverySecond);
    }

    [Fact]
    public void UseOneLanguageSwitchesEveryAppWithoutARuleAndListedAppsGetTheirRule()
    {
        Start(WindowA, new InputLanguagePerAppSettings
        {
            Apps = [new AppLanguage { Name = "C", Program = AppC, InputMethod = "0411" }],
            OtherApps = OtherApps.Use,
            OtherAppsInputMethod = "0407:00000407",
        });

        Front(WindowB);
        Confirm();
        Front(WindowC);
        Confirm();
        Front(WindowA);

        Assert.Equal([(WindowB, German), (WindowC, Japanese), (WindowA, German)], _system.Requests);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: a switch from OneLanguage held after 50 ms.");
        Assert.Equal(0, _switcher.Remembered.Count);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("040C:0000040C", false)]
    [InlineData("French", true)]
    public void UseOneLanguageWithoutAnInputMethodInTheUsersListSwitchesNothing(string? inputMethod, bool warns)
    {
        Start(WindowA, new InputLanguagePerAppSettings { OtherApps = OtherApps.Use, OtherAppsInputMethod = inputMethod });

        Front(WindowB);

        Assert.Empty(_system.Requests);
        Assert.Equal(warns, _logger.Entries.Any(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public void AHandSwitchHoldsUntilTheUserLeavesAndTheRuleAppliesTheNextTimeTheAppComesToTheFront()
    {
        Start(WindowB, Rules((AppA, FakeInputLanguageSystem.German)));

        Front(WindowA);
        Confirm();
        _system.Layouts[100] = English;
        _switcher.OnForegroundChanged();
        _switcher.OnSecond();

        Assert.Single(_system.Requests);

        Front(WindowB);
        Front(WindowA);

        Assert.Equal([(WindowA, German), (WindowA, German)], _system.Requests);
    }

    [Fact]
    public void ANewWindowOfTheAppInFrontGetsTheAppsInputMethodOnce()
    {
        // With Windows' setting on, a new window starts in the default input method.
        _system.PerWindow = true;
        _system.Layouts[100] = German;
        _system.Add(5, 10, 101, AppA);
        Start(WindowA, new InputLanguagePerAppSettings());

        Front(5);
        Confirm();
        Front(WindowA);
        Front(5);

        Assert.Equal([(5, German)], _system.Requests);
        Assert.Equal([FakeInputLanguageSystem.German], _system.Notices);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: a switch from AppWindow held after 50 ms.");
    }

    [Fact]
    public void ANewWindowGetsTheInputMethodAskedForWhileTheAppHasntTakenItYet()
    {
        _system.PerWindow = true;
        _system.Add(5, 10, 101, AppA);
        _system[WindowA].TakesRequests = false;
        Start(WindowB, Rules((AppA, FakeInputLanguageSystem.German)));

        Front(WindowA);
        Front(5);

        Assert.Equal([(WindowA, German), (5, German)], _system.Requests);
    }

    [Fact]
    public void WithKeepANewWindowOfAnAppWithoutARuleIsntSwitched()
    {
        _system.PerWindow = true;
        _system.Layouts[100] = German;
        _system.Add(5, 10, 101, AppA);
        Start(WindowA, new InputLanguagePerAppSettings { OtherApps = OtherApps.Keep });

        Front(5);

        Assert.Empty(_system.Requests);
    }

    [Fact]
    public void TheForegroundEventComingTwiceAsksOnce()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        _switcher.OnForegroundChanged();
        Confirm();

        Assert.Single(_system.Requests);
        Assert.Single(_system.Notices);
    }

    [Fact]
    public void AnAppThatHasTheRightInputMethodAlreadyIsntAsked()
    {
        _system.PerWindow = true;
        _system.Layouts[200] = German;
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        Confirm();

        Assert.Empty(_system.Requests);
        Assert.Empty(_system.Notices);
    }

    [Fact]
    public void ASwitchTheAppDoesntTakeShowsNoNoticeAndIsntTriedAgainWhileTheAppStaysInFront()
    {
        _system[WindowB].TakesRequests = false;
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        _clock.Advance(TimeSpan.FromMilliseconds(450));

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("didn't take", StringComparison.Ordinal));

        _clock.Advance(TimeSpan.FromSeconds(2));
        _switcher.OnForegroundChanged();

        Assert.Single(_system.Requests);
        Assert.Empty(_system.Notices);
        Assert.Single(_logger.Entries, entry => entry.Message == "Input Language per App: the app didn't take a switch from Rule within 500 ms.");
    }

    [Theory]
    [InlineData(LayoutRequest.Refused, "Input Language per App: Windows refused a switch from Rule, as for an app running as administrator.")]
    [InlineData(LayoutRequest.Failed, "Input Language per App: a switch from Rule couldn't be asked for.")]
    public void ASwitchThatCantBeAskedForIsLoggedWithoutANotice(LayoutRequest answer, string message)
    {
        _system[WindowB].Answer = answer;
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        Confirm();

        Assert.Empty(_system.Notices);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message == message);
    }

    [Fact]
    public void AnAppThatRefusesTheSwitchStillHasItsInputMethodRemembered()
    {
        _system[WindowB].Answer = LayoutRequest.Refused;
        Start(WindowA, new InputLanguagePerAppSettings());
        _switcher.Remembered.Remember(App(AppB, 20), FakeInputLanguageSystem.German.Id);

        Front(WindowB);
        Front(WindowA);

        Assert.Equal([(WindowB, German)], _system.Requests);
        Assert.Equal(FakeInputLanguageSystem.UnitedStates.Id, _switcher.Remembered.Of(App(AppB, 20)));
    }

    [Fact]
    public void ShowNoticeOffShowsNoNotice()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)) with { ShowNotice = false });

        Front(WindowB);
        Confirm();

        Assert.Single(_system.Requests);
        Assert.Empty(_system.Notices);
    }

    [Fact]
    public void AConsoleIsSwitchedWithoutReadsOrANoticeUntilItsHostIsFoundThenThroughItsHost()
    {
        _system.AddConsole(6, 60, 600, hostThread: 601);
        Start(WindowA, Rules((CommandPrompt, FakeInputLanguageSystem.German)) with { OtherApps = OtherApps.Keep });

        Front(6);
        Confirm();

        Assert.Equal([(6, German)], _system.Requests);
        Assert.Equal(German, _system.LayoutOfWindow(6));
        Assert.Empty(_system.Notices);
        Assert.Equal(1, _system.PendingLookups);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: asked a console for a switch from Rule without reading it, since its console host isn't found yet.");

        _system.FinishLookups();
        Front(WindowA);
        _system.Layouts[100] = English;
        Front(6);
        Confirm();

        Assert.Equal([(6, German), (6, German)], _system.Requests);
        Assert.Equal([FakeInputLanguageSystem.German], _system.Notices);
        Assert.Equal(0, _system.PendingLookups);
        Assert.Contains(_logger.Entries, entry => entry.Message.StartsWith("Input Language per App: console host found: True", StringComparison.Ordinal));
    }

    [Fact]
    public void AConsolesInputMethodIsRememberedOnlyOnceItsHostIsFound()
    {
        _system.AddConsole(6, 60, 600, hostThread: 601);
        Start(6, new InputLanguagePerAppSettings());

        _system.Layouts[601] = German;
        Front(WindowA);

        Assert.Null(_switcher.Remembered.Of(App(CommandPrompt, 60)));

        _system.FinishLookups();
        Front(6);
        _system.Layouts[601] = Japanese;
        Front(WindowA);

        Assert.Equal(FakeInputLanguageSystem.Japanese.Id, _switcher.Remembered.Of(App(CommandPrompt, 60)));
    }

    [Fact]
    public void AConsoleWhoseHostIsntFoundIsntLookedUpAgain()
    {
        // Thread 0: the console host can't be found, as if Windows started consoles differently.
        _system.AddConsole(6, 60, 600, hostThread: 0);
        Start(WindowA, Rules((CommandPrompt, FakeInputLanguageSystem.German)) with { OtherApps = OtherApps.Keep });

        Front(6);
        _system.FinishLookups();
        Front(WindowA);
        Front(6);

        Assert.Equal(0, _system.PendingLookups);
        Assert.Equal([(6, German), (6, German)], _system.Requests);
        Assert.Contains(_logger.Entries, entry => entry.Message.StartsWith("Input Language per App: console host found: False", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingSwitchesBeforeTheInputMethodsAreRead()
    {
        _system.ReadsAtOnce = false;
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);

        Assert.Empty(_system.Requests);

        _system.FinishReads();
        Front(WindowA);
        Front(WindowB);

        Assert.Equal([(WindowB, German)], _system.Requests);
    }

    [Fact]
    public void AnInputMethodThatLeftTheUsersListIsntSwitchedToAndTheListIsReadAgain()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));
        _system.ReadsAtOnce = false;
        _system.InputMethods.Remove(FakeInputLanguageSystem.German);

        Front(WindowB);

        Assert.Empty(_system.Requests);
        Assert.Equal(2, _system.Reads);
        Assert.Contains(_logger.Entries, entry => entry.Message == "Input Language per App: the input method of a switch from Rule isn't in the user's list.");
    }

    [Fact]
    public void AnInputMethodAddedToTheUsersListIsUsedOnceRead()
    {
        _system.InputMethods.Remove(FakeInputLanguageSystem.German);
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Front(WindowB);
        _system.InputMethods.Add(FakeInputLanguageSystem.German);
        Front(WindowA);
        Front(WindowB);

        Assert.Equal([(WindowB, German)], _system.Requests);
        Assert.Equal(2, _system.Reads);
    }

    [Fact]
    public void TheListIsReadAgainAfterAReadThatChangedWhileItRan()
    {
        _system.ReadsAtOnce = false;
        Start(WindowA, new InputLanguagePerAppSettings());
        _system.InputMethods.Remove(FakeInputLanguageSystem.Japanese);

        Front(WindowB);

        Assert.Equal(1, _system.Reads);

        _system.FinishReads();

        Assert.Equal(2, _system.Reads);
    }

    [Fact]
    public void AListedAppWhoseIdentifierCantBeReadIsLeftOutWithAWarning()
    {
        Start(WindowA, new InputLanguagePerAppSettings
        {
            Apps = [new AppLanguage { Name = "B", Program = AppB, InputMethod = "German" }],
            OtherApps = OtherApps.Use,
            OtherAppsInputMethod = "0411",
        });

        Front(WindowB);

        Assert.Equal([(WindowB, Japanese)], _system.Requests);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message == "Input Language per App: 1 listed apps have an input method identifier that can't be read, and are left out.");
    }

    [Fact]
    public void ChoosingAnotherBehaviorForOtherAppsForgetsEveryRememberedInputMethod()
    {
        Start(WindowA, new InputLanguagePerAppSettings());
        Front(WindowB);

        Assert.Equal(1, _switcher.Remembered.Count);

        _switcher.Apply(new InputLanguagePerAppSettings { Enabled = true, OtherApps = OtherApps.Use });

        Assert.Equal(0, _switcher.Remembered.Count);
    }

    [Fact]
    public void TheInputMethodIsReadOnceASecondWhileAnAppWithoutARuleIsInFrontAndOtherAppsAreRemembered()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));

        Assert.True(_system.ReadsEverySecond);

        Front(WindowB);

        Assert.False(_system.ReadsEverySecond);

        Confirm();
        Front(WindowA);
        Confirm();

        Assert.True(_system.ReadsEverySecond);

        // An app that closes while in front keeps the input method it had last.
        _system.Layouts[100] = German;
        _switcher.OnSecond();
        _system[WindowA].Closed = true;
        _system.Layouts.Remove(100);
        _switcher.OnSecond();

        Assert.Equal(FakeInputLanguageSystem.German.Id, _switcher.Remembered.Of(App(AppA, 10)));

        _switcher.Apply(new InputLanguagePerAppSettings { Enabled = true, OtherApps = OtherApps.Keep });

        Assert.False(_system.ReadsEverySecond);
    }

    [Fact]
    public void StoppingStopsTheReadsAndTheConfirmation()
    {
        Start(WindowA, Rules((AppB, FakeInputLanguageSystem.German)));
        Front(WindowB);
        Front(WindowA);

        _switcher.Dispose();
        Confirm();

        Assert.False(_system.ReadsEverySecond);
        Assert.Empty(_system.Notices);
    }

    private static InputLanguagePerAppSettings Rules(params (string Program, InputMethod InputMethod)[] rules) => new()
    {
        Apps = [.. rules.Select(rule => new AppLanguage { Name = "App", Program = rule.Program, InputMethod = rule.InputMethod.Id.ToString() })],
    };

    private static AppOfWindow App(string program, uint processId) => new(null, null, program, processId);

    private void Start(nint front, InputLanguagePerAppSettings settings)
    {
        _system.ForegroundWindow = front;
        _switcher.Apply(settings with { Enabled = true });
        _switcher.Start();
    }

    private void Front(nint window)
    {
        _system.BringToFront(window);
        _switcher.OnForegroundChanged();
    }

    private void Confirm() => _clock.Advance(TimeSpan.FromMilliseconds(50));
}
