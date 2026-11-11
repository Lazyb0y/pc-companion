using System;

namespace PCCompanion.Core.Features;

/// <summary>
/// The actions of the running features, which the host gives the feature that runs them from
/// shortcuts and mouse buttons, so that modules don't reference each other.
/// </summary>
public interface IHostActions
{
    /// <summary>
    /// Raised on the host's UI thread after the host applied the settings to every feature, as at
    /// startup and after each change, so which actions can run may have changed.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Whether a running feature offers <paramref name="action"/> and can run it now. Call it on
    /// the host's UI thread.
    /// </summary>
    bool CanRun(FeatureAction action);

    /// <summary>
    /// Runs <paramref name="action"/> through the feature that offers it, on the host's UI thread.
    /// Call it from any thread; it doesn't wait.
    /// </summary>
    void Run(FeatureAction action);
}
