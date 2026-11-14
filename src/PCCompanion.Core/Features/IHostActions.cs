using System;

namespace PCCompanion.Core.Features;

/// <summary>
/// The actions of the running features, which the host gives the features that run them from
/// shortcuts, mouse buttons and launchers, so that modules don't reference each other.
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
    /// Whether a running feature offers <paramref name="action"/> and can run it now, and it
    /// doesn't act on the window in front, so a launcher or the command can run it. Call it on the
    /// host's UI thread.
    /// </summary>
    bool CanRunFromLauncher(FeatureAction action);

    /// <summary>
    /// Whether the feature with the ID <paramref name="featureId"/> runs: it's on, supported on
    /// this PC and hasn't failed. Tells an action whose feature isn't running from one its feature
    /// refuses now. Call it on the host's UI thread.
    /// </summary>
    bool IsRunning(string featureId);

    /// <summary>
    /// The feature with the ID <paramref name="featureId"/>, running or not, or
    /// <see langword="null"/> when the host has none, so a refused action can name its feature.
    /// </summary>
    FeatureInfo? FindFeature(string featureId);

    /// <summary>
    /// Runs <paramref name="action"/> through the feature that offers it, on the host's UI thread.
    /// Call it from any thread; it doesn't wait.
    /// </summary>
    void Run(FeatureAction action);
}
