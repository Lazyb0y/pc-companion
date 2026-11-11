namespace PCCompanion.Core.Features;

/// <summary>
/// Implemented by a feature that offers actions that shortcuts and mouse buttons can run, such as
/// Hide Apps' Hide others. The host asks only a running feature.
/// </summary>
public interface IFeatureActions
{
    /// <summary>
    /// Whether <paramref name="action"/> is one of the feature's and can run now, as with a
    /// profile of that name. Called on the host's UI thread, so it answers at once from what the
    /// feature already knows.
    /// </summary>
    bool CanRun(FeatureAction action);

    /// <summary>
    /// Runs <paramref name="action"/>, as the feature's own shortcut or menu item would. Called on
    /// the host's UI thread; work that can wait goes to the feature's thread or the thread pool.
    /// </summary>
    void Run(FeatureAction action);
}
