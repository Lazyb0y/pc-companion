using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>What a rule does with the copied text.</summary>
public enum ClipboardRuleAction
{
    /// <summary>Opens a web address made from the rule's address and the text.</summary>
    OpenAddress,

    /// <summary>Starts a program with the text in its arguments.</summary>
    RunProgram,
}

/// <summary>
/// A rule: when the copied text matches its pattern, the menu offers its action under its name.
/// </summary>
public sealed record ClipboardRule
{
    /// <summary>The text that stands for the copied text in an address or arguments.</summary>
    public const string TextPlaceholder = "{text}";

    /// <summary>The item's text in the menu.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A .NET regular expression that the whole copied text must match.</summary>
    public string Pattern { get; set; } = string.Empty;

    public bool MatchCase { get; set; }

    /// <summary>
    /// The action's name: <c>OpenAddress</c> or <c>RunProgram</c>. It's kept as written, so a rule
    /// with an action this version doesn't know, such as one from a newer version, keeps it when
    /// the settings are saved, and is left out of the menu; see <see cref="KnownAction"/>.
    /// </summary>
    public string Action { get; set; } = nameof(ClipboardRuleAction.OpenAddress);

    /// <summary>For Open a web address: the address, with <see cref="TextPlaceholder"/> for the text.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>For Run a program: the full path of an <c>.exe</c> file.</summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>For Run a program: the arguments, on one line, with <see cref="TextPlaceholder"/> for the text.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>The action, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public ClipboardRuleAction? KnownAction =>
        Enum.GetValues<ClipboardRuleAction>().Cast<ClipboardRuleAction?>()
            .FirstOrDefault(action => string.Equals(action.ToString(), Action, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Clipboard Actions settings, stored in <c>features.clipboardActions</c>. See docs/features/clipboard/clipboard-actions.md.
/// </summary>
public sealed record ClipboardActionsSettings : IFeatureSettings<ClipboardActionsSettings>
{
    /// <summary>The most rules the menu uses; later ones are left out.</summary>
    public const int MaximumRules = 50;

    public bool Enabled { get; set; }

    /// <summary>Whether a copied web link gets Open in browser.</summary>
    public bool OpenLinks { get; set; } = true;

    public IReadOnlyList<ClipboardRule> Rules { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static ClipboardActionsSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, ClipboardActionsJsonContext.Default.ClipboardActionsSettings, () => new ClipboardActionsSettings());

    public JsonElement Write() => FeatureSettings.Write(this, ClipboardActionsJsonContext.Default.ClipboardActionsSettings);

    public ClipboardActionsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the rules one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(ClipboardActionsSettings? other) =>
        other is not null && Enabled == other.Enabled && OpenLinks == other.OpenLinks && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => HashCode.Combine(Enabled, OpenLinks, Rules.Count);
}
