using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>How a rule's title text is compared with a window's title.</summary>
public enum TitleMatch
{
    /// <summary>The title contains the text, case ignored.</summary>
    Contains,

    /// <summary>The title is the text, case ignored.</summary>
    Is,

    /// <summary>The title matches the text as a regular expression, case ignored.</summary>
    Pattern,
}

/// <summary>Which display a rule puts a window on.</summary>
public enum DisplayChoice
{
    /// <summary>The main display, where the taskbar's notification area is.</summary>
    Main,

    /// <summary>The display with the pointer when the window opens.</summary>
    UnderPointer,

    /// <summary>A display chosen from the connected ones, recognized by its model and serial number.</summary>
    Chosen,
}

/// <summary>Where on its display a rule puts a window.</summary>
public enum PlaceChoice
{
    /// <summary>Centered in the work area, at the window's size.</summary>
    Centered,

    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,

    /// <summary>The whole work area, without maximizing.</summary>
    WorkArea,

    /// <summary>Where the user last left one of the rule's windows, for each set of displays.</summary>
    Remembered,

    /// <summary>Left, top, width and height in percent of the work area.</summary>
    Custom,
}

/// <summary>How a rule opens a window.</summary>
public enum OpenChoice
{
    Normal,
    Maximized,
    Minimized,
}

/// <summary>Where a rule's opacity comes from.</summary>
public enum OpacityChoice
{
    /// <summary>A fixed opacity, from 20% to 90%.</summary>
    Fixed,

    /// <summary>The opacity Title Bar Actions' wheel last gave one of the rule's windows.</summary>
    Wheel,
}

/// <summary>
/// A rule's title condition. The match's name is kept as written, so a rule with one this version
/// doesn't know, such as from a newer version, keeps it when the settings are saved and isn't used.
/// </summary>
public sealed record RuleTitle
{
    /// <summary><c>Contains</c>, <c>Is</c> or <c>Pattern</c>; see <see cref="KnownMatch"/>.</summary>
    public string Match { get; set; } = nameof(TitleMatch.Contains);

    /// <summary>The text, or the regular expression for <c>Pattern</c>.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>How the title is compared, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public TitleMatch? KnownMatch => WindowRule.Known<TitleMatch>(Match);
}

/// <summary>
/// A rule's display, kept as Display Profiles keeps a display, so a chosen one is recognized on any
/// connection. See "Display" in docs/features/windows/window-rules.md.
/// </summary>
public sealed record RuleDisplay
{
    /// <summary><c>Main</c>, <c>UnderPointer</c> or <c>Chosen</c>; see <see cref="KnownWhich"/>.</summary>
    public string Which { get; set; } = nameof(DisplayChoice.Main);

    /// <summary>For a chosen display: its name from its EDID, such as <c>DELL U2720Q</c>, for the page.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>For a chosen display: the maker's code and the product code from its EDID.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>For a chosen display: a hash of its serial number (<see cref="DisplayRecognition.HashSerial"/>).</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>For a chosen display: its device path when it was chosen.</summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>For a chosen display: whether it's a laptop's or tablet's own screen.</summary>
    public bool BuiltIn { get; set; }

    /// <summary>Which display, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public DisplayChoice? KnownWhich => WindowRule.Known<DisplayChoice>(Which);

    /// <summary>The chosen display, to recognize among the connected ones.</summary>
    [JsonIgnore]
    public KnownDisplay AsKnown => new(Model, Serial, Connection, BuiltIn);
}

/// <summary>A rule's place, in its display's work area. See "Place and size" in docs/features/windows/window-rules.md.</summary>
public sealed record RulePlace
{
    /// <summary>The preset, <c>Remembered</c> or <c>Custom</c>; see <see cref="KnownWhich"/>.</summary>
    public string Which { get; set; } = nameof(PlaceChoice.Centered);

    /// <summary>For <c>Custom</c>: the left edge, in percent of the work area's width from its left.</summary>
    public double Left { get; set; }

    /// <summary>For <c>Custom</c>: the top edge, in percent of the work area's height from its top.</summary>
    public double Top { get; set; }

    /// <summary>For <c>Custom</c>: the width, in percent of the work area's width.</summary>
    public double Width { get; set; } = 100;

    /// <summary>For <c>Custom</c>: the height, in percent of the work area's height.</summary>
    public double Height { get; set; } = 100;

    /// <summary>The place, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public PlaceChoice? KnownWhich => WindowRule.Known<PlaceChoice>(Which);
}

/// <summary>A rule's opacity. See "Opacity" in docs/features/windows/window-rules.md.</summary>
public sealed record RuleOpacity
{
    /// <summary>The lowest fixed opacity, in percent.</summary>
    public const int Lowest = 20;

    /// <summary>The highest fixed opacity, in percent: 100% is the window's own.</summary>
    public const int Highest = 90;

    /// <summary>The steps a fixed opacity takes, in percent.</summary>
    public const int Step = 10;

    /// <summary><c>Fixed</c> or <c>Wheel</c>; see <see cref="KnownWhich"/>.</summary>
    public string Which { get; set; } = nameof(OpacityChoice.Fixed);

    /// <summary>For <c>Fixed</c>: the opacity in percent, from 20 to 90 in steps of 10.</summary>
    public int Percent { get; set; } = 80;

    /// <summary>Where the opacity comes from, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public OpacityChoice? KnownWhich => WindowRule.Known<OpacityChoice>(Which);
}

/// <summary>
/// A rule: the app, and optionally the title and window class, it matches, and its actions. An
/// action that's left out, or <see langword="false"/>, is off. See "Rules" in
/// docs/features/windows/window-rules.md.
/// </summary>
public sealed record WindowRule
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaximumNameLength = 60;

    /// <summary>
    /// A key the settings app gives a rule as it's added, which keeps what the rule remembers when
    /// it's renamed or moved; empty in a hand-written rule, which is then known by its name.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>The app the rule matches; a rule without one isn't used.</summary>
    public ListedApp? App { get; set; }

    public RuleTitle? Title { get; set; }

    /// <summary>The class name the app registered for its windows, matched exactly, case ignored.</summary>
    public string? WindowClass { get; set; }

    public RuleDisplay? Display { get; set; }

    public RulePlace? Place { get; set; }

    /// <summary>
    /// <c>Normal</c>, <c>Maximized</c> or <c>Minimized</c>, kept as written; <see langword="null"/>
    /// when the app chooses. See <see cref="KnownOpen"/>.
    /// </summary>
    public string? Open { get; set; }

    public bool OnTop { get; set; }

    public RuleOpacity? Opacity { get; set; }

    public bool HideFromTaskbar { get; set; }

    /// <summary>The app whose front-most window the rule's windows open as a tab of, with Window Tabs.</summary>
    public ListedApp? TabOf { get; set; }

    /// <summary>Whether the rule's open windows are placed again after the displays change.</summary>
    public bool ApplyOnDisplayChange { get; set; }

    /// <summary>How the window opens, or <see langword="null"/> when the app chooses or this version doesn't know it.</summary>
    [JsonIgnore]
    public OpenChoice? KnownOpen => Open is null ? null : Known<OpenChoice>(Open);

    /// <summary>Whether any action is on.</summary>
    [JsonIgnore]
    public bool HasAction =>
        Display is not null || Place is not null || Open is not null || OnTop || Opacity is not null || HideFromTaskbar || TabOf is not null;

    /// <summary>Whether the rule places windows, so Apply now and display changes move them.</summary>
    [JsonIgnore]
    public bool Places => Display is not null || Place is not null || Open is not null;

    internal static T? Known<T>(string name)
        where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase));

    // Blank app IDs and programs, and blank texts, count as none, as a hand-edited file can hold.
    internal WindowRule Checked() => this with
    {
        App = App?.Checked(),
        TabOf = TabOf?.Checked(),
        WindowClass = string.IsNullOrWhiteSpace(WindowClass) ? null : WindowClass,
    };
}

/// <summary>
/// Window Rules settings, stored in <c>features.windowRules</c>. See docs/features/windows/window-rules.md.
/// </summary>
public sealed record WindowRulesSettings : IFeatureSettings<WindowRulesSettings>
{
    /// <summary>The most rules that are used; later ones are left out.</summary>
    public const int MaximumRules = 50;

    public bool Enabled { get; set; }

    public IReadOnlyList<WindowRule> Rules { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in the list,
    /// such as after a hand edit, is left out.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static WindowRulesSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, WindowRulesJsonContext.Default.WindowRulesSettings, () => new WindowRulesSettings());
        return settings with { Rules = [.. settings.Rules.Where(rule => rule is not null).Select(rule => rule.Checked())] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, WindowRulesJsonContext.Default.WindowRulesSettings);

    public WindowRulesSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the rules one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(WindowRulesSettings? other) =>
        other is not null && Enabled == other.Enabled && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => HashCode.Combine(Enabled, Rules.Count);
}
