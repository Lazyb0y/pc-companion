using System;
using System.Drawing;
using System.Globalization;
using PCCompanion.Modules.Desktop.SystemMeters;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// A fake taskbar and strip window; drawing happens in memory.
public sealed class MeterStripTests : IDisposable
{
    private static readonly StripStyle Style = StripStyle.From(new SystemMetersSettings { Enabled = true }, rightToLeft: false);
    private static readonly StripTexts Values = new("42%", "61%", "1.2 Mbps", "80.0 Kbps");
    private static readonly StripColors White = new(0xFFFFFF, false);

    private readonly FakeStripHost _host = new();
    private readonly MeterStrip _strip;

    public MeterStripTests()
    {
        _strip = new MeterStrip(_host);
    }

    public void Dispose() => _strip.Dispose();

    [Theory]
    [InlineData(22620, false)]
    [InlineData(22621, true)]
    [InlineData(26300, true)]
    [InlineData(26301, false)]
    public void TheStripRunsOnlyOnTestedBuilds(int build, bool supported)
    {
        Assert.Equal(supported, MeterStrip.IsSupportedBuild(build));
    }

    [Fact]
    public void TheFirstValuesLayTheStripOutAndShowIt()
    {
        var state = _strip.Show(Style, Values, White, "CPU: 42%");

        Assert.Equal(StripState.Shown, state);
        Assert.Equal(1, _host.TaskbarReads);
        var bounds = _host.Last!.Value.Bounds;
        Assert.Equal(FakeStripHost.Widgets.Left - 4, bounds.Right);
        Assert.Equal(FakeStripHost.TaskbarBounds.Top, bounds.Top);
        Assert.Equal(FakeStripHost.TaskbarBounds.Height, bounds.Height);
        Assert.Equal("CPU: 42%", _host.Last.Value.Description);
    }

    [Fact]
    public void OnlyChangesAreSentToTheWindow()
    {
        _strip.Show(Style, Values, White, "CPU: 42%");

        _strip.Show(Style, Values, White, "CPU: 42%");
        Assert.Equal(1, _host.Shows);

        _strip.Show(Style, Values with { Cpu = "43%" }, White, "CPU: 43%");
        Assert.Equal(2, _host.Shows);
        Assert.Equal(1, _host.TaskbarReads);
    }

    [Fact]
    public void ANewStyleChangesTheStripsWidth()
    {
        _strip.Show(Style, Values, White, "CPU: 42%");
        var width = _host.Last!.Value.Bounds.Width;

        _strip.Show(Style with { Layout = StripLayout.Compact }, Values, White, "CPU: 42%");

        Assert.True(_host.Last!.Value.Bounds.Width < width);
    }

    [Fact]
    public void WithoutATaskbarTheStripCantShow()
    {
        _host.Taskbar = null;

        Assert.Equal(StripState.NoTaskbar, _strip.Show(Style, Values, White, string.Empty));
        Assert.False(_host.IsShown);
    }

    [Fact]
    public void ATaskbarThatDidntAnswerCountsAsNone()
    {
        _host.Elements = null;

        Assert.Equal(StripState.NoTaskbar, _strip.Show(Style, Values, White, string.Empty));
    }

    [Fact]
    public void AnAutoHidingTaskbarHasNoStrip()
    {
        _host.Taskbar = _host.Taskbar! with { IsAutoHide = true };

        Assert.Equal(StripState.AutoHide, _strip.Show(Style, Values, White, string.Empty));
    }

    [Fact]
    public void AFullTaskbarHasNoRoom()
    {
        _host.Elements = [new(new Rectangle(323, 1032, 1300, 48), "Window: 0x1", StripPlacement.AppButtonClass)];

        Assert.Equal(StripState.NoRoom, _strip.Show(Style, Values, White, string.Empty));
        Assert.False(_host.IsShown);
    }

    [Fact]
    public void LayingOutAgainMovesTheStripAndBringsItBack()
    {
        _strip.Show(Style, Values, White, string.Empty);
        _host.Taskbar = null;

        _strip.LayOut();
        Assert.Equal(StripState.NoTaskbar, _strip.State);
        Assert.False(_host.IsShown);

        _host.Taskbar = new(2, 200, FakeStripHost.TaskbarBounds, FakeStripHost.NotificationArea, 96, true, false);
        _host.Elements = [];
        _strip.LayOut();

        Assert.Equal(StripState.Shown, _strip.State);
        Assert.Equal(FakeStripHost.NotificationArea.Left - 4, _host.Last!.Value.Bounds.Right);
    }

    [Fact]
    public void TheStripHidesWithTheTaskbarAndComesBackWithIt()
    {
        _strip.Show(Style, Values, White, string.Empty);

        _host.TaskbarTopmost = false;
        _strip.KeepInSight();
        Assert.Equal(StripState.HiddenWithTaskbar, _strip.State);
        Assert.False(_host.IsShown);
        Assert.Equal(StripState.HiddenWithTaskbar, _strip.Show(Style, Values, White, string.Empty));
        Assert.False(_host.IsShown);

        _host.TaskbarTopmost = true;
        _strip.KeepInSight();
        Assert.Equal(StripState.Shown, _strip.State);
        Assert.True(_host.IsShown);
    }

    [Fact]
    public void TheStripComesBackInFrontOfTheTaskbar()
    {
        _strip.Show(Style, Values, White, string.Empty);

        _strip.KeepInSight();
        Assert.Equal(0, _host.Raises);

        _host.TaskbarInFront = true;
        _strip.KeepInSight();
        Assert.Equal(1, _host.Raises);
    }

    [Fact]
    public void TheHighlightShowsWhileThePointerIsOnTheStrip()
    {
        _strip.Show(Style, Values, White, string.Empty);
        var plain = _host.Last!.Value.Pixels;

        _host.IsPointerOver = true;
        _strip.Redraw();

        Assert.Equal(2, _host.Shows);
        Assert.NotEqual(plain, _host.Last!.Value.Pixels);
    }

    [Fact]
    public void ANewScaleMeasuresTheStripAgain()
    {
        _strip.Show(Style, Values, White, string.Empty);
        var width = _host.Last!.Value.Bounds.Width;

        // Without the Widgets entry and the apps, there's room for the wider strip.
        _host.Taskbar = _host.Taskbar! with { Dpi = 144 };
        _host.Elements = [];
        _strip.LayOut();

        Assert.InRange(_host.Last!.Value.Bounds.Width, width * 1.4, width * 1.6);
    }

    [Fact]
    public void DisposeHidesTheStripAndClosesItsWindow()
    {
        _strip.Show(Style, Values, White, string.Empty);

        _strip.Dispose();

        Assert.False(_host.IsShown);
        Assert.True(_host.IsDisposed);
    }

    [Fact]
    public void TheStripIsMeasuredInTheCurrentLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            _strip.Show(Style, Values, White, string.Empty);
            var english = _host.Last!.Value.Bounds.Width;

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            _strip.Show(Style, Values, White, string.Empty);

            Assert.NotEqual(english, _host.Last!.Value.Bounds.Width);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
