namespace PCCompanion.Core;

/// <summary>
/// The scheduled tasks that start the host as administrator, and the elevated helper that
/// creates and deletes them. See docs/features/general/run-as-administrator.md.
/// </summary>
public static class ElevatedStartup
{
    /// <summary>The elevated helper's executable, installed next to the host.</summary>
    public const string HelperFileName = "PCCompanion.Elevated.exe";

    /// <summary>The Task Scheduler folder of the tasks.</summary>
    public const string TaskFolder = @"\PCCompanion\";

    /// <summary>Helper command: create the tasks of the user with the security identifier that follows.</summary>
    public const string CreateTasksCommand = "create-startup-tasks";

    /// <summary>Helper command: delete the tasks of the user with the security identifier that follows.</summary>
    public const string DeleteTasksCommand = "delete-startup-tasks";

    /// <summary>Helper command, used when uninstalling: delete the tasks of every user.</summary>
    public const string DeleteAllTasksCommand = "delete-all-startup-tasks";

    /// <summary>The task that starts the host when the user signs in, if Start with Windows is on.</summary>
    public static string SignInTaskName(string userSid) => $"Host at sign-in {userSid}";

    /// <summary>The task the host runs to start itself as administrator, on request.</summary>
    public static string StartTaskName(string userSid) => $"Host {userSid}";
}
