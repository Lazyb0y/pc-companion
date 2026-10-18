using System;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The named lock that keeps one instance of a program running in the user's session.
/// </summary>
public static class InstanceLock
{
    /// <summary>
    /// Whether a process holds the lock named <paramref name="name"/>, such as <c>Local\Name</c>.
    /// A lock this process may not open counts as held. Whether the holder runs as administrator
    /// can't be told from the lock: processes without admin rights can open its lock too.
    /// </summary>
    public static bool IsHeld(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        try
        {
            if (!Mutex.TryOpenExisting(name, out var instanceLock))
            {
                return false;
            }

            instanceLock.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
