using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Modules.Windows.Tests.FakeShortcutEnvironment;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ShortcutKeeperLogTests : IDisposable
{
    private static readonly KeyPositionShortcut AltAboveTab = new(HotKeyModifiers.Alt, ShortcutKeeper.KeyAboveTab);
    private static readonly KeyPositionShortcut AltShiftAboveTab = new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, ShortcutKeeper.KeyAboveTab);

    private readonly FakeShortcutEnvironment _environment = new();
    private readonly RecordingLogger<ShortcutKeeperLogTests> _logger = new();
    private readonly ShortcutKeeper _keeper;

    public ShortcutKeeperLogTests()
    {
        _keeper = new ShortcutKeeper(_environment, new ManualTimeProvider(), action => action());
        ShortcutKeeperLog.Attach(_keeper, _logger, "App Windows");
    }

    public void Dispose() => _keeper.Dispose();

    [Fact]
    public void ARefusedShortcutIsAWarningThatNamesTheFeatureAndItsKeys()
    {
        _environment.Taken.Add((AltShiftAboveTab.Modifiers, 0xC0));

        _keeper.Configure([AltAboveTab, AltShiftAboveTab]);

        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("App Windows", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Alt, Shift", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegistrationAndAKeyTheLayoutDoesntHaveAreDebugEntries()
    {
        _keeper.Configure([AltAboveTab, new(HotKeyModifiers.Alt, 0x7F)]);

        Assert.Equal(2, _logger.Entries.Count(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("App Windows", StringComparison.Ordinal)));
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
}
