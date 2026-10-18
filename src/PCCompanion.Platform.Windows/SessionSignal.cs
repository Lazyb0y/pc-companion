using System;
using System.IO;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A named signal between processes of the user session, such as the host asking the settings
/// app to close. Any program running as the user can send it, so it may only ask for something
/// harmless.
/// </summary>
public sealed class SessionSignal : IDisposable
{
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle _wait;

    private SessionSignal(EventWaitHandle signal, Action received)
    {
        _signal = signal;
        _wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => received(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Calls <paramref name="received"/> on a thread-pool thread each time the signal named
    /// <paramref name="name"/>, such as <c>Local\Name</c>, is sent, until disposed. Returns
    /// <see langword="null"/> when the signal can't be listened to, for example because another
    /// program holds the name.
    /// </summary>
    public static SessionSignal? Listen(string name, Action received)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(received);

        EventWaitHandle signal;
        try
        {
            signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return null;
        }

        return new SessionSignal(signal, received);
    }

    /// <summary>Sends the signal named <paramref name="name"/>. Returns false when nothing listens for it.</summary>
    public static bool Send(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var signal))
            {
                return false;
            }

            using (signal)
            {
                return signal.Set();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _signal.Dispose();
    }
}
