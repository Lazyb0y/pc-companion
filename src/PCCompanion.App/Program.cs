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
    private static void Main()
    {
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            Foreground.AllowProcess(instance.ProcessId);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();

            // Off the main thread, which is a single-threaded apartment that would otherwise block the call.
            Task.Run(async () => await instance.RedirectActivationToAsync(activation)).Wait();
            return;
        }

        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
