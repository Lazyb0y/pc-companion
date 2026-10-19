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
    /// The argument setup starts the host with before it removes or replaces PC Companion's files:
    /// the host closes the running settings app and host, as Exit in the tray menu does, waits for
    /// them to end, and exits.
    /// </summary>
    public const string ExitArgument = "--exit";

    /// <summary>
    /// The argument setup starts the host with as it uninstalls PC Companion: the host removes the
    /// user's data folder, as the user who uninstalls it, and exits.
    /// </summary>
    public const string RemoveDataArgument = "--remove-data";

    /// <summary>
    /// With <see cref="RemoveDataArgument"/>, keeps the settings file and removes the rest, as
    /// Keep my settings in setup does.
    /// </summary>
    public const string KeepSettingsArgument = "--keep-settings";

    /// <summary>
    /// The argument a host running as administrator starts a copy of itself with, without admin
    /// rights, to run the update check: the copy asks GitHub when a check is due and the setting is
    /// on, saves the result in the user's data folder, and exits.
    /// </summary>
    public const string CheckForUpdatesArgument = "--check-for-updates";

    /// <summary>
    /// The argument a host running as administrator starts a copy of itself with, without admin
    /// rights, to open the latest release's page in the browser.
    /// </summary>
    public const string OpenReleasePageArgument = "--open-release-page";

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

    /// <summary>
    /// Whether the running host lacks the admin rights that Always run as administrator gives it:
    /// its hand-off to the scheduled task failed, so features don't work in front of apps running
    /// as administrator. The settings app checks only while it runs without admin rights because of
    /// UAC; with UAC off, an administrator's programs all have admin rights. When it can't tell,
    /// for example because no host runs, it doesn't warn.
    /// </summary>
    /// <param name="general">The user's general settings.</param>
    /// <param name="uacLimited">Whether the settings app runs without admin rights because of UAC.</param>
    /// <param name="hostElevated">
    /// Whether the running host runs as administrator, read from its process, or
    /// <see langword="null"/> when no host runs or that can't be read.
    /// </param>
    public static bool RunsWithoutAdminRights(GeneralSettings general, bool uacLimited, bool? hostElevated)
    {
        ArgumentNullException.ThrowIfNull(general);
        return general.AlwaysRunAsAdministrator && uacLimited && hostElevated == false;
    }
}
