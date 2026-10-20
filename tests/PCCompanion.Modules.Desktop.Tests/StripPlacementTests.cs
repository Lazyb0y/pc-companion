using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class StripPlacementTests
{
    private const int Width = 200;
    private const int Gap = 4;
    private static readonly Rectangle Taskbar = FakeStripHost.TaskbarBounds;
    private static readonly Rectangle Tray = FakeStripHost.NotificationArea;

    [Fact]
    public void OnALeftAlignedTaskbarTheStripEndsBeforeTheWidgetsEntry()
    {
        var bounds = StripPlacement.Find(Taskbar, Tray, FakeStripHost.DefaultElements(), Width, Gap);

        Assert.Equal(new Rectangle(FakeStripHost.Widgets.Left - Gap - Width, Taskbar.Top, Width, Taskbar.Height), bounds);
    }

    [Fact]
    public void WithoutTheWidgetsEntryTheStripEndsBeforeTheNotificationArea()
    {
        var elements = FakeStripHost.DefaultElements();
        elements.RemoveAll(element => element.AutomationId == StripPlacement.WidgetsButton);

        var bounds = StripPlacement.Find(Taskbar, Tray, elements, Width, Gap);

        Assert.Equal(Tray.Left - Gap - Width, bounds?.X);
    }

    [Fact]
    public void OnACenteredTaskbarTheWidgetsEntryOnTheLeftCountsWithTheApps()
    {
        List<TaskbarElement> elements =
        [
            new(new Rectangle(0, 1032, 152, 48), StripPlacement.WidgetsButton, "ToggleButton"),
            new(new Rectangle(700, 1032, 44, 48), StripPlacement.StartButton, "ToggleButton"),
            new(new Rectangle(744, 1032, 440, 48), "Window: 0x1", StripPlacement.AppButtonClass),
        ];

        var bounds = StripPlacement.Find(Taskbar, Tray, elements, Width, Gap);

        Assert.Equal(Tray.Left - Gap - Width, bounds?.X);
    }

    [Fact]
    public void OnAMirroredTaskbarTheStripStartsAfterTheNotificationArea()
    {
        var tray = new Rectangle(0, 1032, 246, 48);
        List<TaskbarElement> elements =
        [
            new(new Rectangle(1865, 1032, 55, 48), StripPlacement.StartButton, "ToggleButton"),
            new(new Rectangle(1000, 1032, 865, 48), "Window: 0x1", StripPlacement.AppButtonClass),
        ];

        Assert.Equal(tray.Right + Gap, StripPlacement.Find(Taskbar, tray, elements, Width, Gap)?.X);

        elements.Add(new(new Rectangle(258, 1032, 152, 48), StripPlacement.WidgetsButton, "ToggleButton"));
        Assert.Equal(410 + Gap, StripPlacement.Find(Taskbar, tray, elements, Width, Gap)?.X);
    }

    [Theory]
    [InlineData(1302)] // the strip would start at 1306, 4 pixels after the last app: room
    [InlineData(1303)]
    public void TheStripNeedsItsGapOnBothSides(int appsEnd)
    {
        var elements = FakeStripHost.DefaultElements();
        elements.Add(new(Rectangle.FromLTRB(1188, 1032, appsEnd, 1080), "Window: 0x2", StripPlacement.AppButtonClass));

        var bounds = StripPlacement.Find(Taskbar, Tray, elements, Width, Gap);

        Assert.Equal(appsEnd <= 1302, bounds is not null);
    }

    [Fact]
    public void ElementsOutsideTheTaskbarDontCount()
    {
        var elements = FakeStripHost.DefaultElements();
        elements.Add(new(new Rectangle(1300, 0, 300, 48), "Window: 0x3", StripPlacement.AppButtonClass));

        Assert.NotNull(StripPlacement.Find(Taskbar, Tray, elements, Width, Gap));
    }

    [Fact]
    public void AVerticalTaskbarHasNoRoom()
    {
        Assert.Null(StripPlacement.Find(new Rectangle(0, 0, 62, 1080), null, [], Width, Gap));
    }
}
