using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Windows Task Scheduler tasks, managed with <c>schtasks.exe</c>.
/// </summary>
public static class ScheduledTasks
{
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly string Schtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    /// <summary>
    /// Returns the definition of a task that starts <paramref name="program"/> as the user with
    /// the security identifier <paramref name="userSid"/>, with the highest rights the user can
    /// get, either when the user signs in or only when run on request. Only administrators can
    /// change the task; the user can read and run it.
    /// </summary>
    public static string CreateDefinition(string program, string? arguments, string userSid, bool atSignIn, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentException.ThrowIfNullOrEmpty(userSid);

        var exec = new XElement(Task + "Exec", new XElement(Task + "Command", $"\"{program}\""));
        if (!string.IsNullOrEmpty(arguments))
        {
            exec.Add(new XElement(Task + "Arguments", arguments));
        }

        exec.Add(new XElement(Task + "WorkingDirectory", Path.GetDirectoryName(program)));

        var document = new XDocument(
            new XElement(Task + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Task + "RegistrationInfo",
                    new XElement(Task + "Description", description),
                    // Administrators and SYSTEM: full control; the user: read and run.
                    new XElement(Task + "SecurityDescriptor", $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;{userSid})")),
                new XElement(Task + "Triggers",
                    atSignIn ? new XElement(Task + "LogonTrigger", new XElement(Task + "Enabled", "true"), new XElement(Task + "UserId", userSid)) : null),
                new XElement(Task + "Principals",
                    new XElement(Task + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Task + "UserId", userSid),
                        new XElement(Task + "LogonType", "InteractiveToken"),
                        new XElement(Task + "RunLevel", "HighestAvailable"))),
                new XElement(Task + "Settings",
                    new XElement(Task + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Task + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Task + "StopIfGoingOnBatteries", "false"),
                    new XElement(Task + "AllowHardTerminate", "false"),
                    new XElement(Task + "StartWhenAvailable", "false"),
                    new XElement(Task + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Task + "IdleSettings", new XElement(Task + "StopOnIdleEnd", "false"), new XElement(Task + "RestartOnIdle", "false")),
                    new XElement(Task + "AllowStartOnDemand", "true"),
                    new XElement(Task + "Enabled", "true"),
                    new XElement(Task + "Hidden", "false"),
                    new XElement(Task + "RunOnlyIfIdle", "false"),
                    new XElement(Task + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Task + "Priority", "7")),
                new XElement(Task + "Actions", new XAttribute("Context", "Author"), exec)));

        return document.ToString();
    }

    /// <summary>Creates or replaces the task at <paramref name="taskPath"/>, such as <c>\Folder\Name</c>.</summary>
    /// <exception cref="InvalidOperationException">Task Scheduler refused it.</exception>
    public static void Register(string taskPath, string definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskPath);
        ArgumentNullException.ThrowIfNull(definition);

        // schtasks can't read a file that another handle may write to. Others can change the temp
        // folder, so the file is opened again read-only, which keeps anyone from writing, deleting
        // or renaming it until schtasks has read it, and checked to be the one written.
        var file = Path.Combine(Path.GetTempPath(), $"PCCompanion-{Guid.NewGuid():N}.xml");
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(definition)).ToArray();
        try
        {
            using (var writer = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writer.Write(bytes);
            }

            using var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var read = new byte[bytes.Length + 1];
            if (reader.ReadAtLeast(read, read.Length, throwOnEndOfStream: false) != bytes.Length || !read.AsSpan(0, bytes.Length).SequenceEqual(bytes))
            {
                throw new InvalidOperationException("The task file changed before Task Scheduler read it.");
            }

            Run(["/Create", "/TN", taskPath, "/XML", file, "/F"]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Whether the task at <paramref name="taskPath"/> exists and this user can see it.</summary>
    public static bool Exists(string taskPath) => TryRun(["/Query", "/TN", taskPath], out _);

    /// <summary>Deletes the task at <paramref name="taskPath"/> if it exists.</summary>
    /// <exception cref="InvalidOperationException">Task Scheduler refused it.</exception>
    public static void Delete(string taskPath)
    {
        if (Exists(taskPath))
        {
            Run(["/Delete", "/TN", taskPath, "/F"]);
        }
    }

    /// <summary>Returns the paths of the tasks in <paramref name="folder"/>, such as <c>\Folder\</c>.</summary>
    public static IReadOnlyList<string> List(string folder)
    {
        if (!TryRun(["/Query", "/TN", folder, "/FO", "CSV", "/NH"], out var output))
        {
            return [];
        }

        // Each line starts with the task's path in quotes.
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('"'))
            .Select(line => line[1..line.IndexOf('"', 1)])
            .Where(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Runs the task at <paramref name="taskPath"/> now. Returns false when it doesn't exist or
    /// this user may not run it.
    /// </summary>
    public static bool RunNow(string taskPath) => TryRun(["/Run", "/TN", taskPath], out _);

    private static void Run(string[] arguments)
    {
        if (!TryRun(arguments, out var output))
        {
            throw new InvalidOperationException($"schtasks {arguments[0]} failed: {output.Trim()}");
        }
    }

    private static bool TryRun(string[] arguments, out string output)
    {
        var startInfo = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("schtasks didn't start.");
        var error = process.StandardError.ReadToEndAsync();
        output = process.StandardOutput.ReadToEnd() + error.Result;
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
