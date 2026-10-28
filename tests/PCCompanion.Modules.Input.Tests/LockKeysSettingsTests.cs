using System;
using System.Text.Json;
using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

public sealed class LockKeysSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithCapsLockAndNumLockAtTheBottomCenterFor2Seconds()
    {
        var settings = LockKeysSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.True(settings.CapsLock);
        Assert.True(settings.NumLock);
        Assert.False(settings.ScrollLock);
        Assert.True(settings.ShowWhenOff);
        Assert.Equal(NoticePosition.BottomCenter, settings.Position);
        Assert.Equal(2000, settings.DurationMilliseconds);
        Assert.True(settings.IgnoreFullScreenApps);
        Assert.False(settings.KeepCapsLockBadge);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = LockKeysSettings.Read(Section("""
            {
              "enabled": true,
              "capsLock": true,
              "numLock": true,
              "scrollLock": false,
              "showWhenOff": true,
              "position": "BottomCenter",
              "durationMilliseconds": 2000,
              "ignoreFullScreenApps": true,
              "keepCapsLockBadge": false
            }
            """));

        Assert.Equal(LockKeysSettings.Read(null) with { Enabled = true }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(LockKeysSettings.Read(null) with { Enabled = true }, LockKeysSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new LockKeysSettings
        {
            Enabled = true,
            CapsLock = false,
            NumLock = false,
            ScrollLock = true,
            ShowWhenOff = false,
            Position = NoticePosition.TopLeft,
            DurationMilliseconds = 3500,
            IgnoreFullScreenApps = false,
            KeepCapsLockBadge = true,
        };

        var section = settings.Write();

        Assert.Equal(settings, LockKeysSettings.Read(section));
        Assert.Equal("TopLeft", section.GetProperty("position").GetString());
        Assert.True(section.GetProperty("keepCapsLockBadge").GetBoolean());
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(2500, 2500)]
    [InlineData(5000, 5000)]
    [InlineData(60000, 5000)]
    public void ReadKeepsTheDurationInRange(int stored, int expected)
    {
        Assert.Equal(expected, LockKeysSettings.Read(Section($$"""{ "durationMilliseconds": {{stored}} }""")).DurationMilliseconds);
    }

    [Fact]
    public void AnUnknownPositionNumberIsTheBottomCenter()
    {
        Assert.Equal(NoticePosition.BottomCenter, LockKeysSettings.Read(Section("""{ "position": 7 }""")).Position);
        Assert.Equal(NoticePosition.TopCenter, LockKeysSettings.Read(Section("""{ "position": "TopCenter" }""")).Position);
    }

    [Theory]
    [InlineData("""{ "position": "Middle" }""")]
    [InlineData("""{ "capsLock": null }""")]
    [InlineData("""{ "enabled": "yes" }""")]
    public void ReadRejectsASectionThatDoesNotMatch(string json)
    {
        Assert.ThrowsAny<JsonException>(() => LockKeysSettings.Read(Section(json)));
    }

    [Fact]
    public void ShowsTellsWhichKeysShowNotices()
    {
        var settings = new LockKeysSettings { CapsLock = false, NumLock = true, ScrollLock = true };

        Assert.False(settings.Shows(LockKey.CapsLock));
        Assert.True(settings.Shows(LockKey.NumLock));
        Assert.True(settings.Shows(LockKey.ScrollLock));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Shows((LockKey)3));
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new LockKeysSettings { Position = NoticePosition.TopCenter, ScrollLock = true };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
