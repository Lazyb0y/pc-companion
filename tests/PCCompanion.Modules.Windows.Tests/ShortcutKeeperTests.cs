using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Modules.Windows.Tests.FakeShortcutEnvironment;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ShortcutKeeperTests : IDisposable
{
    private const HotKeyModifiers Alt = HotKeyModifiers.Alt;
    private const HotKeyModifiers AltShift = HotKeyModifiers.Alt | HotKeyModifiers.Shift;

    private readonly FakeShortcutEnvironment _environment = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<ShortcutKeeper> _logger = new();
    private readonly ShortcutKeeper _keeper;

    public ShortcutKeeperTests()
    {
        _keeper = new ShortcutKeeper(_environment, _clock, action => action(), _logger);
    }

    public void Dispose() => _keeper.Dispose();

    [Fact]
    public void RegistersTheModifierWithTheKeyAboveTabAndShiftToGoBack()
    {
        _keeper.Configure(Alt);

        Assert.Equal([(Alt, 0xC0u), (AltShift, 0xC0u)], Registered());
    }

    [Fact]
    public void TheWindowsKeyOptionRegistersTheWindowsKey()
    {
        _keeper.Configure(HotKeyModifiers.Windows);

        Assert.Equal([(HotKeyModifiers.Windows, 0xC0u), (HotKeyModifiers.Windows | HotKeyModifiers.Shift, 0xC0u)], Registered());
    }

    [Fact]
    public void PressingTheShortcutsReportsTheirDirection()
    {
        _keeper.Configure(Alt);
        var pressed = new List<SwitchDirection>();
        _keeper.Pressed += (_, direction) => pressed.Add(direction);

        _environment.Press(Alt, 0xC0);
        _environment.Press(AltShift, 0xC0);

        Assert.Equal([SwitchDirection.Forward, SwitchDirection.Back], pressed);
    }

    [Fact]
    public void ALayoutWithAnotherKeyInFrontRegistersThatKey()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);

        _environment.Foreground = German;
        _keeper.Check();

        Assert.Equal([(Alt, 0xDCu), (AltShift, 0xDCu)], Registered());
    }

    [Fact]
    public void ALayoutWithTheSameKeyKeepsTheShortcut()
    {
        _environment.Layouts.Add(Russian);
        _keeper.Configure(Alt);

        _environment.Foreground = Russian;
        _keeper.Check();

        Assert.Equal(2, _environment.Registrations);
    }

    [Fact]
    public void AWindowWhoseLayoutCanNotBeReadKeepsTheShortcut()
    {
        _keeper.Configure(Alt);

        _environment.Foreground = null;
        _keeper.Check();

        Assert.Equal([(Alt, 0xC0u), (AltShift, 0xC0u)], Registered());
    }

    [Fact]
    public void WithoutALayoutInFrontAtFirstTheFirstLayoutIsUsed()
    {
        _environment.Foreground = null;

        _keeper.Configure(Alt);

        Assert.Equal([(Alt, 0xC0u), (AltShift, 0xC0u)], Registered());
    }

    [Fact]
    public void ARefusedShortcutIsLoggedAndNotTriedAgainForTheSameKey()
    {
        _environment.Taken.Add((Alt, 0xC0));
        _keeper.Configure(Alt);

        _keeper.Check();
        _keeper.Check();

        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(1, _environment.Registrations);
    }

    [Fact]
    public void ARefusedShortcutIsTriedAgainWhenTheSettingsApply()
    {
        _environment.Taken.Add((Alt, 0xC0));
        _keeper.Configure(Alt);

        _environment.Taken.Clear();
        _keeper.Configure(Alt);

        Assert.Equal([(Alt, 0xC0u), (AltShift, 0xC0u)], Registered());
    }

    [Fact]
    public void NoModifierMeansNoShortcut()
    {
        _keeper.Configure(Alt);

        _keeper.Configure(null);

        Assert.Empty(Registered());
    }

    [Fact]
    public void LayoutsThatAgreeOnTheKeyAreNotCheckedOnATimer()
    {
        _environment.Layouts.Add(Russian);
        _keeper.Configure(Alt);

        Assert.False(_keeper.ChecksLayout);
    }

    [Fact]
    public void LayoutsThatDisagreeAreCheckedOnceASecond()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);
        Assert.True(_keeper.ChecksLayout);

        // Switched within the same window: nothing reports it.
        _environment.Foreground = German;
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0xC0u, Registered()[0].Key);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(0xDCu, Registered()[0].Key);

        _environment.Foreground = English;
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0xC0u, Registered()[0].Key);
    }

    [Fact]
    public void TheLayoutIsCheckedAgainSoonAfterAnotherWindowComesToTheFront()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);

        // Windows switches the new window's layout a moment after it comes to the front.
        _keeper.Check();
        _environment.Foreground = German;
        _clock.Advance(ShortcutKeeper.FollowUpDelay);

        Assert.Equal(0xDCu, Registered()[0].Key);
    }

    [Fact]
    public void CheckingStopsWhenTheLayoutsAgreeAgain()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);

        _environment.Layouts.Remove(German);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(_keeper.ChecksLayout);
    }

    [Fact]
    public void DisposingUnregistersAndStopsChecking()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);

        _keeper.Dispose();
        _environment.Foreground = German;
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(Registered());
        Assert.False(_keeper.ChecksLayout);
    }

    [Fact]
    public void TheLogNamesNoLayout()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure(Alt);
        _environment.Foreground = German;
        _keeper.Check();

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("407", StringComparison.Ordinal));
    }

    private List<(HotKeyModifiers Modifiers, uint Key)> Registered() =>
        _environment.Registered.Keys.OrderBy(key => key.Modifiers).ToList();
}
