using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ShortcutKeeperLogTests : IDisposable
{
    private const nint English = 0x04090409;
    private const nint German = 0x04070407;

    private static readonly KeyPositionShortcut AltAboveTab = new(HotKeyModifiers.Alt, ShortcutKeeper.KeyAboveTab);
    private static readonly KeyPositionShortcut AltShiftAboveTab = new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, ShortcutKeeper.KeyAboveTab);

    private readonly Environment _environment = new();
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
        _environment.Taken = AltShiftAboveTab.Modifiers;

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
        _keeper.Configure([AltAboveTab]);
        _environment.Foreground = German;
        _keeper.Check();

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("407", StringComparison.Ordinal));
    }

    // English and German, which put different virtual keys above Tab; other keys mean none.
    private sealed class Environment : IShortcutEnvironment
    {
        public nint Foreground { get; set; } = English;

        public HotKeyModifiers? Taken { get; set; }

        public nint? ForegroundLayout() => Foreground;

        public IReadOnlyList<nint> InstalledLayouts() => [English, German];

        public uint? VirtualKeyOf(uint scanCode, nint layout) =>
            scanCode == ShortcutKeeper.KeyAboveTab ? (layout == German ? 0xDCu : 0xC0u) : null;

        public IDisposable? Register(HotKeyModifiers modifiers, uint virtualKey, Action pressed) =>
            modifiers == Taken ? null : new Registration();

        private sealed class Registration : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
