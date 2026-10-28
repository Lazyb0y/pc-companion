using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class LockKeyDetectorTests
{
    private static readonly LockKeyState AllOff = new(false, false, false);
    private static readonly LockKeyState NumOn = new(false, true, false);

    private readonly LockKeyDetector _detector = new();

    [Fact]
    public void TheFirstStateShowsNoNotice()
    {
        Assert.Empty(_detector.Check(new LockKeyState(true, true, true)));
        Assert.Equal(new LockKeyState(true, true, true), _detector.State);
    }

    [Fact]
    public void AChosenKeyTurningOnOrOffShowsANotice()
    {
        _detector.Check(NumOn);

        Assert.Equal([new LockKeyChange(LockKey.CapsLock, true)], _detector.Check(NumOn with { CapsLock = true }));
        Assert.Equal([new LockKeyChange(LockKey.CapsLock, false)], _detector.Check(NumOn));
        Assert.Equal([new LockKeyChange(LockKey.NumLock, false)], _detector.Check(AllOff));
    }

    [Fact]
    public void NoChangeShowsNoNotice()
    {
        _detector.Check(NumOn);

        Assert.Empty(_detector.Check(NumOn));
    }

    [Fact]
    public void ScrollLockShowsNoNoticeUntilItsChosen()
    {
        _detector.Check(AllOff);
        Assert.Empty(_detector.Check(AllOff with { ScrollLock = true }));

        _detector.Configure(new LockKeysSettings { ScrollLock = true });
        Assert.Equal([new LockKeyChange(LockKey.ScrollLock, false)], _detector.Check(AllOff));
    }

    [Fact]
    public void AKeyTurningOffShowsNoNoticeWhenOnlyTurningOnDoes()
    {
        _detector.Configure(new LockKeysSettings { ShowWhenOff = false });
        _detector.Check(NumOn);

        Assert.Empty(_detector.Check(AllOff));
        Assert.Equal([new LockKeyChange(LockKey.CapsLock, true)], _detector.Check(AllOff with { CapsLock = true }));
    }

    [Fact]
    public void KeysThatChangedTogetherAreListedCapsLockFirst()
    {
        _detector.Configure(new LockKeysSettings { ScrollLock = true });
        _detector.Check(NumOn);

        Assert.Equal(
            [new LockKeyChange(LockKey.CapsLock, true), new LockKeyChange(LockKey.NumLock, false), new LockKeyChange(LockKey.ScrollLock, true)],
            _detector.Check(new LockKeyState(true, false, true)));
    }

    [Fact]
    public void AResetTakesTheStateWithoutANotice()
    {
        _detector.Check(NumOn);

        _detector.Reset(new LockKeyState(true, false, false));

        Assert.Empty(_detector.Check(new LockKeyState(true, false, false)));
        Assert.Equal([new LockKeyChange(LockKey.NumLock, true)], _detector.Check(new LockKeyState(true, true, false)));
    }

    [Fact]
    public void NewSettingsKeepTheState()
    {
        _detector.Check(NumOn);

        _detector.Configure(new LockKeysSettings { DurationMilliseconds = 4000 });

        Assert.Empty(_detector.Check(NumOn));
    }

    [Fact]
    public void TheBadgeShowsWhileCapsLockIsOnWhenItsOn()
    {
        _detector.Configure(new LockKeysSettings { KeepCapsLockBadge = true });
        Assert.False(_detector.ShowsBadge);

        _detector.Reset(NumOn with { CapsLock = true });
        Assert.True(_detector.ShowsBadge);

        _detector.Check(NumOn);
        Assert.False(_detector.ShowsBadge);
    }

    [Fact]
    public void TheBadgeNeedsItsSettingAndCapsLocksNotices()
    {
        _detector.Reset(NumOn with { CapsLock = true });
        Assert.False(_detector.ShowsBadge);

        _detector.Configure(new LockKeysSettings { KeepCapsLockBadge = true, CapsLock = false });
        Assert.False(_detector.ShowsBadge);
    }
}
