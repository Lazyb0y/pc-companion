using System.Collections.Generic;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Opens the apps features ask for by their app ID, such as a Drop-down Apps app's, through
/// Start's Apps folder, never with admin rights, as <see cref="ProgramCommand"/> starts programs: a
/// host running as administrator through UAC hands the app ID to an <see cref="UnelevatedCopy"/>
/// started with <see cref="HostProcess.OpenAppArgument"/>, which opens it. See
/// docs/features/general/run-as-administrator.md.
/// </summary>
internal static class AppCommand
{
    private const string PipeName = "PCCompanion.OpenApp";
    private const int FileNotFound = 2; // ERROR_FILE_NOT_FOUND
    private const int AccessDenied = 5; // ERROR_ACCESS_DENIED
    private const int InvalidData = 13; // ERROR_INVALID_DATA

    // An app ID in UTF-8 has at most 4 bytes for each character.
    private const int MaximumHandOffLength = HostProcess.MaximumAppIdLength * 4;

    /// <summary>
    /// Opens the app with <paramref name="appId"/> from the running host, on the calling thread: a
    /// host running as administrator waits up to 30 seconds for its copy. Returns 0, or Windows'
    /// error code when it wasn't opened: <c>ERROR_FILE_NOT_FOUND</c> when the app ID isn't one or
    /// isn't in the Apps folder.
    /// </summary>
    public static int Start(string appId)
    {
        if (!HostProcess.IsAppId(appId))
        {
            return FileNotFound;
        }

        return Elevation.IsUacElevated
            ? UnelevatedCopy.Run(PipeName, HostProcess.AppIdHandOff(appId), HostProcess.OpenAppArguments)
            : Open(appId);
    }

    /// <summary>Runs the copy started with <see cref="HostProcess.OpenAppArgument"/>, and returns its exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        // Started with admin rights, for example by hand, it opens nothing.
        if (Elevation.IsUacElevated)
        {
            return AccessDenied;
        }

        if (UnelevatedCopy.Read(HostProcess.ReadOpenAppPipe(arguments), MaximumHandOffLength) is not { } handOff
            || HostProcess.ReadAppId(handOff) is not { } appId)
        {
            return InvalidData;
        }

        return Open(appId);
    }

    // Windows opens File Explorer, without an error, for an app ID that isn't in the Apps folder,
    // so it's looked up first.
    private static int Open(string appId) => ShellActions.IsInAppsFolder(appId) ? ShellActions.OpenApp(appId) : FileNotFound;
}
