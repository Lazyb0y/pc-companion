using System;
using System.Text;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ShortcutPauseTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly ShortcutPause _pause;
    private int _pauses;
    private int _resumes;

    public ShortcutPauseTests()
    {
        _pause = new ShortcutPause(_clock, () => _pauses++, () => _resumes++);
    }

    public void Dispose() => _pause.Dispose();

    [Fact]
    public void APauseRequestPausesTheShortcutsOnce()
    {
        Assert.Equal("ok", Answer("pause"));
        Assert.Equal("ok", Answer("pause"));

        Assert.Equal((1, 0), (_pauses, _resumes));
    }

    [Fact]
    public void APauseEndsAfterItsLimit()
    {
        Answer("pause");

        _clock.Advance(ShortcutPause.Limit - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, _resumes);
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, _resumes);
    }

    [Fact]
    public void EachPauseRequestExtendsThePause()
    {
        Answer("pause");
        _clock.Advance(TimeSpan.FromSeconds(5));

        Answer("pause");
        _clock.Advance(TimeSpan.FromSeconds(9));

        Assert.Equal(0, _resumes);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _resumes);
    }

    [Fact]
    public void AResumeRequestEndsThePauseAtOnce()
    {
        Answer("pause");

        Assert.Equal("ok", Answer("resume"));
        _clock.Advance(ShortcutPause.Limit);

        Assert.Equal((1, 1), (_pauses, _resumes));
    }

    [Fact]
    public void ResumingWithoutAPauseDoesNothing()
    {
        Answer("resume");

        Assert.Equal((0, 0), (_pauses, _resumes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PAUSE")]
    [InlineData("pause please")]
    [InlineData("stop")]
    public void AnythingElseChangesNothing(string request)
    {
        Assert.Equal("", Answer(request));

        Assert.Equal((0, 0), (_pauses, _resumes));
    }

    [Fact]
    public void DisposingResumes()
    {
        Answer("pause");

        _pause.Dispose();

        Assert.Equal(1, _resumes);
    }

    private string Answer(string request) => Encoding.UTF8.GetString(_pause.Answer(Encoding.UTF8.GetBytes(request)));
}
