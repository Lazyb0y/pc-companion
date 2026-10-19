namespace PCCompanion.Core;

/// <summary>
/// What the background host needs to know about the settings app.
/// </summary>
public static class SettingsAppProcess
{
    /// <summary>The settings app's executable, installed next to the host.</summary>
    public const string FileName = "PCCompanion.exe";

    /// <summary>
    /// The signal that asks the settings app to close, one per user session. The host sends it
    /// when the user chooses Exit in its tray menu, and before setup removes or replaces the app's files.
    /// </summary>
    public const string ExitSignalName = @"Local\PCCompanion.App.Exit";
}
