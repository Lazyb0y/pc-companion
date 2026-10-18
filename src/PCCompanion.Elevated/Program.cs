using System;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Infrastructure;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Elevated;

/// <summary>
/// The elevated helper: runs one command that needs admin rights and exits. Its arguments come
/// from a process without admin rights, so each one is checked, and the program the tasks start
/// is always the host next to the helper, never a path from the arguments.
/// </summary>
internal static partial class Program
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

        using var logging = CreateLogging(args);
        return Run(args, logging.CreateLogger(typeof(Program)));
    }

    // The settings app starts the helper through UAC, so it can't read the helper's output: a
    // command for one user logs to that user's protected logs folder, which only administrators
    // can change and the user can read. Deleting every user's tasks, when uninstalling, logs nothing.
    private static ILoggerFactory CreateLogging(string[] args)
    {
        var sid = args is [ElevatedStartup.CreateTasksCommand or ElevatedStartup.DeleteTasksCommand, var user] && Elevation.IsAccountSid(user) ? user : null;
        var folder = sid is null ? null : AppDataPaths.ProtectedLogsFolder(sid);
        return LoggerFactory.Create(builder =>
        {
            if (folder is not null && ProtectedFiles.CreateUserFolder(folder, sid!))
            {
                builder.AddFileLog(folder, "elevated");
            }
        });
    }

    private static int Run(string[] args, ILogger logger)
    {
        // A folder others can change would let them replace the host the tasks start as administrator.
        var folder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var host = Path.Combine(folder, HostProcess.FileName);
        if (!ProtectedFiles.IsProtectedInstall(folder, HostProcess.FileName, ElevatedStartup.HelperFileName))
        {
            LogNotProtected(logger, folder);
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
                    LogCreatedTasks(logger);
                    return Succeeded;

                case [ElevatedStartup.DeleteTasksCommand, var sid] when Elevation.IsAccountSid(sid):
                    ScheduledTasks.Delete(ElevatedStartup.TaskFolder + ElevatedStartup.SignInTaskName(sid));
                    ScheduledTasks.Delete(ElevatedStartup.TaskFolder + ElevatedStartup.StartTaskName(sid));
                    LogDeletedTasks(logger);
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
            LogFailed(logger, exception, args.Length > 0 ? args[0] : string.Empty);
            return Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the Always run as administrator tasks.")]
    private static partial void LogCreatedTasks(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted the Always run as administrator tasks.")]
    private static partial void LogDeletedTasks(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command} failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "Others can change the install folder {Folder}, so the tasks would start a host they could replace.")]
    private static partial void LogNotProtected(ILogger logger, string folder);
}
