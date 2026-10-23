using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
    /// The full path of the host's executable, next to the program that asks: the settings app or
    /// the host itself.
    /// </summary>
    public static string ExecutablePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

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
    /// The argument, followed by an <c>http</c> or <c>https</c> address, that a host running as
    /// administrator starts a copy of itself with, without admin rights, to open the address in
    /// the browser, for a feature or the release page; see <see cref="WebPageArguments"/>.
    /// </summary>
    public const string OpenWebPageArgument = "--open-web-page";

    /// <summary>
    /// Whether <paramref name="address"/> is an absolute <c>http</c> or <c>https</c> address. The
    /// Windows shell starts whatever program is registered for other kinds of addresses.
    /// </summary>
    public static bool IsWebAddress([NotNullWhen(true)] Uri? address) =>
        address is { IsAbsoluteUri: true } && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// The arguments that make the host open <paramref name="address"/>: <see cref="OpenWebPageArgument"/>
    /// and the address, which needs no quotes, since its escaped form has no spaces or quotes.
    /// </summary>
    /// <exception cref="ArgumentException">The address isn't an <c>http</c> or <c>https</c> address.</exception>
    public static string WebPageArguments(Uri address)
    {
        if (!IsWebAddress(address) || address.AbsoluteUri.AsSpan().IndexOfAny(" \t\"") >= 0)
        {
            throw new ArgumentException("Only http and https addresses are opened.", nameof(address));
        }

        return OpenWebPageArgument + " " + address.AbsoluteUri;
    }

    /// <summary>
    /// The address after <see cref="OpenWebPageArgument"/> in the host's arguments, or
    /// <see langword="null"/> when there is none or it isn't an <c>http</c> or <c>https</c> address.
    /// </summary>
    public static Uri? ReadWebPageAddress(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], OpenWebPageArgument, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate(arguments[index + 1], UriKind.Absolute, out var address) && IsWebAddress(address) ? address : null;
            }
        }

        return null;
    }

    /// <summary>
    /// The argument, followed by the name of a pipe, that a host running as administrator starts a
    /// copy of itself with, without admin rights, to start a program for a feature: the copy reads
    /// the program and its arguments from the pipe, as a <see cref="ProgramToStart"/>, starts the
    /// program, and exits with 0 or Windows' error code. See <see cref="RunProgramArguments"/>.
    /// </summary>
    public const string RunProgramArgument = "--run-program";

    /// <summary>
    /// The arguments that make the host start the program it reads from the pipe named
    /// <paramref name="pipeName"/>: <see cref="RunProgramArgument"/> and the name.
    /// </summary>
    /// <exception cref="ArgumentException">The name has other characters than letters, digits and dots.</exception>
    public static string RunProgramArguments(string pipeName) =>
        IsPipeName(pipeName)
            ? RunProgramArgument + " " + pipeName
            : throw new ArgumentException("A pipe name has only letters, digits and dots.", nameof(pipeName));

    /// <summary>
    /// The pipe name after <see cref="RunProgramArgument"/> in the host's arguments, or
    /// <see langword="null"/> when there is none or it isn't one.
    /// </summary>
    public static string? ReadRunProgramPipe(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], RunProgramArgument, StringComparison.OrdinalIgnoreCase))
            {
                return IsPipeName(arguments[index + 1]) ? arguments[index + 1] : null;
            }
        }

        return null;
    }

    // A name that needs no quotes on a command line and names nothing but a pipe.
    private static bool IsPipeName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 200 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '.');

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

    /// <summary>
    /// Whether the host runs as administrator, for the feature pages' notes about apps running as
    /// administrator: as read from its process, or, when no host runs or that can't be read, as
    /// Always run as administrator would have it. The setting alone is wrong with UAC off, where the
    /// host runs as administrator without it, and after the host's hand-off to the scheduled task failed.
    /// </summary>
    /// <param name="general">The user's general settings.</param>
    /// <param name="hostElevated">
    /// Whether the running host runs as administrator, read from its process, or
    /// <see langword="null"/> when no host runs or that can't be read.
    /// </param>
    public static bool RunsAsAdministrator(GeneralSettings general, bool? hostElevated)
    {
        ArgumentNullException.ThrowIfNull(general);
        return hostElevated ?? general.AlwaysRunAsAdministrator;
    }
}
