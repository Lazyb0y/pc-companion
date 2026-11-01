using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Modules.Windows.Tests.FakeShortcutEnvironment;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ShortcutKeeperTests : IDisposable
{
    private const HotKeyModifiers Alt = HotKeyModifiers.Alt;
    private const HotKeyModifiers AltShift = HotKeyModifiers.Alt | HotKeyModifiers.Shift;
    private const uint KeyOne = 0x02;

    private static readonly KeyPositionShortcut AltAboveTab = new(Alt, ShortcutKeeper.KeyAboveTab);
    private static readonly KeyPositionShortcut AltShiftAboveTab = new(AltShift, ShortcutKeeper.KeyAboveTab);

    private readonly FakeShortcutEnvironment _environment = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<ShortcutKeeper> _logger = new();
    private readonly ShortcutKeeper _keeper;

    public ShortcutKeeperTests()
    {
        _keeper = new ShortcutKeeper(_environment, _clock, action => action(), _logger, "App Windows");
    }

    public void Dispose() => _keeper.Dispose();

    [Fact]
    public void RegistersEachShortcutWithItsKeysVirtualKey()
    {
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        Assert.Equal([(Alt, 0xC0u), (AltShift, 0xC0u)], Registered());
    }

    [Fact]
    public void TheNumberKeysAreFoundByTheirPosition()
    {
        var control = HotKeyModifiers.Windows | HotKeyModifiers.Control | HotKeyModifiers.Alt;
        _keeper.Configure([new(control, KeyOne), new(control, KeyOne + 8)]);

        Assert.Equal([(control, 0x31u), (control, 0x39u)], Registered().OrderBy(key => key.Key));
    }

    [Fact]
    public void PressingAShortcutReportsWhichOne()
    {
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);
        var pressed = new List<KeyPositionShortcut>();
        _keeper.Pressed += (_, shortcut) => pressed.Add(shortcut);

        _environment.Press(Alt, 0xC0);
        _environment.Press(AltShift, 0xC0);

        Assert.Equal([AltAboveTab, AltShiftAboveTab], pressed);
    }

    [Fact]
    public void AShortcutListedTwiceIsRegisteredOnce()
    {
        _keeper.Configure([AltAboveTab, AltAboveTab]);

        Assert.Equal([(Alt, 0xC0u)], Registered());
    }

    [Fact]
    public void ALayoutWithAnotherKeyInFrontRegistersThatKey()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        _environment.Foreground = German;
        _keeper.Check();

        Assert.Equal([(Alt, 0xDCu), (AltShift, 0xDCu)], Registered());
    }

    [Fact]
    public void ALayoutWithTheSameKeyKeepsTheShortcut()
    {
        _environment.Layouts.Add(Russian);
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        _environment.Foreground = Russian;
        _keeper.Check();

        Assert.Equal(2, _environment.Registrations);
    }

    [Fact]
    public void AWindowWhoseLayoutCanNotBeReadKeepsTheShortcut()
    {
        _keeper.Configure([AltAboveTab]);

        _environment.Foreground = null;
        _keeper.Check();

        Assert.Equal([(Alt, 0xC0u)], Registered());
    }

    [Fact]
    public void WithoutALayoutInFrontAtFirstTheFirstLayoutIsUsed()
    {
        _environment.Foreground = null;

        _keeper.Configure([AltAboveTab]);

        Assert.Equal([(Alt, 0xC0u)], Registered());
    }

    [Fact]
    public void ARefusedShortcutIsLoggedAndNotTriedAgainForTheSameKey()
    {
        _environment.Taken.Add((Alt, 0xC0));
        _keeper.Configure([AltAboveTab]);

        _keeper.Check();
        _keeper.Check();

        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("App Windows", warning.Message, StringComparison.Ordinal);
        Assert.Equal(0, _environment.Registrations);
    }

    [Fact]
    public void ARefusedShortcutIsTheOneLoggedAndTheOthersStillWork()
    {
        _environment.Taken.Add((AltShift, 0xC0));
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("Alt, Shift", warning.Message, StringComparison.Ordinal);
        Assert.Equal([(Alt, 0xC0u)], Registered());
    }

    [Fact]
    public void ARefusedShortcutIsTriedAgainWhenTheSettingsApply()
    {
        _environment.Taken.Add((Alt, 0xC0));
        _keeper.Configure([AltAboveTab]);

        _environment.Taken.Clear();
        _keeper.Configure([AltAboveTab]);

        Assert.Equal([(Alt, 0xC0u)], Registered());
    }

    [Fact]
    public void NoShortcutsUnregistersThemAll()
    {
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        _keeper.Configure([]);

        Assert.Empty(Registered());
    }

    [Fact]
    public void ConfiguringOtherShortcutsReplacesTheEarlierOnes()
    {
        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        _keeper.Configure([new(Alt, KeyOne)]);

        Assert.Equal([(Alt, 0x31u)], Registered());
    }

    [Fact]
    public void LayoutsThatAgreeOnTheKeysAreNotCheckedOnATimer()
    {
        _environment.Layouts.Add(Russian);
        _environment.Layouts.Add(German);
        _keeper.Configure([new(Alt, KeyOne)]);

        Assert.False(_keeper.ChecksLayout);
    }

    [Fact]
    public void LayoutsThatDisagreeAreCheckedOnceASecond()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure([new(Alt, KeyOne), AltAboveTab]);
        Assert.True(_keeper.ChecksLayout);

        // Switched within the same window: nothing reports it.
        _environment.Foreground = German;
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0xC0u, KeyAboveTabRegistered());
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(0xDCu, KeyAboveTabRegistered());

        _environment.Foreground = English;
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0xC0u, KeyAboveTabRegistered());
    }

    [Fact]
    public void TheLayoutIsCheckedAgainSoonAfterAnotherWindowComesToTheFront()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure([AltAboveTab]);

        // Windows switches the new window's layout a moment after it comes to the front.
        _keeper.Check();
        _environment.Foreground = German;
        _clock.Advance(ShortcutKeeper.FollowUpDelay);

        Assert.Equal(0xDCu, KeyAboveTabRegistered());
    }

    [Fact]
    public void CheckingStopsWhenTheLayoutsAgreeAgain()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure([AltAboveTab]);

        _environment.Layouts.Remove(German);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(_keeper.ChecksLayout);
    }

    [Fact]
    public void DisposingUnregistersAndStopsChecking()
    {
        _environment.Layouts.Add(German);
        _keeper.Configure([AltAboveTab]);

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
        _keeper.Configure([AltAboveTab]);
        _environment.Foreground = German;
        _keeper.Check();

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("407", StringComparison.Ordinal));
    }

    private List<(HotKeyModifiers Modifiers, uint Key)> Registered() =>
        _environment.Registered.Keys.OrderBy(key => key.Modifiers).ToList();

    private uint KeyAboveTabRegistered() =>
        Assert.Single(_environment.Registered.Keys, key => key.Key is 0xC0 or 0xDC).Key;
}
