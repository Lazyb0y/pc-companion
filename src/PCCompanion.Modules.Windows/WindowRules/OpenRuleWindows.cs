using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>An open main window, as the rule editor shows it and as Pick a window fills a rule from it.</summary>
/// <param name="Handle">The window.</param>
/// <param name="Facts">Its app, title and class.</param>
/// <param name="Program">Its program file, to name its app.</param>
/// <param name="Frame">Its visible frame when it's restored.</param>
/// <param name="State">Whether it's maximized or minimized now.</param>
/// <param name="WorkArea">The work area of the display it's on.</param>
public sealed record OpenRuleWindow(nint Handle, WindowFacts Facts, string? Program, Rectangle? Frame, WindowState State, Rectangle? WorkArea);

/// <summary>
/// The open main windows, for the rule editor's list of the windows a rule matches and for Pick a
/// window. See "Making rules" in docs/features/windows/window-rules.md.
/// </summary>
public static class OpenRuleWindows
{
    /// <summary>
    /// The main windows the taskbar shows, front to back, without the windows of the programs in
    /// <paramref name="leftOut"/>, such as PC Companion's. It reads them without waiting for any
    /// program, but reads each one's app, so call it off the UI thread.
    /// </summary>
    public static IReadOnlyList<OpenRuleWindow> Read(IReadOnlyCollection<string> leftOut)
    {
        ArgumentNullException.ThrowIfNull(leftOut);

        var apps = new AppOfWindowReader(new WindowsAppIdentityReader());
        var windows = new List<OpenRuleWindow>();
        foreach (var window in TopLevelWindows.List())
        {
            if (window.IsCloaked || window.IsDesktop || !TopLevelWindows.MayHaveTaskbarButton(window.Handle)
                || TopLevelWindows.PlaceOf(window.Handle) is not { HasFrame: true })
            {
                continue;
            }

            var app = apps.Of(window);
            if (app.Program is { } program && leftOut.Contains(program, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var placement = TopLevelWindows.Placement(window.Handle);
            var frame = placement is { State: WindowState.Normal } ? TopLevelWindows.VisibleBounds(window.Handle)
                : placement is { } other ? RulePlacement.WithoutUsualBorders(other.NormalBounds, TopLevelWindows.Dpi(window.Handle))
                : null;
            windows.Add(new OpenRuleWindow(
                window.Handle,
                new WindowFacts(app, TopLevelWindows.Title(window.Handle), TopLevelWindows.ClassName(window.Handle)),
                app.Program,
                frame,
                placement?.State ?? WindowState.Normal,
                Displays.Of(window.Handle)?.WorkArea));
        }

        return windows;
    }

    /// <summary>
    /// The custom place of <paramref name="frame"/> in <paramref name="workArea"/>, in percent
    /// rounded to tenths, kept inside the work area, as Pick a window fills a rule.
    /// </summary>
    public static RulePlace PlaceOf(Rectangle frame, Rectangle workArea)
    {
        var inside = RulePlacement.Within(frame, workArea);
        double Percent(int part, int whole) => whole <= 0 ? 0 : Math.Round(part * 1000.0 / whole) / 10;
        return new RulePlace
        {
            Which = nameof(PlaceChoice.Custom),
            Left = Math.Min(Percent(inside.Left - workArea.Left, workArea.Width), 99.9),
            Top = Math.Min(Percent(inside.Top - workArea.Top, workArea.Height), 99.9),
            Width = Math.Max(Percent(inside.Width, workArea.Width), 0.1),
            Height = Math.Max(Percent(inside.Height, workArea.Height), 0.1),
        };
    }
}
