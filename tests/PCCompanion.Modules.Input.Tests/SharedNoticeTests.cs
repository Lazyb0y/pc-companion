using System;
using System.Drawing;
using PCCompanion.Modules.Input.Notice;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// The notice's window uses one window class name, so the tests that open it run one at a time.
[CollectionDefinition(nameof(NoticeWindows))]
public sealed class NoticeWindows;

// Creates the notice's hidden window only; no test shows it.
[Collection(nameof(NoticeWindows))]
public sealed class SharedNoticeTests : IDisposable
{
    private readonly SharedNotice _notice = new(TimeProvider.System);
    private readonly object _lockKeys = new();
    private readonly object _inputLanguages = new();

    public void Dispose() => _notice.Dispose();

    [Fact]
    public void TheWindowOpensForTheFirstFeatureAndClosesAfterTheLast()
    {
        Assert.False(_notice.IsOpen);

        _notice.Acquire(_lockKeys);
        _notice.Acquire(_inputLanguages);
        _notice.Acquire(_lockKeys);
        _notice.Release(_lockKeys);

        Assert.True(_notice.IsOpen);

        _notice.Release(_inputLanguages);

        Assert.False(_notice.IsOpen);
    }

    [Fact]
    public void AFeatureShowsTheNoticeOnlyAfterItTookIt()
    {
        _notice.Acquire(_lockKeys);
        var display = new DisplayDetails(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), 96);

        Assert.Throws<InvalidOperationException>(() =>
            _notice.Show(_inputLanguages, new NoticeImage(1, 1, [0]), display, NoticePosition.BottomCenter, "German (Germany)", TimeSpan.FromSeconds(2), fadesOut: true, action => action()));
        Assert.False(_notice.IsShownBy(_inputLanguages));
    }

    [Fact]
    public void HidingANoticeThatDoesntShowDoesNothing()
    {
        _notice.Acquire(_lockKeys);

        _notice.Hide(_lockKeys);

        Assert.False(_notice.IsShownBy(_lockKeys));
        Assert.True(_notice.IsOpen);
    }
}
