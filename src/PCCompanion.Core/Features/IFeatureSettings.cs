using System.Text.Json;

namespace PCCompanion.Core.Features;

/// <summary>
/// A feature's settings, stored in its section of the settings file, so code shared by every
/// feature, such as the settings app's pages, can read, turn on or off and save them.
/// </summary>
public interface IFeatureSettings<TSelf>
    where TSelf : IFeatureSettings<TSelf>
{
    /// <summary>Whether the feature is on.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    static abstract TSelf Read(JsonElement? section);

    /// <summary>The settings as the feature's section.</summary>
    JsonElement Write();

    /// <summary>Returns a copy with the feature turned on or off and its other settings unchanged.</summary>
    TSelf WithEnabled(bool enabled);
}
