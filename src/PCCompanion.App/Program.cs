using System;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

internal static class Program
{
    // The first settings app in the user session registers this key; later launches hand over to it.
    private const string InstanceKey = "PCCompanion.App";

    [STAThread]
    private static int Main(string[] arguments)
    {
        // The command runs an action through the host and exits, before WinUI starts.
        if (LauncherRun.IsCommand(arguments))
        {
            return (int)LauncherRun.Run(arguments);
        }

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            Foreground.AllowProcess(instance.ProcessId);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();

            // Off the main thread, which is a single-threaded apartment that would otherwise block the call.
            Task.Run(async () => await instance.RedirectActivationToAsync(activation)).Wait();
            return 0;
        }

        XamlGeneratedProgram.XamlGeneratedMain();
        return 0;
    }
}
