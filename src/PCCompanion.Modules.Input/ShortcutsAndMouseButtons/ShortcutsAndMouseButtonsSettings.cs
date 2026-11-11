using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// Shortcuts & Mouse Buttons settings, stored in <c>features.shortcutsAndMouseButtons</c>. See
/// docs/features/input/shortcuts-and-mouse-buttons.md.
/// </summary>
public sealed record ShortcutsAndMouseButtonsSettings : IFeatureSettings<ShortcutsAndMouseButtonsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>The shortcuts and mouse buttons, each with its action, in the order the page shows them.</summary>
    public IReadOnlyList<Trigger> Triggers { get; set; } = [];

    /// <summary>Whether mouse buttons let their clicks through while the window in front is full screen.</summary>
    public bool PauseMouseButtonsInFullScreen { get; set; } = true;

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in the list
    /// of triggers is left out, and each trigger is cleaned up as <see cref="Trigger.Checked"/>
    /// says; a trigger that can't be used is kept, for the page to show.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static ShortcutsAndMouseButtonsSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, ShortcutsAndMouseButtonsJsonContext.Default.ShortcutsAndMouseButtonsSettings, () => new ShortcutsAndMouseButtonsSettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items.
        return settings with { Triggers = [.. settings.Triggers.Where(trigger => trigger is not null).Select(trigger => trigger.Checked())] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, ShortcutsAndMouseButtonsJsonContext.Default.ShortcutsAndMouseButtonsSettings);

    public ShortcutsAndMouseButtonsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(ShortcutsAndMouseButtonsSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && PauseMouseButtonsInFullScreen == other.PauseMouseButtonsInFullScreen
        && Triggers.SequenceEqual(other.Triggers);

    public override int GetHashCode() => HashCode.Combine(Enabled, PauseMouseButtonsInFullScreen, Triggers.Count);
}

/// <summary>The JSON format of <c>features.shortcutsAndMouseButtons</c>, matching the rest of <c>settings.json</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShortcutsAndMouseButtonsSettings))]
internal sealed partial class ShortcutsAndMouseButtonsJsonContext : JsonSerializerContext;
