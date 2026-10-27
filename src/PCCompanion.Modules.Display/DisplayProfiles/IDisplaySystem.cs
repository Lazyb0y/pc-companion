using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// What switching needs from Windows: reading the displays, and checking, applying and saving
/// setups. Each call waits for Windows, so it runs on the thread pool, one at a time.
/// </summary>
public interface IDisplaySystem
{
    /// <summary>Reads the connected displays, the setup in use and the displays' serial numbers.</summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    CurrentSetup Read();

    /// <inheritdoc cref="DisplayConfiguration.Check"/>
    void Check(IReadOnlyList<DisplayPath> setup);

    /// <inheritdoc cref="DisplayConfiguration.Apply"/>
    void Apply(IReadOnlyList<DisplayPath> setup);

    /// <inheritdoc cref="DisplayConfiguration.SaveCurrent"/>
    void SaveCurrent();

    /// <inheritdoc cref="DisplayConfiguration.ApplySaved"/>
    void ApplySaved();

    /// <summary>Turns HDR on or off on a display that's on and supports it.</summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    void SetHdr(DisplayTarget target, bool enabled);

    /// <inheritdoc cref="DisplayConfiguration.ListModes"/>
    IReadOnlyList<DisplayMode> ListModes(DisplaySource source);
}
