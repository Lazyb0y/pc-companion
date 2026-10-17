using System;
using System.Text.Json;

namespace PCCompanion.Core.Features;

/// <summary>
/// A feature that runs in the host. See docs/architecture/features.md.
/// </summary>
public interface IFeature : IDisposable
{
    FeatureInfo Info { get; }

    FeatureSupport Support { get; }

    /// <summary>
    /// Starts, stops or reconfigures the feature. Called on the host's UI thread at startup and
    /// after every settings change, with the feature's section of the settings, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    void Apply(JsonElement? settings);
}
