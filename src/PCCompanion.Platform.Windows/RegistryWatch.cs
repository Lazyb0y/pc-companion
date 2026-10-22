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
/// it. A key that doesn't exist yet is watched through the nearest key above it that does, for its
/// subkeys created or deleted only, so it reports when the key is created without reporting every
/// change below it; from then on the key itself is watched. A watched key that's deleted is
/// replaced the same way. Windows can signal one change more than once, such as twice for a change
/// of the proxy settings.
/// </summary>
public sealed class RegistryWatch : IDisposable
{
    private const REG_NOTIFY_FILTER Changes = REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET
        // The watch is set again on whichever thread pool thread runs the callback, and must
        // outlast that thread.
        | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC;

    // For a key above the one asked for: its subkeys created or deleted, which is how the key asked
    // for, or one on the way to it, appears.
    private const REG_NOTIFY_FILTER Subkeys = REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC;

    // Keys created and deleted all the time mustn't keep a thread pool thread looking.
    private const int MostTries = 10;

    private readonly RegistryKey _root;
    private readonly string _path;
    private readonly Action _changed;
    private readonly AutoResetEvent _signal = new(false);
    private readonly RegisteredWaitHandle _wait;
    private readonly Lock _gate = new();
    private RegistryKey? _key;
    private string _keyPath = string.Empty;
    private bool _watching;
    private bool _disposed;

    /// <param name="root">The key the path starts from, such as <see cref="Registry.CurrentUser"/>.</param>
    /// <param name="path">The key to watch, below <paramref name="root"/>.</param>
    /// <param name="changed">
    /// Called on a thread pool thread after a change, never on two threads at once, and also when
    /// another subkey of the key above is created or deleted. It must not throw, and must not
    /// dispose the watch.
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
        var result = WatchNearest();
        if (result != WIN32_ERROR.NO_ERROR)
        {
            _key?.Dispose();
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

        _key?.Dispose();
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
            // Above the key asked for, a key on the way to it may have been created, and a watched
            // key may have been deleted: then watch the nearest key that exists now. The root
            // itself can always be watched.
            if (_keyPath != _path || Watch() != WIN32_ERROR.NO_ERROR)
            {
                _watching = WatchNearest() == WIN32_ERROR.NO_ERROR;
            }

            // Inside the lock: a signal during the callback queues another on a second thread,
            // which waits here for this one to finish.
            _changed();
        }
    }

    // Watches the key asked for, or the nearest key above it that exists. Looks again once the
    // watch is set, so a key created or deleted meanwhile isn't missed.
    private WIN32_ERROR WatchNearest()
    {
        var result = WIN32_ERROR.NO_ERROR;
        for (var tries = 0; tries < MostTries; tries++)
        {
            _key?.Dispose();
            (_key, _keyPath) = OpenNearest();
            result = Watch();
            if (result != WIN32_ERROR.ERROR_KEY_DELETED && (result != WIN32_ERROR.NO_ERROR || _keyPath == _path || NearestPath() == _keyPath))
            {
                break;
            }
        }

        return result;
    }

    private WIN32_ERROR Watch() => _keyPath == _path
        ? PInvoke.RegNotifyChangeKeyValue(_key!.Handle, true, Changes, _signal.SafeWaitHandle, true)
        : PInvoke.RegNotifyChangeKeyValue(_key!.Handle, false, Subkeys, _signal.SafeWaitHandle, true);

    private string NearestPath()
    {
        var (key, path) = OpenNearest();
        key.Dispose();
        return path;
    }

    // The key at the path, or the nearest key above it that exists, down to the root itself.
    private (RegistryKey Key, string Path) OpenNearest()
    {
        var path = _path;
        while (true)
        {
            if (_root.OpenSubKey(path, RegistryRights.Notify) is { } key)
            {
                return (key, path);
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
