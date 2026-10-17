namespace PCCompanion.Core.Features;

/// <summary>
/// What the host and the settings app know about a feature.
/// </summary>
/// <param name="Id">Stable ID; also the name of the feature's section in <c>settings.json</c>.</param>
/// <param name="ModuleId">The module the feature belongs to, such as <c>input</c>.</param>
/// <param name="NameKey">The <c>Strings</c> key of the feature's name.</param>
/// <param name="DescriptionKey">The <c>Strings</c> key of a one-sentence description.</param>
/// <param name="Glyph">The Segoe Fluent Icons glyph of the feature.</param>
public sealed record FeatureInfo(string Id, string ModuleId, string NameKey, string DescriptionKey, string Glyph);
