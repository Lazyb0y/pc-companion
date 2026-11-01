using System;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Starts a listed app that has no window, as "Starting the app" in
/// docs/features/windows/drop-down-apps.md says: a program file in its own folder without
/// arguments, or an app ID through Start's Apps folder, never with admin rights.
/// </summary>
public static class AppStarter
{
    private const int FileNotFound = 2; // ERROR_FILE_NOT_FOUND
    private const int PathNotFound = 3; // ERROR_PATH_NOT_FOUND
    private const int ElevationRequired = 740; // ERROR_ELEVATION_REQUIRED

    /// <summary>
    /// Starts <paramref name="app"/> through the host. It waits for Windows, up to 30 seconds, and
    /// checks the program file on disk, so don't call it on a thread that must not wait. Returns
    /// <see cref="DropDownOutcome.Started"/>, or why the app didn't start.
    /// </summary>
    public static DropDownOutcome Start(DropDownApp app, IHostCommands commands, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (app.AppId is { } appId)
        {
            return commands.StartApp(appId, out var appError) ? DropDownOutcome.Started
                : appError == FileNotFound ? DropDownOutcome.AppIdNotFound
                : DropDownOutcome.StartFailed;
        }

        if (ProgramFile.Check(app.Program, fileExists) != ProgramProblem.None)
        {
            return DropDownOutcome.ProgramMissing;
        }

        return commands.StartProgram(app.Program!, [], out var errorCode) ? DropDownOutcome.Started : errorCode switch
        {
            ElevationRequired => DropDownOutcome.NeedsAdminRights,
            FileNotFound or PathNotFound => DropDownOutcome.ProgramMissing,
            _ => DropDownOutcome.StartFailed,
        };
    }
}
