using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// Removes what Remote Folders registered for the user, as setup uninstalls PC Companion: the
/// servers' folders in File Explorer, which would show with nothing behind them, and, unless the
/// settings stay, the passwords and passphrases in Credential Manager. Files downloaded to the PC
/// stay in what are then ordinary folders. See "Installer" in docs/features/files/remote-folders.md.
/// </summary>
public static class RemoteFoldersRemoval
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Stops the provider, then removes the user's folders and, unless <paramref name="keepSettings"/>, their credentials. Never throws.</summary>
    public static void RemoveForCurrentUser(bool keepSettings)
    {
        StopProvider();

        try
        {
            string prefix = RemoteFoldersProcess.ProviderName + "!" + Elevation.CurrentUserSid + "!";
            foreach (var (id, _) in SyncRoots.Current(prefix))
            {
                try
                {
                    SyncRoots.Unregister(id);
                }
                catch (Exception exception) when (exception is COMException or ArgumentException or UnauthorizedAccessException)
                {
                    // The folder stays registered; the next install's provider removes it.
                }
            }
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            // Windows couldn't list them.
        }

        if (keepSettings)
        {
            // The servers stay in the settings, so their passwords stay with them.
            return;
        }

        try
        {
            foreach (string target in CredentialStore.Targets(RemoteFoldersProcess.CredentialPrefix))
            {
                CredentialStore.Delete(target);
            }
        }
        catch (Win32Exception)
        {
            // Left for the user to remove in Credential Manager.
        }
    }

    // A connected folder can't be unregistered: the provider stops first, and its lock tells when it's gone.
    private static void StopProvider()
    {
        if (!SessionSignal.Send(RemoteFoldersProcess.StopSignalName))
        {
            return;
        }

        var waited = Stopwatch.StartNew();
        while (IsRunning() && waited.Elapsed < StopTimeout)
        {
            Thread.Sleep(100);
        }
    }

    private static bool IsRunning()
    {
        if (Mutex.TryOpenExisting(RemoteFoldersProcess.InstanceLockName, out var instanceLock))
        {
            instanceLock.Dispose();
            return true;
        }

        return false;
    }
}
