using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Where the user last left one of a rule's windows, for one set of displays: the display, as
/// rules keep a chosen one, and the frame in fractions of its work area. See "Remembering places"
/// in docs/features/windows/window-rules.md.
/// </summary>
public sealed record RememberedPlace
{
    /// <summary>The set of displays that were connected, as <see cref="IRulesSystem.DisplaySet"/> names it.</summary>
    public string Displays { get; set; } = string.Empty;

    /// <summary>The display the window was on.</summary>
    public RuleDisplay? Display { get; set; }

    /// <summary>The frame's left edge, as a fraction of the work area's width from its left.</summary>
    public double Left { get; set; }

    /// <summary>The frame's top edge, as a fraction of the work area's height from its top.</summary>
    public double Top { get; set; }

    /// <summary>The frame's width, as a fraction of the work area's width.</summary>
    public double Width { get; set; }

    /// <summary>The frame's height, as a fraction of the work area's height.</summary>
    public double Height { get; set; }

    /// <summary>Whether the window was maximized, with the frame as its normal place.</summary>
    public bool Maximized { get; set; }

    /// <summary>When it was remembered, as a number that grows, so the one used longest ago goes first.</summary>
    public long Used { get; set; }

    /// <summary>The frame in <paramref name="workArea"/>.</summary>
    public Rectangle FrameIn(Rectangle workArea) => Rectangle.FromLTRB(
        workArea.Left + (int)Math.Round(workArea.Width * Left),
        workArea.Top + (int)Math.Round(workArea.Height * Top),
        workArea.Left + (int)Math.Round(workArea.Width * (Left + Width)),
        workArea.Top + (int)Math.Round(workArea.Height * (Top + Height)));

    // A place read from the file that can be used: fractions inside the work area, a size above
    // zero, and a display to find it on.
    internal bool IsUsable =>
        Display is { } display && (display.Model.Length > 0 || display.Connection.Length > 0)
        && double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
        && Left is >= 0 and < 1 && Top is >= 0 and < 1 && Width is > 0 and <= 1 && Height is > 0 and <= 1;
}

/// <summary>What a rule remembers: its places, for each set of displays, and the opacity the wheel last set.</summary>
public sealed record RememberedRule
{
    /// <summary>The rule's key, <see cref="RememberedPlaces.KeyOf"/>.</summary>
    public string Rule { get; set; } = string.Empty;

    public IReadOnlyList<RememberedPlace?> Places { get; set; } = [];

    /// <summary>The opacity in percent Title Bar Actions' wheel last gave one of the rule's windows.</summary>
    public int? Opacity { get; set; }
}

/// <summary>
/// The places and opacities rules remember, in memory, for <c>window-rules.json</c>. Use it on the
/// feature's thread only.
/// </summary>
public sealed class RememberedPlaces
{
    /// <summary>The most sets of displays a rule keeps a place for; the one used longest ago goes.</summary>
    public const int PlacesPerRule = 10;

    private readonly Dictionary<string, Remembered> _rules = new(StringComparer.Ordinal);
    private long _used;

    /// <summary>Raised when what's remembered changed, so it's written to the file.</summary>
    public event EventHandler? Changed;

    /// <summary>What's remembered, for the file.</summary>
    public IReadOnlyList<RememberedRule> Saved =>
    [
        .. _rules.Select(pair => new RememberedRule
        {
            Rule = pair.Key,
            Places = [.. pair.Value.Places],
            Opacity = pair.Value.Opacity,
        }),
    ];

    /// <summary>The key a rule's places are kept by: its ID, or else its name, as in a hand-written rule.</summary>
    public static string KeyOf(WindowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Id.Length > 0 ? rule.Id : "name:" + rule.Name;
    }

    /// <summary>
    /// Adds what the file kept. Entries that can't be used, as after a hand edit, are left out, and
    /// each rule keeps its newest places up to the limit. It doesn't raise <see cref="Changed"/>.
    /// </summary>
    public void Load(IEnumerable<RememberedRule?> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            if (rule is null || rule.Rule.Length == 0 || _rules.ContainsKey(rule.Rule))
            {
                continue;
            }

            var places = rule.Places.OfType<RememberedPlace>().Where(place => place.IsUsable)
                .OrderByDescending(place => place.Used).Take(PlacesPerRule).ToList();
            int? opacity = rule.Opacity is >= RuleOpacity.Lowest and <= 100 ? rule.Opacity : null;
            if (places.Count > 0 || opacity is not null)
            {
                _rules[rule.Rule] = new Remembered(places, opacity);
                _used = Math.Max(_used, places.Select(place => place.Used).DefaultIfEmpty().Max());
            }
        }
    }

    /// <summary>
    /// Remembers where one of the rule's windows is: <paramref name="frame"/> on the display
    /// <paramref name="display"/> with the work area <paramref name="workArea"/>, for the set of
    /// displays <paramref name="displays"/>.
    /// </summary>
    public void Remember(string rule, string displays, RuleDisplay display, Rectangle frame, Rectangle workArea, bool maximized)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(display);

        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        var inside = RulePlacement.Within(frame, workArea);
        var place = new RememberedPlace
        {
            Displays = displays,
            Display = display with { Which = nameof(DisplayChoice.Chosen) },
            Left = (inside.Left - workArea.Left) / (double)workArea.Width,
            Top = (inside.Top - workArea.Top) / (double)workArea.Height,
            Width = inside.Width / (double)workArea.Width,
            Height = inside.Height / (double)workArea.Height,
            Maximized = maximized,
            Used = ++_used,
        };

        var remembered = _rules.GetValueOrDefault(rule) ?? new Remembered([], null);
        var before = remembered.Places.FirstOrDefault(old => old.Displays == displays);
        if (before is not null && before with { Used = place.Used } == place)
        {
            return;
        }

        var places = remembered.Places.Where(old => old.Displays != displays).Prepend(place).Take(PlacesPerRule).ToList();
        _rules[rule] = remembered with { Places = places };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where one of the rule's windows was left last for the set of displays, or <see langword="null"/>.</summary>
    public RememberedPlace? Find(string rule, string displays) =>
        _rules.GetValueOrDefault(rule)?.Places.FirstOrDefault(place => place.Displays == displays);

    /// <summary>Remembers the opacity the wheel gave one of the rule's windows.</summary>
    public void RememberOpacity(string rule, int percent)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var remembered = _rules.GetValueOrDefault(rule) ?? new Remembered([], null);
        if (remembered.Opacity == percent)
        {
            return;
        }

        _rules[rule] = remembered with { Opacity = percent };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The opacity the wheel last gave one of the rule's windows, or <see langword="null"/>.</summary>
    public int? OpacityOf(string rule) => _rules.GetValueOrDefault(rule)?.Opacity;

    /// <summary>Forgets what rules other than <paramref name="rules"/> remember, as when they're deleted.</summary>
    public void Keep(IReadOnlySet<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var gone = _rules.Keys.Where(rule => !rules.Contains(rule)).ToList();
        foreach (var rule in gone)
        {
            _rules.Remove(rule);
        }

        if (gone.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record Remembered(IReadOnlyList<RememberedPlace> Places, int? Opacity);
}
