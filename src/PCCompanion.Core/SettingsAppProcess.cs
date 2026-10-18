namespace PCCompanion.Core;

/// <summary>
/// What the background host needs to know about the settings app.
/// </summary>
public static class SettingsAppProcess
{
    /// <summary>
    /// The signal that asks the settings app to close, one per user session. The host sends it
    /// when the user chooses Exit in its tray menu.
    /// </summary>
    public const string ExitSignalName = @"Local\PCCompanion.App.Exit";
}
