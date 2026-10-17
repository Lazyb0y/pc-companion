namespace PCCompanion.Core;

/// <summary>
/// What the settings app needs to know about the background host.
/// </summary>
public static class HostProcess
{
    /// <summary>The host's executable, installed next to the settings app.</summary>
    public const string FileName = "PCCompanion.Host.exe";

    /// <summary>
    /// The class name of the host's hidden window, which other processes post messages to.
    /// </summary>
    public const string WindowClassName = "PCCompanion.Host";

    /// <summary>The registered window message that asks the host to exit.</summary>
    public const string ExitMessageName = "PCCompanion.Host.Exit";

    /// <summary>The lock the running host holds, one per user session.</summary>
    public const string InstanceLockName = @"Local\PCCompanion.Host";

    /// <summary>
    /// The argument of the startup entry the installer adds. The host started with it exits
    /// unless the user turned on <see cref="Settings.GeneralSettings.StartWithWindows"/>.
    /// </summary>
    public const string StartupArgument = "--startup";
}
