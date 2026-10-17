using System;
using System.IO;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Elevated;

/// <summary>
/// The elevated helper: runs one command that needs admin rights and exits. Its arguments come
/// from a process without admin rights, so each one is checked, and the program the tasks start
/// is always the host next to the helper, never a path from the arguments.
/// </summary>
internal static class Program
{
    private const int Succeeded = 0;
    private const int Failed = 1;
    private const int InvalidArguments = 2;
    private const int NotElevated = 3;
    private const int NotProtected = 4;

    private const string TaskDescription = "Starts PC Companion's background host as administrator. Created by the Always run as administrator setting.";

    [STAThread]
    private static int Main(string[] args)
    {
        if (!Elevation.IsElevated)
        {
            return NotElevated;
        }

        // A folder others can change would let them replace the host the tasks start as administrator.
        var folder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var host = Path.Combine(folder, HostProcess.FileName);
        if (!ProtectedFiles.IsProtectedInstall(folder, HostProcess.FileName, ElevatedStartup.HelperFileName))
        {
            return NotProtected;
        }

        try
        {
            switch (args)
            {
                case [ElevatedStartup.CreateTasksCommand, var sid] when Elevation.IsAccountSid(sid):
                    ScheduledTasks.Register(ElevatedStartup.TaskFolder + ElevatedStartup.SignInTaskName(sid),
                        ScheduledTasks.CreateDefinition(host, HostProcess.StartupArgument, sid, atSignIn: true, TaskDescription));
                    ScheduledTasks.Register(ElevatedStartup.TaskFolder + ElevatedStartup.StartTaskName(sid),
                        ScheduledTasks.CreateDefinition(host, null, sid, atSignIn: false, TaskDescription));
                    return Succeeded;

                case [ElevatedStartup.DeleteTasksCommand, var sid] when Elevation.IsAccountSid(sid):
                    ScheduledTasks.Delete(ElevatedStartup.TaskFolder + ElevatedStartup.SignInTaskName(sid));
                    ScheduledTasks.Delete(ElevatedStartup.TaskFolder + ElevatedStartup.StartTaskName(sid));
                    return Succeeded;

                case [ElevatedStartup.DeleteAllTasksCommand]:
                    foreach (var task in ScheduledTasks.List(ElevatedStartup.TaskFolder))
                    {
                        ScheduledTasks.Delete(task);
                    }

                    return Succeeded;

                default:
                    return InvalidArguments;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failed;
        }
    }
}
