using System;
using System.Collections.Generic;

namespace PCCompanion.Core.Features;

/// <summary>
/// Commands the host carries out for features, such as from a feature's own menu. They may be
/// called from any thread. The host runs Settings and Exit on its UI thread, and opens web pages and
/// starts programs on the calling thread.
/// </summary>
public interface IHostCommands
{
    /// <summary>
    /// Opens the settings app, or brings it to the front, as selecting the PC Companion icon does.
    /// </summary>
    void OpenSettings();

    /// <summary>
    /// Opens the settings app on the page of the feature with the ID <paramref name="featureId"/>,
    /// or shows that page in the settings app that's open.
    /// </summary>
    void OpenFeatureSettings(string featureId);

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

    /// <summary>
    /// Starts a program with the user's rights, never with admin rights, in its own folder, with its
    /// arguments passed one by one and no shell: a host running as administrator has a copy of itself
    /// without them start it. It waits for Windows to start the program, up to 30 seconds, so don't
    /// call it on the host's UI thread. Returns false, with Windows' error code, when the program
    /// wasn't started.
    /// </summary>
    /// <param name="program">The full path of a program file that was checked to exist.</param>
    bool StartProgram(string program, IReadOnlyList<string> arguments, out int errorCode);
}
