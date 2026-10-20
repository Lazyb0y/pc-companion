using System;

namespace PCCompanion.Core.Features;

/// <summary>
/// Commands the host carries out for features, such as from a feature's own menu. They may be
/// called from any thread. The host runs Settings and Exit on its UI thread, and opens web pages on
/// the calling thread.
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

    /// <summary>
    /// Opens an <c>http</c> or <c>https</c> address in the default browser, never with admin
    /// rights: a host running as administrator has a copy of itself without them open it. It may
    /// wait for Windows to start the browser, so don't call it on the host's UI thread. Returns
    /// false when the address isn't a web address or Windows refused.
    /// </summary>
    bool OpenWebPage(Uri address);
}
