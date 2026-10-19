using System;
using System.ComponentModel;
using System.IO;
using PCCompanion.Core;
using PCCompanion.Infrastructure;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Removes the user's data folder as setup uninstalls PC Companion. Setup runs the host with
/// <see cref="HostProcess.RemoveDataArgument"/>, and <see cref="HostProcess.KeepSettingsArgument"/>
/// to keep the settings file, as the user who uninstalls it.
/// </summary>
internal static class RemoveDataCommand
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, HostProcess.FileName);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    public static void Run(bool keepSettings)
    {
        // Any program of the user's can change the folder. Setup started as administrator runs this
        // with admin rights too, so it runs again without them, as the same user: a link planted in
        // the folder can then lead only to what the user could remove anyway.
        if (Elevation.IsUacElevated)
        {
            var arguments = keepSettings
                ? $"{HostProcess.RemoveDataArgument} {HostProcess.KeepSettingsArgument}"
                : HostProcess.RemoveDataArgument;
            try
            {
                UnelevatedProcess.Run(HostPath, arguments, Timeout);
            }
            catch (Win32Exception)
            {
                // Without a shell of the same user, without admin rights, to take them from, the folder stays.
            }

            return;
        }

        UserDataRemoval.Remove(AppDataPaths.ForCurrentUser(), keepSettings);
    }
}
