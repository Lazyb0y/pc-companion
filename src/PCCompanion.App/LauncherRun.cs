using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Core;
using PCCompanion.Modules.Input.LauncherActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

/// <summary>
/// Runs <c>PCCompanion.exe --run &lt;feature&gt; &lt;action&gt; [&lt;target&gt;]</c>: passes the
/// action to the host, which it starts first when none runs, and exits with the host's answer,
/// without starting WinUI or opening a window. See "The command" in
/// docs/features/input/launcher-actions.md.
/// </summary>
internal static class LauncherRun
{
    // How long the command waits for the pipe of a host it started or that's starting, and of a
    // host that runs, which serves it only while Launcher Actions is on, as just after it was turned on.
    private static readonly TimeSpan StartingHostWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RunningHostWait = TimeSpan.FromSeconds(1);

    // The host serves the pipe as it applies its settings, a moment before it creates its window.
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    // How long the host may take to answer, as while its UI thread is busy.
    private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(5);

    /// <summary>Whether <paramref name="arguments"/> run an action, rather than open the settings app.</summary>
    public static bool IsCommand(string[] arguments) => arguments is [LauncherCommand.RunArgument, ..];

    /// <summary>
    /// Runs the action that the arguments after <see cref="LauncherCommand.RunArgument"/> name, and
    /// returns the exit code. Call it only when <see cref="IsCommand"/> says so.
    /// </summary>
    public static LauncherExitCode Run(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (LauncherCommand.Read(arguments[1..]) is not { } action)
        {
            return LauncherExitCode.NotACommand;
        }

        if (!WaitForPipe(LauncherPipe.Name))
        {
            return LauncherExitCode.Unreachable;
        }

        // As the program the user just started, the command may let the host bring a window to the
        // front, which End App, Drop-down Apps, Clipboard Actions' menu and Display Profiles' question need.
        WaitUntil(() => MessageWindow.FindOwnerProcess(HostProcess.WindowClassName) is not null, WindowWait);
        MessageWindow.AllowOwnerToComeToFront(HostProcess.WindowClassName);

        // Off the main thread, which is a single-threaded apartment.
        var answer = Task.Run(() => LauncherPipe.AskAsync(LauncherPipe.Name, action, AnswerWait)).GetAwaiter().GetResult();
        return LauncherCommand.ExitCodeOf(answer);
    }

    // Starts the host when none runs, and waits for the pipe it serves while Launcher Actions is on.
    private static bool WaitForPipe(string name)
    {
        if (RequestPipe.IsServed(name))
        {
            return true;
        }

        TimeSpan wait;
        if (InstanceLock.IsHeld(HostProcess.InstanceLockName))
        {
            // A host without its window yet is starting.
            wait = MessageWindow.FindOwnerProcess(HostProcess.WindowClassName) is null ? StartingHostWait : RunningHostWait;
        }
        else if (StartHost())
        {
            wait = StartingHostWait;
        }
        else
        {
            return false;
        }

        return WaitUntil(() => RequestPipe.IsServed(name), wait);
    }

    // As the settings app starts it: the host itself hands over to Always run as administrator's task.
    private static bool StartHost()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(HostProcess.ExecutablePath) { UseShellExecute = false });
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan wait)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed >= wait)
            {
                return false;
            }

            Thread.Sleep(Poll);
        }

        return true;
    }
}
