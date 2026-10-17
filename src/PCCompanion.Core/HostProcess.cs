using System;
using PCCompanion.Core.Settings;

namespace PCCompanion.Core;

/// <summary>
/// What the settings app needs to know about the background host, and how the host starts.
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

    /// <summary>
    /// What a host does as it starts, before taking the instance lock. With Always run as
    /// administrator on, a host without admin rights leaves the work to the one the scheduled
    /// tasks start: at sign-in, the sign-in task starts it; otherwise the host runs the
    /// on-request task. See docs/features/general/run-as-administrator.md.
    /// </summary>
    /// <param name="general">The user's general settings.</param>
    /// <param name="startedAtSignIn">Whether the host was started with <see cref="StartupArgument"/>.</param>
    /// <param name="elevated">Whether the host runs as administrator.</param>
    /// <param name="hostRunning">Whether another host already runs in the user's session.</param>
    public static HostStart DecideStart(GeneralSettings general, bool startedAtSignIn, bool elevated, bool hostRunning)
    {
        ArgumentNullException.ThrowIfNull(general);
        if (startedAtSignIn && !general.StartWithWindows)
        {
            return HostStart.Exit;
        }

        if (general.AlwaysRunAsAdministrator && !elevated && !hostRunning)
        {
            return startedAtSignIn ? HostStart.Exit : HostStart.RunTask;
        }

        return HostStart.Run;
    }
}
