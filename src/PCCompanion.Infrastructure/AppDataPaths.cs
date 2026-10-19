using System;
using System.IO;

namespace PCCompanion.Infrastructure;

/// <summary>
/// Locations of the files PC Companion keeps for the current user.
/// </summary>
public sealed class AppDataPaths
{
    private const string FolderName = "PCCompanion";

    /// <param name="root">The data folder. Must be an absolute path.</param>
    public AppDataPaths(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("The data folder must be an absolute path.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogsFolder => Path.Combine(Root, "Logs");

    /// <summary>The result of the update checks, which only the host writes.</summary>
    public string UpdateCheckFile => Path.Combine(Root, "update-check.json");

    /// <summary>
    /// The folder of the host's logs while it runs as administrator,
    /// <c>%ProgramData%\PCCompanion\Logs\</c> and the user's security identifier, which only
    /// administrators can change.
    /// </summary>
    public static string ProtectedLogsFolder(string userSid)
    {
        ArgumentException.ThrowIfNullOrEmpty(userSid);

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(programData, FolderName, "Logs", userSid);
    }

    /// <summary>
    /// The paths under <c>%LOCALAPPDATA%\PCCompanion</c>.
    /// </summary>
    public static AppDataPaths ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrEmpty(localAppData))
        {
            throw new InvalidOperationException("The local application data folder is not available.");
        }

        return new AppDataPaths(Path.Combine(localAppData, FolderName));
    }
}
