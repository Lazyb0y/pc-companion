using System;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Threading;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Calls back on the thread pool when a registry key, or a value or subkey in it, changes, until
/// disposed. No thread waits in between: Windows signals an event, and a thread pool wait serves
/// it. A key that doesn't exist yet is watched through the nearest key above it that does, which
/// also reports when the key is created; a watched key that's deleted is replaced the same way.
/// Windows can signal one change more than once, such as twice for a change of the proxy settings.
/// </summary>
public sealed class RegistryWatch : IDisposable
{
    private const REG_NOTIFY_FILTER Changes = REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET
        // The watch is set again on whichever thread pool thread runs the callback, and must
        // outlast that thread.
        | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC;

    private readonly RegistryKey _root;
    private readonly string _path;
    private readonly Action _changed;
    private readonly AutoResetEvent _signal = new(false);
    private readonly RegisteredWaitHandle _wait;
    private readonly Lock _gate = new();
    private RegistryKey _key;
    private bool _watching;
    private bool _disposed;

    /// <param name="root">The key the path starts from, such as <see cref="Registry.CurrentUser"/>.</param>
    /// <param name="path">The key to watch, below <paramref name="root"/>.</param>
    /// <param name="changed">
    /// Called on a thread pool thread after a change, never on two threads at once. It must not
    /// throw, and must not dispose the watch.
    /// </param>
    /// <exception cref="Win32Exception">Windows refused the watch.</exception>
    public RegistryWatch(RegistryKey root, string path, Action changed)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(changed);

        _root = root;
        _path = path;
        _changed = changed;
        _key = OpenNearest();
        var result = Watch();
        if (result != WIN32_ERROR.NO_ERROR)
        {
            _key.Dispose();
            _signal.Dispose();
            throw new Win32Exception((int)result);
        }

        _watching = true;
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, OnSignal, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Stops the watch. A callback that's running finishes first, so it should be short, and none
    /// comes after this returns.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        using (var unregistered = new ManualResetEvent(false))
        {
            if (_wait.Unregister(unregistered))
            {
                unregistered.WaitOne();
            }
        }

        _key.Dispose();
        _signal.Dispose();
    }

    private void OnSignal(object? state, bool timedOut)
    {
        lock (_gate)
        {
            if (_disposed || !_watching)
            {
                return;
            }

            // Set the watch again before the callback, so a change while it runs is signaled too.
            if (Watch() != WIN32_ERROR.NO_ERROR)
            {
                // The key was deleted: watch the nearest key above it, which also reports when
                // it's created again. The root itself can always be watched.
                _key.Dispose();
                _key = OpenNearest();
                _watching = Watch() == WIN32_ERROR.NO_ERROR;
            }

            // Inside the lock: a signal during the callback queues another on a second thread,
            // which waits here for this one to finish.
            _changed();
        }
    }

    private WIN32_ERROR Watch() => PInvoke.RegNotifyChangeKeyValue(_key.Handle, true, Changes, _signal.SafeWaitHandle, true);

    // The key at the path, or the nearest key above it that exists, down to the root itself.
    private RegistryKey OpenNearest()
    {
        var path = _path;
        while (true)
        {
            if (_root.OpenSubKey(path, RegistryRights.Notify) is { } key)
            {
                return key;
            }

            if (path.Length == 0)
            {
                throw new Win32Exception((int)WIN32_ERROR.ERROR_FILE_NOT_FOUND);
            }

            var parent = path.LastIndexOf('\\');
            path = parent < 0 ? string.Empty : path[..parent];
        }
    }
}
