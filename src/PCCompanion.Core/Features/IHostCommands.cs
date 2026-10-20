namespace PCCompanion.Core.Features;

/// <summary>
/// Commands the host carries out for features, such as from a feature's own menu. They may be
/// called from any thread; the host runs them on its UI thread.
/// </summary>
public interface IHostCommands
{
    /// <summary>
    /// Opens the settings app, or brings it to the front, as selecting the PC Companion icon does.
    /// </summary>
    void OpenSettings();

    /// <summary>
    /// Stops PC Companion, the settings app included, as Exit in the PC Companion icon's menu does.
    /// </summary>
    void ExitApp();
}
