using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Closes the settings app and the host running in this session, as Exit in the tray menu does, and
/// waits until their processes end. Setup runs the host with <see cref="HostProcess.ExitArgument"/>
/// before Windows Installer looks for files in use, so it doesn't ask to close them.
/// </summary>
internal static class ExitCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void Run()
    {
        // A host that's just starting runs before it creates the window that gets the message.
        var waited = Stopwatch.StartNew();
        while (MessageWindow.FindOwnerProcess(HostProcess.WindowClassName) is null
            && IsAnotherHostRunning()
            && waited.Elapsed < Timeout)
        {
            Thread.Sleep(100);
        }

        // Found before they're asked to exit, so these are the processes that were asked.
        var processes = FindRunning();
        try
        {
            SessionSignal.Send(SettingsAppProcess.ExitSignalName);
            MessageWindow.PostToWindow(HostProcess.WindowClassName, TrayController.ExitMessage);

            foreach (var process in processes)
            {
                WaitForExit(process, Timeout - waited.Elapsed);
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<Process> FindRunning()
    {
        var processes = new List<Process>();
        if (MessageWindow.FindOwnerProcess(HostProcess.WindowClassName) is { } hostId)
        {
            try
            {
                processes.Add(Process.GetProcessById((int)hostId));
            }
            catch (ArgumentException)
            {
                // It just ended.
            }
        }

        processes.AddRange(FindInSession(SettingsAppProcess.FileName));
        return processes;
    }

    private static bool IsAnotherHostRunning()
    {
        var hosts = FindInSession(HostProcess.FileName);
        foreach (var host in hosts)
        {
            host.Dispose();
        }

        return hosts.Count > 0;
    }

    // The processes of a program in this session, other than this one.
    private static List<Process> FindInSession(string fileName)
    {
        using var current = Process.GetCurrentProcess();
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fileName)))
        {
            if (process.SessionId == current.SessionId && process.Id != current.Id)
            {
                found.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return found;
    }

    private static void WaitForExit(Process process, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            process.WaitForExit(timeout);
        }
        catch (Win32Exception)
        {
            // Without the right to wait on it, as for a host running as administrator, watch for it to end.
            var waited = Stopwatch.StartNew();
            while (IsRunning(process.Id) && waited.Elapsed < timeout)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
